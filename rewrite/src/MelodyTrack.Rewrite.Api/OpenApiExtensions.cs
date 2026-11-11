using System.Text.Json;
using Microsoft.OpenApi;

namespace MelodyTrack.Rewrite.Api;

public static class OpenApiExtensions
{
    public static IServiceCollection AddRewriteOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddSchemaTransformer((schema, context, _) =>
            {
                // WebAuthn assertions remain JSON objects; no invented property contract is emitted.
                if (context.JsonTypeInfo.Type == typeof(JsonElement)
                    || context.JsonTypeInfo.Type == typeof(JsonElement?))
                {
                    schema.Type = JsonSchemaType.Object | ((schema.Type ?? 0) & JsonSchemaType.Null);
                    schema.AdditionalPropertiesAllowed = true;
                }
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "MelodyTrack rewrite API";
                document.Info.Version = "v1";
                // Hosts and credentials come from the caller, never from a developer's configuration.
                document.Servers = [];
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
