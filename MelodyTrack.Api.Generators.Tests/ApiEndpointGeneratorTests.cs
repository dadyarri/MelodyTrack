using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MelodyTrack.Api.Generators.Tests;

public sealed class ApiEndpointGeneratorTests
{
    private const string EndpointContract = """
        using System;

        namespace MelodyTrack.Backend.Api
        {
            public enum ApiMethod
            {
                Get,
                Post,
                Put,
                Delete,
                Patch
            }

            [AttributeUsage(AttributeTargets.Class, Inherited = false)]
            public sealed class ApiEndpointAttribute(ApiMethod method, string route) : Attribute
            {
            }
        }
        """;

    [Fact]
    public void GeneratesMappingsForEverySupportedMethod()
    {
        var result = RunGenerator("""
            using System.Threading;
            using MelodyTrack.Backend.Api;

            namespace TestEndpoints;

            [ApiEndpoint(ApiMethod.Get, "/things")]
            public sealed class GetThingsEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Post, "/things")]
            public sealed class CreateThingEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Put, "/things/{id}")]
            public sealed class ReplaceThingEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Delete, "/things/{id}")]
            public sealed class DeleteThingEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Patch, "/things/{id}")]
            public sealed class UpdateThingEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }
            """);

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("endpoints.MapGet(\"/things\"", generated, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapPost(\"/things\"", generated, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapPut(\"/things/{id}\"", generated, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapDelete(\"/things/{id}\"", generated, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapPatch(\"/things/{id}\"", generated, StringComparison.Ordinal);
        Assert.Contains(".WithName(\"GetThingsEndpoint\")", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsRequiredEndpointShapeDiagnostics()
    {
        var result = RunGenerator("""
            using System.Threading;
            using MelodyTrack.Backend.Api;

            namespace InvalidEndpoints;

            [ApiEndpoint(ApiMethod.Get, "/bad-name")]
            public sealed class BadName
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Get, "/missing")]
            public sealed class MissingEndpoint
            {
            }

            [ApiEndpoint(ApiMethod.Get, "/multiple")]
            public sealed class MultipleEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
                public static void HandleAsync(string value, CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Get, "/non-public")]
            public sealed class NonPublicEndpoint
            {
                private static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Get, "/non-static")]
            public sealed class NonStaticEndpoint
            {
                public void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Get, "/no-cancellation")]
            public sealed class NoCancellationEndpoint
            {
                public static void HandleAsync() { }
            }

            [ApiEndpoint(ApiMethod.Get, "relative")]
            public sealed class InvalidRouteEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint((ApiMethod)99, "/unsupported")]
            public sealed class UnsupportedMethodEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            """);

        var diagnosticIds = result.Diagnostics.Select(diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("MTAPI001", diagnosticIds);
        Assert.Contains("MTAPI002", diagnosticIds);
        Assert.Contains("MTAPI003", diagnosticIds);
        Assert.Contains("MTAPI004", diagnosticIds);
        Assert.Contains("MTAPI005", diagnosticIds);
        Assert.Contains("MTAPI008", diagnosticIds);
        Assert.Contains("MTAPI009", diagnosticIds);
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Id == "MTAPI004"));
    }

    [Fact]
    public void ReportsDuplicateOperationIdsAndRoutes()
    {
        var result = RunGenerator("""
            using System.Threading;
            using MelodyTrack.Backend.Api;

            namespace FirstOperation
            {
                [ApiEndpoint(ApiMethod.Get, "/first")]
                public sealed class DuplicateOperationEndpoint
                {
                    public static void HandleAsync(CancellationToken cancellationToken) { }
                }
            }

            namespace SecondOperation
            {
                [ApiEndpoint(ApiMethod.Get, "/second")]
                public sealed class DuplicateOperationEndpoint
                {
                    public static void HandleAsync(CancellationToken cancellationToken) { }
                }
            }

            [ApiEndpoint(ApiMethod.Post, "/duplicate-route")]
            public sealed class FirstRouteEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }

            [ApiEndpoint(ApiMethod.Post, "/DUPLICATE-ROUTE")]
            public sealed class SecondRouteEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }
            """);

        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Id == "MTAPI006"));
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Id == "MTAPI007"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("/contains whitespace")]
    [InlineData("/double//separator")]
    [InlineData("/unbalanced/{value")]
    [InlineData("/unbalanced/value}")]
    [InlineData("/empty/{}")]
    [InlineData("/nested/{{value}}")]
    public void ReportsInvalidRoutes(string route)
    {
        var result = RunGenerator($$"""
            using System.Threading;
            using MelodyTrack.Backend.Api;

            namespace InvalidRouteEndpoints;

            [ApiEndpoint(ApiMethod.Get, "{{route}}")]
            public sealed class InvalidRouteEndpoint
            {
                public static void HandleAsync(CancellationToken cancellationToken) { }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MTAPI008");
    }

    private static GeneratorDriverRunResult RunGenerator(string endpointSource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var contractSyntaxTree = CSharpSyntaxTree.ParseText(EndpointContract, parseOptions);
        var endpointSyntaxTree = CSharpSyntaxTree.ParseText(endpointSource, parseOptions);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [contractSyntaxTree, endpointSyntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ApiEndpointGenerator().AsSourceGenerator());

        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult();
    }
}
