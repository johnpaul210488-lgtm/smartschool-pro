using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;

namespace SmartSchool.API.Services;

/// <summary>The only real sender. The gateway itself refuses an unapproved account.</summary>
public sealed class QrWhatsAppSender(QrTestGatewayService gateway) : IWhatsAppSender
{
    public Task<WhatsAppSendResult> SendAsync(WhatsAppLog log, CancellationToken ct = default)
        => gateway.SendOutboxAsync(log.CorrelationId, ct);
}
