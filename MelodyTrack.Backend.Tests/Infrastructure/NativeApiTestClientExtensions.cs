using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Tests.Infrastructure;

internal static class NativeApiTestClientExtensions
{
    public static Task<(HttpResponseMessage Response, TResponse Result)> GETAsync<TEndpoint, TRequest, TResponse>(
        this HttpClient client,
        TRequest request) => SendAsync<TEndpoint, TRequest, TResponse>(client, HttpMethod.Get, request);

    public static Task<HttpResponseMessage> GETAsync<TEndpoint, TRequest>(this HttpClient client, TRequest request) =>
        SendAsync<TEndpoint, TRequest>(client, HttpMethod.Get, request);

    public static Task<(HttpResponseMessage Response, TResponse Result)> POSTAsync<TEndpoint, TRequest, TResponse>(
        this HttpClient client,
        TRequest request) => SendAsync<TEndpoint, TRequest, TResponse>(client, HttpMethod.Post, request);

    public static Task<HttpResponseMessage> POSTAsync<TEndpoint, TRequest>(this HttpClient client, TRequest request) =>
        SendAsync<TEndpoint, TRequest>(client, HttpMethod.Post, request);

    public static Task<(HttpResponseMessage Response, TResponse Result)> PUTAsync<TEndpoint, TRequest, TResponse>(
        this HttpClient client,
        TRequest request) => SendAsync<TEndpoint, TRequest, TResponse>(client, HttpMethod.Put, request);

    public static Task<HttpResponseMessage> PUTAsync<TEndpoint, TRequest>(this HttpClient client, TRequest request) =>
        SendAsync<TEndpoint, TRequest>(client, HttpMethod.Put, request);

    public static Task<(HttpResponseMessage Response, TResponse Result)> PATCHAsync<TEndpoint, TRequest, TResponse>(
        this HttpClient client,
        TRequest request) => SendAsync<TEndpoint, TRequest, TResponse>(client, HttpMethod.Patch, request);

    public static Task<HttpResponseMessage> PATCHAsync<TEndpoint, TRequest>(this HttpClient client, TRequest request) =>
        SendAsync<TEndpoint, TRequest>(client, HttpMethod.Patch, request);

    public static Task<(HttpResponseMessage Response, TResponse Result)> DELETEAsync<TEndpoint, TRequest, TResponse>(
        this HttpClient client,
        TRequest request) => SendAsync<TEndpoint, TRequest, TResponse>(client, HttpMethod.Delete, request);

    public static Task<HttpResponseMessage> DELETEAsync<TEndpoint, TRequest>(this HttpClient client, TRequest request) =>
        SendAsync<TEndpoint, TRequest>(client, HttpMethod.Delete, request);

    private static async Task<(HttpResponseMessage Response, TResponse Result)> SendAsync<TEndpoint, TRequest, TResponse>(
        HttpClient client,
        HttpMethod method,
        TRequest request)
    {
        var response = await SendAsync<TEndpoint, TRequest>(client, method, request);
        if (response.Content.Headers.ContentLength is null or 0)
        {
            return (response, default!);
        }

        if (!response.IsSuccessStatusCode
            && typeof(TResponse) != typeof(ApiProblemDetails)
            && !typeof(ProblemDetails).IsAssignableFrom(typeof(TResponse)))
        {
            return (response, default!);
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);
        return (response, result!);
    }

    private static async Task<HttpResponseMessage> SendAsync<TEndpoint, TRequest>(
        HttpClient client,
        HttpMethod method,
        TRequest request)
    {
        var uri = BuildUri<TEndpoint, TRequest>(request);
        using var message = new HttpRequestMessage(method, uri);
        if (method is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            message.Content = JsonContent.Create(request, options: JsonSerializerOptions.Web);
        }
        return await client.SendAsync(message, TestContext.Current.CancellationToken);
    }

    private static string BuildUri<TEndpoint, TRequest>(TRequest request)
    {
        var endpoint = typeof(TEndpoint).GetCustomAttribute<ApiEndpointAttribute>()
            ?? throw new InvalidOperationException($"{typeof(TEndpoint).Name} is not an API endpoint.");
        var route = endpoint.Route;
        var query = new List<string>();
        if (request is null || request is EmptyRequest)
        {
            return route;
        }

        foreach (var property in typeof(TRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(request);
            var routeAttribute = property.GetCustomAttribute<FromRouteAttribute>();
            var routeName = routeAttribute?.Name ?? property.Name;
            var placeholder = "{" + routeName + "}";
            if (route.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                route = ReplaceOrdinalIgnoreCase(route, placeholder, Format(value));
                continue;
            }

            var queryAttribute = property.GetCustomAttribute<FromQueryAttribute>();
            if (endpoint.Method is ApiMethod.Get or ApiMethod.Delete || queryAttribute is not null)
            {
                if (value is null)
                {
                    continue;
                }
                var name = queryAttribute?.Name
                    ?? property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                query.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(Format(value))}");
            }
        }

        return query.Count == 0 ? route : $"{route}?{string.Join('&', query)}";
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? source : string.Concat(source.AsSpan(0, index), newValue, source.AsSpan(index + oldValue.Length));
    }
}
