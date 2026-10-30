using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.API.Controllers;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Constants;
using Xunit;

namespace SmartSchool.Tests;

public sealed class WhatsAppSecurityTests
{
    private sealed class Sessions : IWhatsAppSessionManager
    {
        public int StatusCalls;
        private static WhatsAppSessionInfo Status => new(
            "LinkedDevice", "disconnected", true, false, false,
            null, null, null, null, 0, null, "notice");

        public Task<WhatsAppSessionInfo> GetStatusAsync(CancellationToken ct = default)
        {
            StatusCalls++;
            return Task.FromResult(Status);
        }

        public Task<WhatsAppSessionCommandResult> ConnectAsync(bool acceptTermsAndRisk, CancellationToken ct = default)
            => Task.FromResult(new WhatsAppSessionCommandResult(true, Status, null));

        public Task<WhatsAppSessionCommandResult> LogoutAsync(bool confirmUnlink, CancellationToken ct = default)
            => Task.FromResult(new WhatsAppSessionCommandResult(true, Status, null));
    }

    [Fact]
    public void SessionAndConsentEndpointsAreOfficeStaffOnly()
    {
        var sessionAuth = Assert.Single(typeof(WhatsAppSessionController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false));
        var consentAuth = Assert.Single(typeof(WhatsAppConsentsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false));
        Assert.Equal(AppRoles.Staff, sessionAuth.Roles);
        Assert.Equal(AppRoles.Staff, consentAuth.Roles);
    }

    [Theory]
    [InlineData("10.0.0.25", false)]
    [InlineData("127.0.0.1", true)]
    public async Task SessionQrStateRejectsRemoteHttpAndBrowserOrigin(string remoteIp, bool addOrigin)
    {
        var sessions = new Sessions();
        var controller = Create(sessions, IPAddress.Parse(remoteIp), https: false);
        if (addOrigin) controller.Request.Headers["Origin"] = "https://untrusted.example";

        var action = await controller.Get(default);
        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(0, sessions.StatusCalls);
    }

    [Fact]
    public async Task NativeLoopbackAdminCanReadSessionState()
    {
        var sessions = new Sessions();
        var controller = Create(sessions, IPAddress.Loopback, https: false);
        var action = await controller.Get(default);
        Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(1, sessions.StatusCalls);
    }

    private static WhatsAppSessionController Create(Sessions sessions, IPAddress remote, bool https)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote;
        context.Request.Scheme = https ? "https" : "http";
        return new WhatsAppSessionController(sessions)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
