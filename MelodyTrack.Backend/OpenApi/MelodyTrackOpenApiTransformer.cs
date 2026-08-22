using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace MelodyTrack.Backend.OpenApi;

public sealed class MelodyTrackOpenApiTransformer(IOptions<PublicUrlOptions> publicUrlOptions)
    : IOpenApiDocumentTransformer, IOpenApiOperationTransformer, IOpenApiSchemaTransformer
{
    private const string BearerScheme = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "MelodyTrack API",
            Version = "v1"
        };
        document.Servers ??= [];
        if (document.Servers.Count == 0)
        {
            document.Servers.Add(new OpenApiServer
            {
                Url = publicUrlOptions.Value.BaseUrl.TrimEnd('/'),
                Description = "MelodyTrack public origin"
            });
        }
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "MelodyTrack access token"
        };
        return Task.CompletedTask;
    }

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var route = "/" + (context.Description.RelativePath ?? string.Empty).TrimStart('/');
        if (!allowsAnonymous)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, context.Document, null)] = []
            });
        }
        if (!allowsAnonymous || route == "/api/auth/refresh")
        {
            EnsureProblemResponse(operation, StatusCodes.Status401Unauthorized, await GetProblemSchemaAsync(context, cancellationToken));
            EnsureProblemResponse(operation, StatusCodes.Status403Forbidden, await GetProblemSchemaAsync(context, cancellationToken));
        }

        var problemSchema = await GetProblemSchemaAsync(context, cancellationToken);
        EnsureProblemResponse(operation, StatusCodes.Status405MethodNotAllowed, problemSchema);
        EnsureProblemResponse(operation, StatusCodes.Status500InternalServerError, problemSchema);
        if (operation.RequestBody is not null)
        {
            EnsureProblemResponse(operation, StatusCodes.Status400BadRequest, problemSchema);
            EnsureProblemResponse(operation, StatusCodes.Status415UnsupportedMediaType, problemSchema);
        }

        foreach (var (status, response) in operation.Responses ?? [])
        {
            AddTraceHeader(response);
            if (status == StatusCodes.Status201Created.ToString())
            {
                AddHeader(response, "Location", "URI of the created resource");
            }
            if (int.TryParse(status, out var statusCode) && statusCode >= StatusCodes.Status400BadRequest)
            {
                ConfigureProblemResponse(response, statusCode, problemSchema);
            }
        }

        if (string.Equals(context.Description.HttpMethod, HttpMethods.Post, StringComparison.OrdinalIgnoreCase)
            && SupportsIdempotency(route))
        {
            operation.Parameters ??= [];
            if (!operation.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)))
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "Idempotency-Key",
                    In = ParameterLocation.Header,
                    Required = false,
                    Description = "Optional caller-selected replay key for safe creation retries."
                });
            }
        }

        if (GetDownloadMediaType(route) is { } mediaType)
        {
            operation.Responses ??= new OpenApiResponses();
            if (!operation.Responses.TryGetValue(StatusCodes.Status200OK.ToString(), out var response))
            {
                response = new OpenApiResponse { Description = "Download" };
                operation.Responses[StatusCodes.Status200OK.ToString()] = response;
            }
            var concreteResponse = response as OpenApiResponse
                ?? new OpenApiResponse { Description = response.Description };
            operation.Responses[StatusCodes.Status200OK.ToString()] = concreteResponse;
            concreteResponse.Content = new Dictionary<string, OpenApiMediaType>
            {
                [mediaType] = new()
                {
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                }
            };
            concreteResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concreteResponse.Headers["Content-Disposition"] = new OpenApiHeader
            {
                Description = "Attachment filename"
            };
            concreteResponse.Headers["Cache-Control"] = new OpenApiHeader
            {
                Description = "Download caching policy"
            };
        }
    }

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var declaredType = context.JsonTypeInfo.Type;
        var nullableType = Nullable.GetUnderlyingType(declaredType);
        var type = nullableType ?? declaredType;
        var nullableFlag = nullableType is null ? (JsonSchemaType)0 : JsonSchemaType.Null;

        if (type == typeof(Ulid))
        {
            // Keep the shared primitive schema non-nullable. OpenAPI nullability belongs
            // to the containing property; Kiota otherwise generates Ulid as Parsable.
            schema.Type = JsonSchemaType.String;
            schema.Format = null;
            schema.Pattern = "^[0-9A-HJKMNP-TV-Z]{26}$";
            schema.Properties?.Clear();
        }
        else if (type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong))
        {
            schema.Type = JsonSchemaType.Integer | nullableFlag;
            schema.Pattern = null;
        }
        else if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            schema.Type = JsonSchemaType.Number | nullableFlag;
            schema.Pattern = null;
        }
        else if (context.JsonPropertyInfo?.AttributeProvider?.IsDefined(typeof(UrlAttribute), true) == true)
        {
            // Kiota represents URLs as strings and warns about OpenAPI's uri format.
            // URL validation metadata remains on the .NET property.
            schema.Format = null;
        }

        return Task.CompletedTask;
    }

    private static async Task<IOpenApiSchema> GetProblemSchemaAsync(
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        await context.GetOrCreateSchemaAsync(typeof(ApiProblemDetails), null, cancellationToken);
        return new OpenApiSchemaReference(nameof(ApiProblemDetails), context.Document, null);
    }

    private static void EnsureProblemResponse(OpenApiOperation operation, int statusCode, IOpenApiSchema schema)
    {
        operation.Responses ??= new OpenApiResponses();
        var key = statusCode.ToString();
        if (!operation.Responses.TryGetValue(key, out var response))
        {
            response = new OpenApiResponse();
            operation.Responses[key] = response;
        }
        ConfigureProblemResponse(response, statusCode, schema);
    }

    private static void ConfigureProblemResponse(IOpenApiResponse response, int statusCode, IOpenApiSchema schema)
    {
        if (response is not OpenApiResponse concreteResponse)
        {
            return;
        }
        concreteResponse.Description = ApiErrorResponseFactory.GetTitle(statusCode);
        concreteResponse.Content = new Dictionary<string, OpenApiMediaType>
        {
            [ApiMediaTypes.ProblemJson] = new() { Schema = schema }
        };
        AddTraceHeader(concreteResponse);
        if (statusCode == StatusCodes.Status401Unauthorized)
        {
            concreteResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concreteResponse.Headers["WWW-Authenticate"] = new OpenApiHeader
            {
                Description = "Bearer authentication challenge"
            };
        }
    }

    private static void AddTraceHeader(IOpenApiResponse response)
    {
        AddHeader(response, "X-Trace-Id", "W3C request trace identifier");
    }

    private static void AddHeader(IOpenApiResponse response, string name, string description)
    {
        if (response is not OpenApiResponse concreteResponse)
        {
            return;
        }
        concreteResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
        concreteResponse.Headers[name] = new OpenApiHeader
        {
            Description = description
        };
    }

    private static bool SupportsIdempotency(string path) => path is
        "/api/appointments" or "/api/clients" or "/api/payments" or "/api/expenses"
        or "/api/course-enrollments" or "/api/courses" or "/api/services"
        or "/api/expense-categories" or "/api/client-sources";

    private static string? GetDownloadMediaType(string path) => path switch
    {
        "/api/exports/client-debts" or "/api/exports/expenses" or "/api/exports/payments" =>
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "/api/exports/teacher-schedule" => "image/png",
        "/api/calendar-subscriptions/{token}.ics" => "text/calendar",
        _ => null
    };
}
