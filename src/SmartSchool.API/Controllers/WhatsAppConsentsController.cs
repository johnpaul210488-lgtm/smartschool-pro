using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

/// <summary>Records an actual recipient's permission; an old default toggle is not consent.</summary>
[ApiController]
[Authorize(Roles = AppRoles.Staff)]
[Route("api/whatsapp-consents")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WhatsAppConsentsController(IUnitOfWork uow) : BaseApiController
{
    private bool Secure()
    {
        if (Request.Headers.ContainsKey("Origin") || Request.Headers.ContainsKey("Sec-Fetch-Site")) return false;
        if (Request.IsHttps) return true;
        var ip = HttpContext.Connection.RemoteIpAddress;
        return ip is not null && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
    }
    private ActionResult Insecure() => StatusCode(403, SmartSchool.Shared.Common.ApiResponse.Fail(
        "Consent ke liye server PC ki admin app ya HTTPS istemal karein."));

    [HttpGet("student/{studentId:int}")]
    public async Task<ActionResult> Student(int studentId, CancellationToken ct)
    {
        if (!Secure()) return Insecure();
        var student = await uow.Students.Query().FirstOrDefaultAsync(s => s.Id == studentId && s.IsActive, ct);
        if (student is null) return NotFoundEnvelope("Active student not found.");
        var consents = await uow.WhatsAppConsents.Query().Where(c => c.StudentId == studentId).ToListAsync(ct);
        var parentSettings = await uow.ParentStudents.Query().Where(p => p.StudentId == studentId)
            .Select(p => new { p.ParentUser.Phone, p.ParentUser.ReceiveWhatsApp }).ToListAsync(ct);
        var contacts = new[] { (Label: "Father", Phone: student.FatherPhone), (Label: "Mother", Phone: student.MotherPhone) }
            .Select(x => new { x.Label, Phone = PhoneNormalizer.ToE164(x.Phone) })
            .Where(x => x.Phone is not null).DistinctBy(x => x.Phone)
            .Select(x => new WhatsAppConsentContact(x.Label, x.Phone!,
                consents.Any(c => c.PhoneE164 == x.Phone && c.Granted),
                parentSettings.Any(p => PhoneNormalizer.ToE164(p.Phone) == x.Phone && !p.ReceiveWhatsApp)))
            .ToList();
        return OkEnvelope(new WhatsAppConsentStudent(student.Id, student.FullName, contacts));
    }

    [HttpPost]
    public async Task<ActionResult> Set([FromBody] WhatsAppConsentRequest request, CancellationToken ct)
    {
        if (!Secure()) return Insecure();
        var phone = PhoneNormalizer.ToE164(request.Phone);
        if (request.StudentId <= 0 || phone is null || (request.Granted && !request.ConsentConfirmed))
            return BadRequestEnvelope("Student, valid number and the recipient's explicit permission are required.");
        var student = await uow.Students.Query().FirstOrDefaultAsync(s => s.Id == request.StudentId && s.IsActive, ct);
        if (student is null) return NotFoundEnvelope("Active student not found.");
        var row = await uow.WhatsAppConsents.Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.StudentId == request.StudentId && c.PhoneE164 == phone, ct);
        if (request.Granted && phone != PhoneNormalizer.ToE164(student.FatherPhone) && phone != PhoneNormalizer.ToE164(student.MotherPhone))
            return BadRequestEnvelope("Number must match this student's current father or mother phone.");
        if (row is null)
        {
            if (!request.Granted) return OkMessage("No consent was recorded for this number.");
            row = new() { StudentId = request.StudentId, PhoneE164 = phone };
            await uow.WhatsAppConsents.AddAsync(row, ct);
        }
        row.Granted = request.Granted;
        row.ChangedAtUtc = DateTime.UtcNow;
        row.ChangedByUserId = CallerUserId;
        await uow.SaveChangesAsync(ct);
        return OkMessage(request.Granted ? "Consent recorded. A parent opt-out still takes precedence." : "Consent revoked.");
    }
}

public sealed record WhatsAppConsentRequest(int StudentId, string? Phone, bool Granted, bool ConsentConfirmed);
public sealed record WhatsAppConsentStudent(int StudentId, string StudentName, IReadOnlyList<WhatsAppConsentContact> Contacts);
public sealed record WhatsAppConsentContact(string Label, string Phone, bool Granted, bool ParentOptedOut);
