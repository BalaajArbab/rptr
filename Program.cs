global using Microsoft.EntityFrameworkCore;
global using  dotnetrpg_again.Data;
global using rptr.Models;

using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using rptr.Data.Mongo;
using rptr.Endpoints;
using rptr.Services.Activity;
using rptr.Services.Authorization;
using rptr.Services.GuestSessions;
using rptr.Services.Feedback;
using rptr.Services.Playlists;
using rptr.Services.PlaylistSongs;
using rptr.Services.Snippets;
using rptr.Services.Spotify;
using Scalar.AspNetCore;

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

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection(MongoDbSettings.SectionName));
builder.Services.AddOptions<GuestSessionSettings>()
    .Bind(builder.Configuration.GetSection(GuestSessionSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MongoDbContext>();
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

var connectionName = builder.Configuration["Database:ConnectionName"] ?? "AzureConnection";
var connectionString = builder.Configuration.GetConnectionString(connectionName)
    ?? throw new InvalidOperationException(
        $"Connection string '{connectionName}' is not configured.");

builder.Services.AddDbContext<DataContext>(options =>
{
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

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BackendBearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer(async (document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??=
            new Dictionary<string, IOpenApiSchema>();

        var eventSchemaReferences = new List<IOpenApiSchema>();
        var discriminatorMapping =
            new Dictionary<string, OpenApiSchemaReference>(StringComparer.Ordinal);

        foreach (var eventType in ActivityEventMetadata.EventTypes)
        {
            var schema = await context.GetOrCreateSchemaAsync(
                eventType.RequestType,
                parameterDescription: null,
                cancellationToken);
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["type"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [eventType.Discriminator]
            };
            schema.Required ??= new HashSet<string>();
            schema.Required.Add("type");

            var componentName = eventType.RequestType.Name;
            document.Components.Schemas[componentName] = schema;

            var schemaReference = new OpenApiSchemaReference(
                componentName,
                document);
            eventSchemaReferences.Add(schemaReference);
            discriminatorMapping[eventType.Discriminator] = schemaReference;
        }

        document.Components.Schemas[nameof(ActivityEventRequest)] =
            new OpenApiSchema
            {
                OneOf = eventSchemaReferences,
                Discriminator = new OpenApiDiscriminator
                {
                    PropertyName = "type",
                    Mapping = discriminatorMapping
                }
            };
    });

    options.AddSchemaTransformer((schema, context, cancellationToken) =>
    {
        var property = context.JsonPropertyInfo;

        if (property?.DeclaringType == typeof(AuthSessionRequest)
            && property.Name == "code")
        {
            schema.MinLength = 22;
            schema.MaxLength = 22;
            schema.Pattern = "^[A-Za-z0-9_-]{22}$";
        }
        else if (property?.DeclaringType == typeof(AuthSessionResponse)
            && property.Name == "tokenType")
        {
            schema.Enum = ["Bearer"];
        }
        else if (property?.DeclaringType is not null
            && (property.DeclaringType == typeof(PutSongTagsRequest)
                || property.DeclaringType == typeof(SongTagsResponse)
                || property.DeclaringType == typeof(PlaylistSongResponse))
            && property.Name == "tags")
        {
            schema.MaxItems = 2;
            schema.Items = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MaxLength = 10
            };
        }
        else if (property?.DeclaringType == typeof(CreateFeedbackRequest)
            && property.Name == "message")
        {
            schema.MaxLength = 2000;
        }
        else if (property?.DeclaringType == typeof(EncodedActivityRequest)
            && property.Name == "payload")
        {
            schema.Format = "byte";
            schema.MinLength = 4;
            schema.MaxLength = EncodedActivityRequest.MaximumPayloadLength;
        }

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var endpointName = context.Description.ActionDescriptor.AttributeRouteInfo?.Name
            ?? metadata.OfType<IEndpointNameMetadata>().FirstOrDefault()?.EndpointName;

        var authorizationMetadata = metadata.OfType<IAuthorizeData>().ToArray();
        var requiresAuthorization = authorizationMetadata.Length > 0;
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        if (allowsAnonymous)
        {
            operation.Security = [];
        }
        else if (requiresAuthorization)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();

            var usesGuestFeedbackToken = endpointName == "CreateGuestFeedback"
                || authorizationMetadata.Any(value =>
                    value.AuthenticationSchemes?.Split(',', StringSplitOptions.TrimEntries)
                        .Contains(GuestFeedbackTokenService.AuthenticationScheme) == true);
            var securitySchemeName = usesGuestFeedbackToken
                ? GuestFeedbackTokenService.AuthenticationScheme
                : "BackendBearer";

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(securitySchemeName, context.Document)] = []
            });
        }

        if (endpointName is "StartSpotifyLogin" or "HandleSpotifyCallback"
            && operation.Responses is { } responses)
        {
            var isLogin = endpointName == "StartSpotifyLogin";

            responses["302"] = new OpenApiResponse
            {
                Description = isLogin
                    ? "Redirects the browser to Spotify authorization."
                    : "Redirects the browser to the frontend authentication result page.",
                Headers = new Dictionary<string, IOpenApiHeader>
                {
                    ["Location"] = new OpenApiHeader
                    {
                        Description = isLogin
                            ? "Spotify authorization URL."
                            : "Frontend authentication result URL.",
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "uri"
                        }
                    },
                    ["Set-Cookie"] = new OpenApiHeader
                    {
                        Description = isLogin
                            ? "Sets the short-lived HttpOnly state-validation cookie."
                            : "Expires the state-validation cookie.",
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String
                        }
                    }
                }
            };
        }

        if (operation.Responses is { } documentedResponses)
        {
            if (endpointName is "CreateGuestSession" or "CreateGuestFeedback" or "RecordActivity")
            {
                var rateLimitedStatuses = endpointName == "CreateGuestSession"
                    ? new[] { "409", "429" }
                    : new[] { "429" };
                foreach (var statusCode in rateLimitedStatuses)
                {
                    if (documentedResponses.TryGetValue(statusCode, out var response))
                    {
                        var headers = response.Headers is null
                            ? new Dictionary<string, IOpenApiHeader>()
                            : new Dictionary<string, IOpenApiHeader>(response.Headers);
                        headers["Retry-After"] = new OpenApiHeader
                        {
                            Description = endpointName == "CreateGuestSession"
                                ? "Seconds before another guest-session attempt should be made."
                                : endpointName == "CreateGuestFeedback"
                                    ? "Seconds before another guest-feedback submission should be made."
                                    : "Seconds before another activity event should be submitted.",
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Integer,
                                Format = "int32"
                            }
                        };

                        documentedResponses[statusCode] = new OpenApiResponse
                        {
                            Description = response.Description,
                            Content = response.Content,
                            Headers = headers
                        };
                    }
                }
            }

            var responseDescriptions = endpointName switch
            {
                "CreateAuthSession" => new Dictionary<string, string>
                {
                    ["200"] = "The handoff code was consumed and the authenticated session details are returned.",
                    ["400"] = "The request body is missing or malformed.",
                    ["401"] = "The handoff code is missing, invalid, expired, already consumed, or no longer maps to a valid user and Spotify token.",
                    ["500"] = "An unexpected backend failure occurred, including an unrecoverable Spotify token refresh failure."
                },
                "GetSpotifyAccessToken" => new Dictionary<string, string>
                {
                    ["200"] = "A current Spotify access token is returned.",
                    ["401"] = "The backend JWT is missing, invalid, or expired, or the authenticated user has no stored Spotify token.",
                    ["500"] = "An unexpected backend failure occurred, including an unrecoverable Spotify token refresh failure."
                },
                _ => null
            };

            if (responseDescriptions is not null)
            {
                foreach (var (statusCode, description) in responseDescriptions)
                {
                    if (documentedResponses.TryGetValue(statusCode, out var response))
                    {
                        response.Description = description;

                        if (response.Content is { Count: > 0 } content)
                        {
                            var schema = content.Values
                                .Select(mediaType => mediaType.Schema)
                                .FirstOrDefault(candidate => candidate is not null);

                            if (schema is not null)
                            {
                                var contentType = statusCode == "200"
                                    ? "application/json"
                                    : "application/problem+json";

                                content.Clear();
                                content[contentType] = new OpenApiMediaType
                                {
                                    Schema = schema
                                };
                            }
                        }
                    }
                }
            }
        }

        return Task.CompletedTask;
    });
});


var app = builder.Build();

app.Logger.LogInformation(
    "rptr API starting in the {EnvironmentName} environment.",
    app.Environment.EnvironmentName);

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => 
	{
		options.WithTitle("rptr api")
		.ExpandAllTags()
		.WithTheme(ScalarTheme.BluePlanet)
        .AddPreferredSecuritySchemes("BackendBearer")
        .EnablePersistentAuthentication();
	});
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();
app.MapFeedbackEndpoints();
app.MapActivityEndpoints();
app.MapHealthChecks("/health/mongodb");

app.Run();

internal sealed class BackendBearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        var securitySchemes = new Dictionary<string, IOpenApiSecurityScheme>();
        if (authenticationSchemes.Any(authScheme => authScheme.Name == "Bearer"))
        {
            securitySchemes["BackendBearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "Backend JWT returned by the session endpoint. Spotify access tokens are not accepted."
            };
        }

        if (authenticationSchemes.Any(authScheme =>
                authScheme.Name == GuestFeedbackTokenService.AuthenticationScheme))
        {
            securitySchemes[GuestFeedbackTokenService.AuthenticationScheme] =
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description = "Feedback-only token returned when a guest session starts. It is accepted only by the guest feedback endpoint."
                };
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = securitySchemes;
    }
}
