using SmartSchool.Shared.Common;

namespace SmartSchool.API.Middleware;

/// <summary>
/// Last-resort catch: every unhandled exception becomes the same envelope
/// { success:false, message, errors } and is logged via Serilog.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var detail = _env.IsDevelopment() ? ex.Message : "Please contact the school administrator.";
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail("An unexpected server error occurred.", new[] { detail }));
        }
    }
}
