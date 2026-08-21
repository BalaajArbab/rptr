global using Microsoft.EntityFrameworkCore;
global using  dotnetrpg_again.Data;
global using rptr.Models;

using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using rptr.Data.Mongo;
using rptr.Endpoints;
using rptr.OpenApi;
using rptr.Services.Activity;
using rptr.Services.Authorization;
using rptr.Services.GuestSessions;
using rptr.Services.Feedback;
using rptr.Services.KeepWarm;
using rptr.Services.Playlists;
using rptr.Services.PlaylistSongs;
using rptr.Services.Snippets;
using rptr.Services.Spotify;

var builder = WebApplication.CreateBuilder(args);
const string FrontendCorsPolicy = "Frontend";

builder.Logging.AddAzureWebAppDiagnostics();

var applicationInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
        options.IncludeScopes = true);
    builder.Services
        .AddOpenTelemetry()
        .UseAzureMonitor(options =>
            options.ConnectionString = applicationInsightsConnectionString);
}

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("GuestLogin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("GuestFeedback", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("Activity", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("PageView", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("KeepWarm", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfterSeconds = context.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";
        context.HttpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests."
            },
            cancellationToken);
    };
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
});

builder.Services.AddRptrOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(
    GitHubWorkflowDispatchService.HttpClientName,
    client =>
    {
        client.BaseAddress = new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("rptr-api/1.0");
        client.DefaultRequestHeaders.Add(
            "X-GitHub-Api-Version",
            "2026-03-10");
    });
builder.Services.AddMemoryCache();
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection(MongoDbSettings.SectionName));
builder.Services.AddOptions<GuestSessionSettings>()
    .Bind(builder.Configuration.GetSection(GuestSessionSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<KeepWarmDispatchSettings>()
    .Bind(builder.Configuration.GetSection(KeepWarmDispatchSettings.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        settings => !settings.Enabled || !string.IsNullOrWhiteSpace(settings.Token),
        "KeepWarmDispatch:Token is required when keep-warm dispatch is enabled.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<MongoKeepWarmService>();
builder.Services.AddSingleton<MongoGuestSessionCounter>();
builder.Services.AddSingleton<IGuestSessionCounter, LazyGuestSessionCounter>();
builder.Services.AddSingleton<IGuestLeaseStore, InMemoryGuestLeaseStore>();
builder.Services.AddSingleton<GuestCredentialProtector>();
builder.Services.AddSingleton<IGuestCredentialStore, MongoGuestCredentialStore>();
builder.Services.AddSingleton<IGuestRuntimeStore, InMemoryGuestRuntimeStore>();
builder.Services.AddSingleton<IGuestFeedbackTokenService, GuestFeedbackTokenService>();
builder.Services.AddSingleton<IFeedbackStore, MongoFeedbackStore>();
builder.Services.AddSingleton<IActivityEventSink, ApplicationInsightsActivityEventSink>();
builder.Services.AddScoped<IGuestSpotifyTokenClient, GuestSpotifyTokenClient>();
builder.Services.AddScoped<IGuestSessionService, GuestSessionService>();
builder.Services.AddHostedService<GuestSessionCleanupService>();
builder.Services.AddHostedService<GitHubWorkflowDispatchService>();
builder.Services.AddHealthChecks()
    .AddCheck<MongoDbHealthCheck>("mongodb");
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IAuthService, AuthServiceRouter>();
builder.Services.AddScoped<PlaylistService>();
builder.Services.AddScoped<IPlaylistService, PlaylistServiceRouter>();
builder.Services.AddScoped<PlaylistSongService>();
builder.Services.AddScoped<IPlaylistSongService, PlaylistSongServiceRouter>();
builder.Services.AddScoped<SnippetService>();
builder.Services.AddScoped<ISnippetService, SnippetServiceRouter>();
builder.Services.AddScoped<ISpotifyHttpClient, SpotifyHttpClient>();
builder.Services.Configure<SpotifySettings>(builder.Configuration.GetSection("SpotifySettings"));
builder.Services.Configure<AuthRedirectSettings>(builder.Configuration.GetSection("AuthRedirects"));

builder.Services.AddDbContext<DataContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionName = configuration["Database:ConnectionName"] ?? "AzureConnection";
    var connectionString = configuration.GetConnectionString(connectionName)
        ?? throw new InvalidOperationException(
            $"Connection string '{connectionName}' is not configured.");

    options.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, ProviderModelCacheKeyFactory>();
    options.UseSqlServer(connectionString, sqlServerOptions =>
    {
        if (connectionName.Equals("AzureConnection", StringComparison.OrdinalIgnoreCase))
        {
            sqlServerOptions.EnableRetryOnFailure();
        }
    });
});



builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
                builder.Configuration.GetSection("AppSettings:Token").Value!)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal?.IsGuest() != true)
                {
                    return;
                }

                var guestSessions = context.HttpContext.RequestServices
                    .GetRequiredService<IGuestSessionService>();
                if (!await guestSessions.ValidateAsync(
                    principal,
                    context.HttpContext.RequestAborted))
                {
                    context.Fail("The guest session is no longer active.");
                }
            }
        };
    })
    .AddJwtBearer(
        GuestFeedbackTokenService.AuthenticationScheme,
        options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters =
                GuestFeedbackTokenService.CreateValidationParameters(
                    builder.Configuration);
        });


var app = builder.Build();

app.Logger.LogInformation(
    "rptr API starting in the {EnvironmentName} environment.",
    app.Environment.EnvironmentName);

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapRptrOpenApi();

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();
app.MapFeedbackEndpoints();
app.MapActivityEndpoints();
app.MapKeepWarmEndpoints();
app.MapHealthChecks("/health/mongodb");

app.Run();

public partial class Program;
