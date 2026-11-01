using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Constants;

namespace SmartSchool.API.Controllers;

/// <summary>
/// Manage the WhatsApp Baileys gateway connection and send messages.
/// All endpoints require Staff role. Gateway runs on 127.0.0.1 only.
/// </summary>
[ApiController]
[Route("api/whatsapp")]
[Authorize(Roles = AppRoles.Staff)]
public sealed class WhatsAppController : BaseApiController
{
    private readonly IWhatsAppMessageService _whatsapp;

    public WhatsAppController(IWhatsAppMessageService whatsapp)
    {
        _whatsapp = whatsapp;
    }

    [HttpGet("status")]
    public async Task<ActionResult> Status(CancellationToken ct)
    {
        var enabled = await _whatsapp.IsEnabled();
        if (!enabled) return OkEnvelope(new { enabled = false, reason = "WhatsApp messaging is disabled in appsettings." });
        var status = await _whatsapp.GetStatus(ct);
        return OkEnvelope(new { enabled = true, status });
    }

    [HttpPost("send")]
    public async Task<ActionResult> Send([FromBody] SendRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.To) || string.IsNullOrWhiteSpace(req.Text))
            return BadRequestEnvelope("To and Text are required.");

        var result = await _whatsapp.SendText(req.To, req.Text, req.Priority, ct);
        if (!result.Ok) return BadRequestEnvelope(result.Error ?? "Send failed.");
        return OkEnvelope(new { id = result.Id, status = result.Status });
    }

    [HttpPost("send-media")]
    public async Task<ActionResult> SendMedia([FromBody] SendMediaRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.To) || string.IsNullOrWhiteSpace(req.Path))
            return BadRequestEnvelope("To and Path are required.");

        var result = await _whatsapp.SendMedia(req.To, req.Type ?? "document", req.Path, req.FileName, req.Caption, req.Priority, ct);
        if (!result.Ok) return BadRequestEnvelope(result.Error ?? "Send failed.");
        return OkEnvelope(new { id = result.Id, status = result.Status });
    }

    public sealed record SendRequest(string To, string Text, int? Priority = null);
    public sealed record SendMediaRequest(string To, string? Type, string Path, string? FileName, string? Caption, int? Priority = null);
}
