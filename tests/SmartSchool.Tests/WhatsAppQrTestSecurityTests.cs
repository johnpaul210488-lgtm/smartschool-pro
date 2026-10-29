using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SmartSchool.API.Controllers;
using SmartSchool.API.Services;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Constants;
using Xunit;

namespace SmartSchool.Tests;

public sealed class WhatsAppQrTestSecurityTests
{
    [Fact]
    public void QrEndpointsAreStaffOnlyAndTestGatewayIsNotTheProductionSender()
    {
        var authorization = Assert.Single(typeof(WhatsAppQrTestController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false));
        Assert.Equal(AppRoles.Staff, authorization.Roles);
        Assert.False(typeof(IWhatsAppSender).IsAssignableFrom(typeof(QrTestGatewayService)));
        var cache = Assert.Single(typeof(WhatsAppQrTestController)
            .GetCustomAttributes<ResponseCacheAttribute>(inherit: false));
        Assert.True(cache.NoStore);
    }

    [Fact]
    public void PreviewBuildDisablesTheExistingOutboxWorkerWithoutChangingNormalBuilds()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=production-example;Database=School;"
        }).Build();
#if QR_PREVIEW_ONLY
        Assert.False(PreviewSafetyPolicy.WhatsAppWorkerEnabled);
        Assert.Contains("Database=SmartSchoolQrPreview;", PreviewSafetyPolicy.ConnectionString(config));
        Assert.DoesNotContain("production-example", PreviewSafetyPolicy.ConnectionString(config));
#else
        Assert.True(PreviewSafetyPolicy.WhatsAppWorkerEnabled);
        Assert.Equal("Server=production-example;Database=School;", PreviewSafetyPolicy.ConnectionString(config));
#endif
    }

    [Fact]
    public async Task CleartextLanCannotSeeQrStartSendOrUnlinkEvenWithSpoofedForwardingHeaders()
    {
        // No service is required: every endpoint must reject before accessing it.
        var controller = CreateController(IPAddress.Parse("192.168.1.40"), "http");
        controller.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        controller.Request.Headers["X-Forwarded-Proto"] = "https";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Status(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Qr(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Start(new(true), default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Send(
            new("+923001234567", "pick", true, Guid.NewGuid()), default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Unlink(new(true), default)).StatusCode);
    }

    [Fact]
    public async Task BrowserOriginCannotAccessQrEvenViaLoopbackOrHttps()
    {
        var local = CreateController(IPAddress.Loopback, "http");
        local.Request.Headers["Origin"] = "https://untrusted.example";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await local.Qr(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await local.Start(new(true), default)).StatusCode);
        var secure = CreateController(IPAddress.Parse("192.0.2.4"), "https");
        secure.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await secure.Send(
            new("+923001234567", "pick", true, Guid.NewGuid()), default)).StatusCode);
    }

    [Fact]
    public async Task ConsentAndConfirmationAreRequiredBeforeAccessingTheGateway()
    {
        var controller = CreateController(IPAddress.Loopback, "http");
        Assert.IsType<BadRequestObjectResult>(await controller.Start(new(false), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Unlink(new(false), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Send(
            new("+923001234567", "pick", false, Guid.NewGuid()), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Send(
            new("03001234567", "pick", true, Guid.NewGuid()), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Send(
            new("+923001234567", "custom-text", true, Guid.NewGuid()), default));
    }

    private static WhatsAppQrTestController CreateController(IPAddress remote, string scheme)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote;
        context.Request.Scheme = scheme;
        return new WhatsAppQrTestController(null!) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
