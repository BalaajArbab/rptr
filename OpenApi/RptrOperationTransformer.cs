using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using rptr.Services.GuestSessions;

namespace rptr.OpenApi;

internal sealed class RptrOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var endpointName =
            context.Description.ActionDescriptor.AttributeRouteInfo?.Name
            ?? metadata.OfType<IEndpointNameMetadata>()
                .FirstOrDefault()?.EndpointName;

        ApplySecurity(operation, context, metadata, endpointName);
        ApplyRedirectResponse(operation, endpointName);
        ApplyResponseMetadata(operation, endpointName);

        return Task.CompletedTask;
    }

    private static void ApplySecurity(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        IList<object> metadata,
        string? endpointName)
    {
        var authorizationMetadata = metadata.OfType<IAuthorizeData>().ToArray();
        var requiresAuthorization = authorizationMetadata.Length > 0;
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        if (allowsAnonymous)
        {
            operation.Security = [];
            return;
        }

        if (!requiresAuthorization)
        {
            return;
        }

        operation.Security ??= new List<OpenApiSecurityRequirement>();

        var usesGuestFeedbackToken = endpointName is
                "CreateGuestFeedback" or "RecordGuestSessionEnded"
            || authorizationMetadata.Any(value =>
                value.AuthenticationSchemes?
                    .Split(',', StringSplitOptions.TrimEntries)
                    .Contains(GuestFeedbackTokenService.AuthenticationScheme)
                == true);
        var securitySchemeName = usesGuestFeedbackToken
            ? GuestFeedbackTokenService.AuthenticationScheme
            : "BackendBearer";

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(
                securitySchemeName,
                context.Document)] = []
        });
    }

    private static void ApplyRedirectResponse(
        OpenApiOperation operation,
        string? endpointName)
    {
        if (endpointName is not ("StartSpotifyLogin" or "HandleSpotifyCallback")
            || operation.Responses is not { } responses)
        {
            return;
        }

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

    private static void ApplyResponseMetadata(
        OpenApiOperation operation,
        string? endpointName)
    {
        if (operation.Responses is not { } responses)
        {
            return;
        }

        AddRetryAfterHeaders(responses, endpointName);
        ApplyResponseDescriptions(responses, endpointName);
    }

    private static void AddRetryAfterHeaders(
        OpenApiResponses responses,
        string? endpointName)
    {
        if (endpointName is not (
            "CreateGuestSession"
            or "CreateGuestFeedback"
            or "RecordActivity"
            or "RecordGuestSessionEnded"
            or "RecordFrontPageView"
            or "RecordGuestSessionCheck"
            or "RecordDemoVideoInteraction"
            or "TouchMongo"))
        {
            return;
        }

        var rateLimitedStatuses = endpointName == "CreateGuestSession"
            ? new[] { "409", "429" }
            : new[] { "429" };

        foreach (var statusCode in rateLimitedStatuses)
        {
            if (!responses.TryGetValue(statusCode, out var response))
            {
                continue;
            }

            var headers = response.Headers is null
                ? new Dictionary<string, IOpenApiHeader>()
                : new Dictionary<string, IOpenApiHeader>(response.Headers);
            headers["Retry-After"] = new OpenApiHeader
            {
                Description = endpointName == "CreateGuestSession"
                    ? "Seconds before another guest-session attempt should be made."
                    : endpointName == "CreateGuestFeedback"
                        ? "Seconds before another guest-feedback submission should be made."
                        : endpointName == "TouchMongo"
                            ? "Seconds before another keep-warm request should be made."
                            : "Seconds before another activity event should be submitted.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32"
                }
            };

            responses[statusCode] = new OpenApiResponse
            {
                Description = response.Description,
                Content = response.Content,
                Headers = headers
            };
        }
    }

    private static void ApplyResponseDescriptions(
        OpenApiResponses responses,
        string? endpointName)
    {
        var descriptions = endpointName switch
        {
            "CreateAuthSession" => new Dictionary<string, string>
            {
                ["200"] =
                    "The handoff code was consumed and the authenticated " +
                    "session details are returned.",
                ["400"] = "The request body is missing or malformed.",
                ["401"] =
                    "The handoff code is missing, invalid, expired, already " +
                    "consumed, or no longer maps to a valid user and Spotify token.",
                ["500"] =
                    "An unexpected backend failure occurred, including an " +
                    "unrecoverable Spotify token refresh failure."
            },
            "GetSpotifyAccessToken" => new Dictionary<string, string>
            {
                ["200"] = "A current Spotify access token is returned.",
                ["401"] =
                    "The backend JWT is missing, invalid, or expired, or the " +
                    "authenticated user has no stored Spotify token.",
                ["500"] =
                    "An unexpected backend failure occurred, including an " +
                    "unrecoverable Spotify token refresh failure."
            },
            _ => null
        };

        if (descriptions is null)
        {
            return;
        }

        foreach (var (statusCode, description) in descriptions)
        {
            if (!responses.TryGetValue(statusCode, out var response))
            {
                continue;
            }

            response.Description = description;

            if (response.Content is not { Count: > 0 } content)
            {
                continue;
            }

            var schema = content.Values
                .Select(mediaType => mediaType.Schema)
                .FirstOrDefault(candidate => candidate is not null);

            if (schema is null)
            {
                continue;
            }

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
