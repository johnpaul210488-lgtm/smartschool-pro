using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;

namespace SmartSchool.API.Controllers;

[ApiController]
[Route("api/whatsapp/session")]
[Authorize(Roles = AppRoles.Staff)]
public sealed class WhatsAppSessionController : BaseApiController
{
    private readonly IWhatsAppSessionManager _sessions;

    public WhatsAppSessionController(IWhatsAppSessionManager sessions) => _sessions = sessions;

    private bool IsSecureAdminChannel()
    {
        // QR/session material is for the native admin app, never browser-origin fetch.
        if (Request.Headers.ContainsKey("Origin") || Request.Headers.ContainsKey("Sec-Fetch-Site"))
            return false;
        if (Request.IsHttps) return true;
        var remote = HttpContext.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(
            remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);
    }

    private ActionResult<ApiResponse<WhatsAppSessionInfo>> InsecureChannel()
        => StatusCode(StatusCodes.Status403Forbidden,
            ApiResponse<WhatsAppSessionInfo>.Fail(
                "Open WhatsApp Connection in the admin app on the server PC, or configure trusted HTTPS."));

    /// <summary>Current provider, connection/QR state and enforced safety counters.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<WhatsAppSessionInfo>>> Get(CancellationToken ct)
    {
        if (!IsSecureAdminChannel()) return InsecureChannel();
        return OkEnvelope(await _sessions.GetStatusAsync(ct));
    }

    /// <summary>
    /// Starts/restores linked-device login. A QR is never generated until an
    /// administrator explicitly accepts the compliance and unofficial-client notice.
    /// </summary>
    [HttpPost("connect")]
    public async Task<ActionResult<ApiResponse<WhatsAppSessionInfo>>> Connect(
        WhatsAppConnectRequest request,
        CancellationToken ct)
    {
        if (!IsSecureAdminChannel()) return InsecureChannel();
        if (!request.AcceptTermsAndRisk)
            return BadRequestEnvelope(
                "Read and accept the WhatsApp terms/compliance notice before generating a QR code.");

        var result = await _sessions.ConnectAsync(request.AcceptTermsAndRisk, ct);
        if (!result.Success)
        {
            if (!result.Session.IsAvailable)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<WhatsAppSessionInfo>.Fail(result.Error ?? "Linked-device gateway unavailable."));
            return BadRequest(ApiResponse<WhatsAppSessionInfo>.Fail(result.Error ?? "Connection could not start."));
        }
        return OkEnvelope(result.Session, "WhatsApp connection started. Scan the current QR from Linked Devices.");
    }

    /// <summary>Unlinks the phone and securely erases local session credentials.</summary>
    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<WhatsAppSessionInfo>>> Logout(
        WhatsAppLogoutRequest request,
        CancellationToken ct)
    {
        if (!IsSecureAdminChannel()) return InsecureChannel();
        if (!request.ConfirmUnlink)
            return BadRequestEnvelope("Explicit unlink confirmation is required.");

        var result = await _sessions.LogoutAsync(request.ConfirmUnlink, ct);
        if (!result.Success)
        {
            if (!result.Session.IsAvailable)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<WhatsAppSessionInfo>.Fail(result.Error ?? "Linked-device gateway unavailable."));
            return BadRequest(ApiResponse<WhatsAppSessionInfo>.Fail(result.Error ?? "Device could not be unlinked."));
        }
        return OkEnvelope(result.Session, "WhatsApp device unlinked and encrypted local session erased.");
    }
}

public sealed class WhatsAppConnectRequest
{
    public bool AcceptTermsAndRisk { get; set; }
}

public sealed class WhatsAppLogoutRequest
{
    public bool ConfirmUnlink { get; set; }
}
