using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.API.Services;
using SmartSchool.Shared.Constants;

namespace SmartSchool.API.Controllers;

/// <summary>Staff-only QR test and separately confirmed real-message activation.</summary>
[ApiController]
[Route("api/whatsapp-qr-test")]
[Authorize(Roles = AppRoles.Staff)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WhatsAppQrTestController(QrTestGatewayService gateway) : BaseApiController
{
    // Trust the actual socket address, not X-Forwarded-For. If accessed over the
    // LAN, real TLS must be configured at the API. Never expose a pairing QR or
    // test recipient over the installer's default cleartext LAN connection.
    private bool IsSecureRequest()
    {
        // This test screen is a desktop client, not a browser API. The app's
        // general CORS policy permits LAN clients, so reject browser-origin and
        // fetch-metadata requests here even when they reach localhost/HTTPS.
        if (Request.Headers.ContainsKey("Origin") || Request.Headers.ContainsKey("Sec-Fetch-Site")) return false;
        if (Request.IsHttps) return true;
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote is null) return false;
        return IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);
    }

    private ActionResult InsecureRequest() => StatusCode(StatusCodes.Status403Forbidden,
        SmartSchool.Shared.Common.ApiResponse.Fail("QR test ke liye server PC par admin desktop app ya HTTPS istemal karein; browser requests allowed nahi."));

    [HttpGet("status")]
    public async Task<ActionResult> Status(CancellationToken ct)
        => IsSecureRequest() ? OkEnvelope(await gateway.StatusAsync(ct)) : InsecureRequest();

    [HttpGet("qr")]
    public async Task<ActionResult> Qr(CancellationToken ct)
        => IsSecureRequest() ? OkEnvelope(await gateway.QrAsync(ct)) : InsecureRequest();

    [HttpPost("start")]
    public async Task<ActionResult> Start([FromBody] QrStartRequest request, CancellationToken ct)
    {
        if (!IsSecureRequest()) return InsecureRequest();
        if (!request.RiskConfirmed) return BadRequestEnvelope("Test-account risk confirmation is required.");
        return OkEnvelope(await gateway.StartAsync(true, ct));
    }

    [HttpPost("send")]
    public async Task<ActionResult> Send([FromBody] QrSendRequest request, CancellationToken ct)
    {
        if (!IsSecureRequest()) return InsecureRequest();
        if (!request.ConsentConfirmed || request.RequestId == Guid.Empty ||
            request.Type is not ("pick" or "drop") ||
            !Regex.IsMatch(request.Phone ?? "", @"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant))
            return BadRequestEnvelope("Valid international test number, sample type and recipient consent are required.");
        return OkEnvelope(await gateway.SendAsync(request.Phone!, request.Type!, true, request.RequestId, ct));
    }

    [HttpPost("activate-live")]
    public async Task<ActionResult> Activate([FromBody] QrActivateRequest request, CancellationToken ct)
    {
        if (!IsSecureRequest()) return InsecureRequest();
        if (!request.TestReceived || !request.RiskConfirmed)
            return BadRequestEnvelope("Confirm successful receipt and the live sending risk first.");
        return OkEnvelope(await gateway.ActivateAsync(true, true, ct));
    }

    [HttpPost("deactivate-live")]
    public async Task<ActionResult> Deactivate([FromBody] QrUnlinkRequest request, CancellationToken ct)
    {
        if (!IsSecureRequest()) return InsecureRequest();
        if (!request.Confirm) return BadRequestEnvelope("Confirm first.");
        return OkEnvelope(await gateway.DeactivateAsync(ct));
    }

    [HttpPost("unlink")]
    public async Task<ActionResult> Unlink([FromBody] QrUnlinkRequest request, CancellationToken ct)
    {
        if (!IsSecureRequest()) return InsecureRequest();
        if (!request.Confirm) return BadRequestEnvelope("Confirm unlink first.");
        return OkEnvelope(await gateway.UnlinkAsync(ct));
    }
}

public sealed record QrStartRequest(bool RiskConfirmed);
public sealed record QrSendRequest(string? Phone, string? Type, bool ConsentConfirmed, Guid RequestId);
public sealed record QrUnlinkRequest(bool Confirm);

public sealed record QrActivateRequest(bool TestReceived, bool RiskConfirmed);
