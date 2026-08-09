global using AutoMapper;
global using Microsoft.EntityFrameworkCore;
global using  dotnetrpg_again.Data;
global using rptr.Models;

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using rptr.Endpoints;
using rptr.Services.Authorization;
using rptr.Services.Playlists;
using rptr.Services.PlaylistSongs;
using rptr.Services.Snippets;
using rptr.Services.Spotify;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
const string FrontendCorsPolicy = "Frontend";

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

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAutoMapper(cfg => {}, typeof(Program).Assembly);
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPlaylistService, PlaylistService>();
builder.Services.AddScoped<IPlaylistSongService, PlaylistSongService>();
builder.Services.AddScoped<ISnippetService, SnippetService>();
builder.Services.AddScoped<ISpotifyHttpClient, SpotifyHttpClient>();
builder.Services.Configure<SpotifySettings>(builder.Configuration.GetSection("SpotifySettings"));
builder.Services.Configure<AuthRedirectSettings>(builder.Configuration.GetSection("AuthRedirects"));
builder.Services.AddDbContext<DataContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
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
    });

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BackendBearerSecuritySchemeTransformer>();

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

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var endpointName = context.Description.ActionDescriptor.AttributeRouteInfo?.Name;

        if (endpointName is "GetPlaylistSnippets" or "GetPlaylistSongTags")
        {
            operation.Deprecated = true;
        }

        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        if (allowsAnonymous)
        {
            operation.Security = [];
        }
        else if (requiresAuthorization)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("BackendBearer", context.Document)] = []
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

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();
app.MapFeedbackEndpoints();

app.Run();

internal sealed class BackendBearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (authenticationSchemes.Any(authScheme => authScheme.Name == "Bearer"))
        {
            var securitySchemes = new Dictionary<string, IOpenApiSecurityScheme>
            {
                ["BackendBearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description = "Backend JWT returned by the session endpoint. Spotify access tokens are not accepted."
                }
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = securitySchemes;
        }
    }
}
