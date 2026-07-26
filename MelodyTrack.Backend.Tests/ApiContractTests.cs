using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Tests.Infrastructure;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ApiContractTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task FrameworkErrors_ReturnProblemDetailsWithMatchingTraceHeader()
    {
        var response = await App.Client.GetAsync("/missing-endpoint", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ApiMediaTypes.ProblemJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)HttpStatusCode.NotFound);
        problem.Type.ShouldBe(ApiProblemTypes.NotFound);
        problem.Code.ShouldBe(ApiProblemCodes.NotFound);
        problem.Instance.ShouldBe("/missing-endpoint");
        problem.TraceId.ShouldNotBeNullOrWhiteSpace();
        response.Headers.GetValues("X-Trace-Id").Single().ShouldBe(problem.TraceId);
    }

    [Fact]
    public async Task UnauthorizedErrors_ReturnBearerChallengeAndProblemDetails()
    {
        var response = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(challenge => challenge.Scheme == "Bearer");
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ApiMediaTypes.ProblemJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        problem.Code.ShouldBe(ApiProblemCodes.Unauthorized);
    }

    [Fact]
    public async Task MalformedJson_ReturnsValidationProblemDetails()
    {
        using var content = new StringContent("{not-json", Encoding.UTF8, "application/json");

        var response = await App.Client.PostAsync("/auth/login", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ApiMediaTypes.ProblemJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)HttpStatusCode.BadRequest);
        problem.Code.ShouldBeOneOf(ApiProblemCodes.MalformedRequest, ApiProblemCodes.Validation);
        problem.TraceId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RateLimitErrors_ReturnProblemDetailsAndRetryTiming()
    {
        var throttleIdentity = $"contract-{Ulid.NewUlid()}";
        using var rateLimitedClient = App.CreateClient();
        rateLimitedClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", throttleIdentity);
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(new { email = $"missing-{Ulid.NewUlid()}@example.com", password = "Incorrect1!" })
            };
            response = await rateLimitedClient.SendAsync(request, TestContext.Current.CancellationToken);
        }

        response.ShouldNotBeNull();
        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.ShouldNotBeNull();
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ApiMediaTypes.ProblemJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        problem.Type.ShouldBe(ApiProblemTypes.RateLimited);
        problem.Code.ShouldBe(ApiProblemCodes.RateLimited);
    }

    [Fact]
    public async Task OpenApiOperations_HaveUniqueIdsAndProblemDetailsErrors()
    {
        var response = await App.Client.GetAsync("/swagger/v2/swagger.json", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var operationIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is "parameters" or "summary" or "description")
                {
                    continue;
                }

                var operationId = operation.Value.GetProperty("operationId").GetString();
                operationId.ShouldNotBeNullOrWhiteSpace($"{operation.Name.ToUpperInvariant()} {path.Name} must have an operationId");
                operationIds.Add(operationId).ShouldBeTrue($"Duplicate operationId: {operationId}");

                var responses = operation.Value.GetProperty("responses");
                responses.TryGetProperty("500", out var serverError).ShouldBeTrue($"{operationId} must document unexpected failures");
                AssertProblemResponse(serverError, operationId!, "500");

                if (responses.TryGetProperty("201", out var createdResponse))
                {
                    AssertHeader(createdResponse, operationId!, "201", "Location");
                }

                foreach (var documentedResponse in responses.EnumerateObject())
                {
                    if (!int.TryParse(documentedResponse.Name, out var statusCode)
                        || statusCode is < 200 or >= 300
                        || !documentedResponse.Value.TryGetProperty("content", out var successContent)
                        || !successContent.EnumerateObject().Any(mediaType => mediaType.Name is not "application/json"))
                    {
                        continue;
                    }

                    AssertHeader(documentedResponse.Value, operationId!, documentedResponse.Name, "Content-Disposition");
                    AssertHeader(documentedResponse.Value, operationId!, documentedResponse.Name, "Cache-Control");
                }

                foreach (var documentedResponse in responses.EnumerateObject())
                {
                    if (!int.TryParse(documentedResponse.Name, out var statusCode) || statusCode < 400)
                    {
                        continue;
                    }

                    AssertProblemResponse(documentedResponse.Value, operationId!, documentedResponse.Name);
                }
            }
        }
    }

    [Fact]
    public async Task OpenApiPaginationSchemas_UseSharedDataAndInfoShape()
    {
        var response = await App.Client.GetAsync("/swagger/v2/swagger.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var paginatedSchemas = schemas.EnumerateObject()
            .Where(schema => schema.Value.ToString().Contains(nameof(PagedInfo), StringComparison.Ordinal))
            .ToArray();

        paginatedSchemas.Length.ShouldBeGreaterThanOrEqualTo(5);
        foreach (var schema in paginatedSchemas)
        {
            var serialized = schema.Value.ToString();
            serialized.Contains("\"data\"", StringComparison.Ordinal)
                .ShouldBeTrue($"{schema.Name} must expose the shared data collection");
            serialized.Contains("\"info\"", StringComparison.Ordinal)
                .ShouldBeTrue($"{schema.Name} must expose shared page metadata");
        }
    }

    [Fact]
    public async Task OpenApiSurface_MatchesApprovedSnapshot()
    {
        var response = await App.Client.GetAsync("/swagger/v2/swagger.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var actual = CreateOpenApiSurfaceSnapshot(document.RootElement);
        var sourceSnapshotPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../OpenApiContract.snapshot"));

        if (Environment.GetEnvironmentVariable("MELODYTRACK_UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            await File.WriteAllTextAsync(sourceSnapshotPath, actual, TestContext.Current.CancellationToken);
        }

        File.Exists(sourceSnapshotPath).ShouldBeTrue("The approved OpenAPI surface snapshot is missing.");
        var expected = await File.ReadAllTextAsync(sourceSnapshotPath, TestContext.Current.CancellationToken);
        actual.ShouldBe(expected, "The HTTP surface changed. Review the OpenAPI diff and deliberately refresh the snapshot when the change is compatible.");
    }

    private static void AssertHeader(JsonElement response, string operationId, string statusCode, string header)
    {
        response.TryGetProperty("headers", out var headers).ShouldBeTrue($"{operationId} response {statusCode} must describe headers");
        headers.TryGetProperty(header, out _).ShouldBeTrue($"{operationId} response {statusCode} must describe {header}");
    }

    private static void AssertProblemResponse(JsonElement response, string operationId, string statusCode)
    {
        response.TryGetProperty("content", out var content).ShouldBeTrue($"{operationId} response {statusCode} must describe content");
        content.TryGetProperty(ApiMediaTypes.ProblemJson, out var mediaType)
            .ShouldBeTrue($"{operationId} response {statusCode} must use {ApiMediaTypes.ProblemJson}");
        mediaType.TryGetProperty("schema", out var schema).ShouldBeTrue($"{operationId} response {statusCode} must describe a schema");
        schema.ToString().ShouldContain(nameof(ApiProblemDetails));
    }

    private static string CreateOpenApiSurfaceSnapshot(JsonElement document)
    {
        var lines = new List<string>();
        foreach (var path in document.GetProperty("paths").EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            foreach (var operation in path.Value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (operation.Name is "parameters" or "summary" or "description")
                {
                    continue;
                }

                var value = operation.Value;
                var parameters = value.TryGetProperty("parameters", out var parameterArray)
                    ? string.Join(',', parameterArray.EnumerateArray()
                        .Select(parameter => $"{parameter.GetProperty("in").GetString()}:{parameter.GetProperty("name").GetString()}:{(parameter.TryGetProperty("required", out var required) && required.GetBoolean() ? "required" : "optional")}")
                        .OrderBy(item => item, StringComparer.Ordinal))
                    : string.Empty;
                var responses = string.Join(',', value.GetProperty("responses").EnumerateObject()
                    .OrderBy(item => item.Name, StringComparer.Ordinal)
                    .Select(item =>
                    {
                        var mediaTypes = item.Value.TryGetProperty("content", out var content)
                            ? string.Join('+', content.EnumerateObject().Select(media => media.Name).OrderBy(name => name, StringComparer.Ordinal))
                            : "-";
                        var headers = item.Value.TryGetProperty("headers", out var responseHeaders)
                            ? string.Join('+', responseHeaders.EnumerateObject().Select(header => header.Name).OrderBy(name => name, StringComparer.Ordinal))
                            : "-";
                        return $"{item.Name}[{mediaTypes}][{headers}]";
                    }));
                lines.Add($"{operation.Name.ToUpperInvariant()} {path.Name} | {value.GetProperty("operationId").GetString()} | {parameters} | {responses}");
            }
        }

        return string.Join('\n', lines) + '\n';
    }
}
