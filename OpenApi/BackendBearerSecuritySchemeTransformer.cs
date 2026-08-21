using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using rptr.Services.GuestSessions;

namespace rptr.OpenApi;

internal sealed class BackendBearerSecuritySchemeTransformer(
    IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var authenticationSchemes =
            await authenticationSchemeProvider.GetAllSchemesAsync();
        var securitySchemes = new Dictionary<string, IOpenApiSecurityScheme>();

        if (authenticationSchemes.Any(scheme => scheme.Name == "Bearer"))
        {
            securitySchemes["BackendBearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description =
                    "Backend JWT returned by the session endpoint. " +
                    "Spotify access tokens are not accepted."
            };
        }

        if (authenticationSchemes.Any(scheme =>
                scheme.Name == GuestFeedbackTokenService.AuthenticationScheme))
        {
            securitySchemes[GuestFeedbackTokenService.AuthenticationScheme] =
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description =
                        "Restricted token returned when a guest session starts. " +
                        "It is accepted only by the guest feedback and " +
                        "guest-session-ended endpoints."
                };
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = securitySchemes;
    }
}
