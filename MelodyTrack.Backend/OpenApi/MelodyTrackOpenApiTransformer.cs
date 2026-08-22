using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MelodyTrack.Backend.OpenApi;

public sealed class MelodyTrackOpenApiTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
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
        if (!allowsAnonymous)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, context.Document, null)] = []
            });
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

        var route = "/" + (context.Description.RelativePath ?? string.Empty).TrimStart('/');
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
