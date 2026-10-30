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

    /// <summary>Current provider, connection/QR state and enforced safety counters.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<WhatsAppSessionInfo>>> Get(CancellationToken ct)
        => OkEnvelope(await _sessions.GetStatusAsync(ct));

    /// <summary>
    /// Starts/restores linked-device login. A QR is never generated until an
    /// administrator explicitly accepts the compliance and unofficial-client notice.
    /// </summary>
    [HttpPost("connect")]
    public async Task<ActionResult<ApiResponse<WhatsAppSessionInfo>>> Connect(
        WhatsAppConnectRequest request,
        CancellationToken ct)
    {
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
