using Scalar.AspNetCore;

namespace rptr.OpenApi;

public static class RptrOpenApiExtensions
{
    public static IServiceCollection AddRptrOpenApi(
        this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BackendBearerSecuritySchemeTransformer>();
            options.AddDocumentTransformer<ActivityEventDocumentTransformer>();
            options.AddSchemaTransformer<RptrSchemaTransformer>();
            options.AddOperationTransformer<RptrOperationTransformer>();
        });

        return services;
    }

    public static WebApplication MapRptrOpenApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.WithTitle("rptr api")
                .ExpandAllTags()
                .WithTheme(ScalarTheme.BluePlanet)
                .AddPreferredSecuritySchemes("BackendBearer")
                .EnablePersistentAuthentication();
        });

        return app;
    }
}
