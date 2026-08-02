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
using rptr.Services.Authorization;
using rptr.Services.Spotify;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

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
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var endpointName = context.Description.ActionDescriptor.AttributeRouteInfo?.Name;

        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        if (requiresAuthorization && !allowsAnonymous)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
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

                        if (statusCode != "200"
                            && response.Content is { Count: > 0 } content)
                        {
                            var schema = content.Values
                                .Select(mediaType => mediaType.Schema)
                                .FirstOrDefault(candidate => candidate is not null);

                            if (schema is not null)
                            {
                                content.Clear();
                                content["application/problem+json"] = new OpenApiMediaType
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
        .AddPreferredSecuritySchemes("Bearer")
        .EnablePersistentAuthentication();
	});
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (authenticationSchemes.Any(authScheme => authScheme.Name == "Bearer"))
        {
            var securitySchemes = new Dictionary<string, IOpenApiSecurityScheme>
            {
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer", // "bearer" refers to the header name here
                    In = ParameterLocation.Header,
                    BearerFormat = "Json Web Token"
                }
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = securitySchemes;
        }
    }
}
