using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using rptr.Endpoints;
using rptr.Services.Activity;
using rptr.Services.Authorization;
using rptr.Services.PlaylistSongs;

namespace rptr.OpenApi;

internal sealed class RptrSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var property = context.JsonPropertyInfo;

        if (property?.AttributeProvider is MemberInfo memberInfo
            && memberInfo.GetCustomAttribute<AllowedValuesAttribute>()
                is { } allowedValues)
        {
            schema.Enum =
            [
                .. allowedValues.Values.Select(value =>
                    value?.ToString() ?? string.Empty)
            ];
        }

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
    }
}
