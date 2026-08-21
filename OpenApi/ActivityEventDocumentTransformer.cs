using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using rptr.Services.Activity;

namespace rptr.OpenApi;

internal sealed class ActivityEventDocumentTransformer
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
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
    }
}
