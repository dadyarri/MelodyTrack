using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MelodyTrack.Api.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ApiEndpointGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "MelodyTrack.Backend.Api.ApiEndpointAttribute";

    private static readonly DiagnosticDescriptor InvalidClassName = new(
        "MTAPI001",
        "Endpoint class name is invalid",
        "Endpoint class '{0}' must end with 'Endpoint'",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor MissingHandler = new(
        "MTAPI002",
        "Endpoint handler is missing",
        "Endpoint class '{0}' must declare one HandleAsync method",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor MultipleHandlers = new(
        "MTAPI003",
        "Endpoint has multiple handlers",
        "Endpoint class '{0}' must declare exactly one HandleAsync method",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidHandlerAccessibility = new(
        "MTAPI004",
        "Endpoint handler shape is invalid",
        "Endpoint handler '{0}.HandleAsync' must be public and static",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor MissingCancellationToken = new(
        "MTAPI005",
        "Endpoint handler does not accept cancellation",
        "Endpoint handler '{0}.HandleAsync' must accept a CancellationToken",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateOperationId = new(
        "MTAPI006",
        "Endpoint operation ID is duplicated",
        "Operation ID '{0}' is used by more than one endpoint",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateRoute = new(
        "MTAPI007",
        "Endpoint route is duplicated",
        "Route '{0} {1}' is used by more than one endpoint",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidRoute = new(
        "MTAPI008",
        "Endpoint route is invalid",
        "Endpoint route '{0}' must be a non-empty application-relative route beginning with '/'",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnsupportedMethod = new(
        "MTAPI009",
        "Endpoint HTTP method is unsupported",
        "ApiMethod value '{0}' is not supported",
        "MelodyTrack.Api",
        DiagnosticSeverity.Error,
        true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => CreateEndpoint(attributeContext))
            .Where(static endpoint => endpoint is not null)
            .Select(static (endpoint, _) => endpoint!);

        context.RegisterSourceOutput(endpoints.Collect(), static (productionContext, collectedEndpoints) =>
            Generate(productionContext, collectedEndpoints));
    }

    private static EndpointModel? CreateEndpoint(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol classSymbol || context.Attributes.Length == 0)
        {
            return null;
        }

        var attribute = context.Attributes[0];
        var methodValue = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int value
            ? value
            : int.MinValue;
        var route = attribute.ConstructorArguments.Length > 1
            ? attribute.ConstructorArguments[1].Value as string
            : null;
        var handlers = classSymbol.GetMembers("HandleAsync")
            .OfType<IMethodSymbol>()
            .Where(static method => !method.IsImplicitlyDeclared)
            .ToImmutableArray();

        return new EndpointModel(
            classSymbol.Name,
            classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            methodValue,
            route,
            classSymbol.Name.EndsWith("Endpoint", StringComparison.Ordinal)
                ? classSymbol.Name.Substring(0, classSymbol.Name.Length - "Endpoint".Length)
                : classSymbol.Name,
            handlers,
            classSymbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None);
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<EndpointModel> endpoints)
    {
        var invalidEndpoints = new HashSet<EndpointModel>();

        foreach (var endpoint in endpoints)
        {
            ValidateEndpoint(context, endpoint, invalidEndpoints);
        }

        MarkDuplicates(
            context,
            endpoints,
            static endpoint => endpoint.OperationId,
            DuplicateOperationId,
            static endpoint => new object[] { endpoint.OperationId },
            invalidEndpoints,
            StringComparer.Ordinal);
        MarkDuplicates(
            context,
            endpoints.Where(static endpoint => IsSupportedMethod(endpoint.MethodValue) && IsValidRoute(endpoint.Route)),
            static endpoint => $"{endpoint.MethodValue}:{endpoint.Route}",
            DuplicateRoute,
            static endpoint => new object[] { GetMethodName(endpoint.MethodValue), endpoint.Route! },
            invalidEndpoints,
            StringComparer.OrdinalIgnoreCase);

        var validEndpoints = endpoints
            .Where(endpoint => !invalidEndpoints.Contains(endpoint))
            .OrderBy(static endpoint => endpoint.FullyQualifiedTypeName, StringComparer.Ordinal)
            .ToArray();

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine("using Microsoft.AspNetCore.Builder;");
        source.AppendLine();
        source.AppendLine("namespace MelodyTrack.Backend.Api;");
        source.AppendLine();
        source.AppendLine("internal static class GeneratedApiEndpointMappings");
        source.AppendLine("{");
        source.AppendLine("    internal static global::Microsoft.AspNetCore.Routing.RouteGroupBuilder MapGeneratedApiEndpoints(");
        source.AppendLine("        this global::Microsoft.AspNetCore.Routing.RouteGroupBuilder endpoints)");
        source.AppendLine("    {");

        foreach (var endpoint in validEndpoints)
        {
            source.Append("        endpoints.Map")
                .Append(GetMethodName(endpoint.MethodValue))
                .Append('(')
                .Append(SymbolDisplay.FormatLiteral(endpoint.Route!, true))
                .Append(", ")
                .Append(endpoint.FullyQualifiedTypeName)
                .Append(".HandleAsync)")
                .AppendLine()
                .Append("            .WithName(")
                .Append(SymbolDisplay.FormatLiteral(endpoint.OperationId, true))
                .AppendLine(")")
                .AppendLine("            .DisableValidation();");
        }

        source.AppendLine();
        source.AppendLine("        return endpoints;");
        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource("GeneratedApiEndpointMappings.g.cs", source.ToString());
    }

    private static void ValidateEndpoint(
        SourceProductionContext context,
        EndpointModel endpoint,
        HashSet<EndpointModel> invalidEndpoints)
    {
        if (!endpoint.ClassName.EndsWith("Endpoint", StringComparison.Ordinal))
        {
            Report(context, InvalidClassName, endpoint.Location, endpoint.ClassName);
            invalidEndpoints.Add(endpoint);
        }

        if (endpoint.Handlers.Length == 0)
        {
            Report(context, MissingHandler, endpoint.Location, endpoint.ClassName);
            invalidEndpoints.Add(endpoint);
        }
        else if (endpoint.Handlers.Length > 1)
        {
            Report(context, MultipleHandlers, endpoint.Location, endpoint.ClassName);
            invalidEndpoints.Add(endpoint);
        }
        else
        {
            var handler = endpoint.Handlers[0];
            var handlerLocation = handler.Locations.FirstOrDefault(static location => location.IsInSource) ?? endpoint.Location;
            if (!handler.IsStatic || handler.DeclaredAccessibility != Accessibility.Public)
            {
                Report(context, InvalidHandlerAccessibility, handlerLocation, endpoint.ClassName);
                invalidEndpoints.Add(endpoint);
            }

            if (!handler.Parameters.Any(static parameter => IsCancellationToken(parameter.Type)))
            {
                Report(context, MissingCancellationToken, handlerLocation, endpoint.ClassName);
                invalidEndpoints.Add(endpoint);
            }
        }

        if (!IsSupportedMethod(endpoint.MethodValue))
        {
            Report(context, UnsupportedMethod, endpoint.Location, endpoint.MethodValue);
            invalidEndpoints.Add(endpoint);
        }

        if (!IsValidRoute(endpoint.Route))
        {
            Report(context, InvalidRoute, endpoint.Location, endpoint.Route ?? string.Empty);
            invalidEndpoints.Add(endpoint);
        }

    }

    private static void MarkDuplicates(
        SourceProductionContext context,
        IEnumerable<EndpointModel> endpoints,
        Func<EndpointModel, string> keySelector,
        DiagnosticDescriptor descriptor,
        Func<EndpointModel, object[]> messageArguments,
        HashSet<EndpointModel> invalidEndpoints,
        StringComparer comparer)
    {
        foreach (var duplicateGroup in endpoints.GroupBy(keySelector, comparer).Where(static group => group.Count() > 1))
        {
            foreach (var endpoint in duplicateGroup)
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, endpoint.Location, messageArguments(endpoint)));
                invalidEndpoints.Add(endpoint);
            }
        }
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.Name == nameof(CancellationToken)
               && type.ContainingNamespace.ToDisplayString() == "System.Threading";
    }

    private static bool IsSupportedMethod(int methodValue)
    {
        return methodValue is >= 0 and <= 4;
    }

    private static string GetMethodName(int methodValue)
    {
        return methodValue switch
        {
            0 => "Get",
            1 => "Post",
            2 => "Put",
            3 => "Delete",
            4 => "Patch",
            _ => throw new ArgumentOutOfRangeException(nameof(methodValue), methodValue, null)
        };
    }

    private static bool IsValidRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return false;
        }

        if (route![0] != '/'
            || route.Any(char.IsWhiteSpace)
            || route.IndexOf("//", StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        var parameterStart = -1;
        for (var index = 0; index < route.Length; index++)
        {
            var character = route[index];
            if (character == '{')
            {
                if (parameterStart >= 0)
                {
                    return false;
                }

                parameterStart = index;
            }
            else if (character == '}')
            {
                if (parameterStart < 0 || index == parameterStart + 1)
                {
                    return false;
                }

                parameterStart = -1;
            }
        }

        return parameterStart < 0;
    }

    private static void Report(SourceProductionContext context, DiagnosticDescriptor descriptor, Location location, params object[] arguments)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));
    }

    private sealed class EndpointModel
    {
        public EndpointModel(
            string className,
            string fullyQualifiedTypeName,
            int methodValue,
            string? route,
            string operationId,
            ImmutableArray<IMethodSymbol> handlers,
            Location location)
        {
            ClassName = className;
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            MethodValue = methodValue;
            Route = route;
            OperationId = operationId;
            Handlers = handlers;
            Location = location;
        }

        public string ClassName { get; }

        public string FullyQualifiedTypeName { get; }

        public int MethodValue { get; }

        public string? Route { get; }

        public string OperationId { get; }

        public ImmutableArray<IMethodSymbol> Handlers { get; }

        public Location Location { get; }
    }
}
