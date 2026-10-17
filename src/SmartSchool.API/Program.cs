using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using SmartSchool.API.Hubs;
using SmartSchool.API.Middleware;
using SmartSchool.API.Services;
using SmartSchool.API.Validators;
using SmartSchool.Core.Interfaces;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Infrastructure.Repositories;
using SmartSchool.Infrastructure.Services;
using SmartSchool.Infrastructure.Services.Push;
using SmartSchool.Infrastructure.Services.WhatsApp;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Helpers;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ----------------------------- Serilog ---------------------------------
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ----------------------------- Database --------------------------------
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

    builder.Services.AddDbContext<SchoolDbContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

    // ----------------------------- Controllers -----------------------------
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Enums travel as strings ("PickIn" instead of 1).
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    // ----------------------------- FluentValidation ------------------------
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

    // Validation failures also use the uniform envelope:
    // { "success": false, "message": "Validation failed.", "errors": [ ... ] }
    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(kvp => kvp.Value is { Errors.Count: > 0 })
                .SelectMany(kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage))
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            return new BadRequestObjectResult(ApiResponse.Fail("Validation failed.", errors));
        };
    });

    // ----------------------------- JWT auth --------------------------------
    var jwtKey = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException("Jwt:Key is not configured.");
    if (jwtKey.Length < 32)
        throw new InvalidOperationException("Jwt:Key must be at least 32 characters (HMAC-SHA256).");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Jwt:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            };

            // SignalR clients (WPF/Flutter) send the token as ?access_token={jwt}.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    // ----------------------------- SignalR + CORS ---------------------------
    builder.Services.AddSignalR();

    builder.Services.AddCors(options => options.AddPolicy("clients", policy => policy
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    // ----------------------------- Swagger ----------------------------------
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "SmartSchool Pro API",
            Version = "v1",
            Description = "Backend for SmartSchool Pro: WPF admin desktop app, Flutter driver/parent apps, " +
                          "real-time bus tracking and WhatsApp parent alerts. All timestamps are Pakistan time (UTC+5)."
        });
        var bearer = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Description = "Enter: Bearer {your JWT token}",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        };
        c.AddSecurityDefinition("Bearer", bearer);
        c.AddSecurityRequirement(new OpenApiSecurityRequirement { { bearer, Array.Empty<string>() } });
    });

    // ----------------------------- App services -----------------------------
    builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
    builder.Services.AddSingleton<ISchoolNotificationService, SchoolNotificationService>();

    builder.Services.AddSingleton<InMemoryWhatsAppQueue>();
    builder.Services.AddSingleton<IWhatsAppQueue>(sp => sp.GetRequiredService<InMemoryWhatsAppQueue>());
    builder.Services.AddHostedService<WhatsAppQueueHostedService>();

    builder.Services.AddHttpClient<IWhatsAppSender, TwilioWhatsAppService>();
    builder.Services.AddHttpClient<IGeocodingService, GoogleMapsGeocodingService>();

    // Firebase Cloud Messaging: dry-run until Firebase:Enabled=true + credentials JSON path set.
    builder.Services.AddSingleton<IFcmSender, FirebaseFcmSender>();

    // Self-update feed: school server proxies GitHub Releases for WPF/APK auto-updates.
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<GitHubReleaseProvider>();

    // ThingWorx (IoT portal) integration — bus locations relay into SignalR.
    builder.Services.Configure<SmartSchool.API.Services.IoT.ThingWorxOptions>(
        builder.Configuration.GetSection("ThingWorx"));
    builder.Services.AddHttpClient<SmartSchool.API.Services.IoT.ThingWorxClient>();
    builder.Services.AddHostedService<SmartSchool.API.Services.IoT.ThingWorxRelayService>();

    var app = builder.Build();

    // ----------------------------- DB init + seed ---------------------------
    // NEVER die on DB problems (LocalDB cold start, missing install, etc.):
    // retry for ~2 minutes, then stay UP and let endpoints report the error.
    var dbInitOk = false;
    for (var attempt = 1; attempt <= 12 && !dbInitOk; attempt++)
    {
        try
        {
            await DbInitializer.InitializeAsync(app.Services, app.Configuration, app.Logger);
            dbInitOk = true;
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Database init attempt {Attempt}/12 failed; retrying in 10s...", attempt);
            if (attempt < 12) await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
    if (!dbInitOk)
        app.Logger.LogCritical("Database init could not complete — API is staying UP so clients see the real error instead of connection-refused.");

    // ----------------------------- Pipeline ---------------------------------
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartSchool Pro API v1");
        c.DocumentTitle = "SmartSchool Pro API";
    });

    app.UseSerilogRequestLogging(options =>
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms");

    app.UseCors("clients");

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<SchoolNotificationHub>("/hubs/notifications");

    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "ok",
        serverTimeUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
        serverTimePakistan = PkTime.NowPk.ToString("yyyy-MM-dd hh:mm:ss tt 'PST'")
    })).AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (!ex.GetType().Name.Equals("HostAbortedException", StringComparison.Ordinal))
{
    Log.Fatal(ex, "SmartSchool.API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
