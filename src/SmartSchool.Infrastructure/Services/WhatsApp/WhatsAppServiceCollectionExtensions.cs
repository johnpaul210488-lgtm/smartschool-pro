using Microsoft.Extensions.DependencyInjection;
using SmartSchool.Core.Interfaces;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>The Twilio SDK owns its HTTP client; this is not a typed HttpClient.</summary>
    public static IServiceCollection AddWhatsAppSender(this IServiceCollection services)
        => services.AddTransient<IWhatsAppSender, TwilioWhatsAppService>();
}
