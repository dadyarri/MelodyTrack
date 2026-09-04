using System.Net;
using MelodyTrack.Backend.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Backend.Hosting;

public static class ReverseProxyExtensions
{
    public static IServiceCollection AddTrustedReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReverseProxyOptions>()
            .Bind(configuration.GetSection(ReverseProxyOptions.SectionName))
            .Validate(HasValidAddresses, "ReverseProxy entries must be IP addresses or CIDR networks.")
            .ValidateOnStart();

        return services;
    }

    public static WebApplication UseTrustedReverseProxy(this WebApplication app)
    {
        var trustedProxy = app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
        if (trustedProxy.KnownProxies.Length == 0 && trustedProxy.KnownNetworks.Length == 0)
        {
            return app;
        }

        var forwardedHeaders = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        forwardedHeaders.KnownProxies.Clear();
        forwardedHeaders.KnownIPNetworks.Clear();

        foreach (var proxy in trustedProxy.KnownProxies)
        {
            forwardedHeaders.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in trustedProxy.KnownNetworks)
        {
            forwardedHeaders.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        app.UseForwardedHeaders(forwardedHeaders);
        return app;
    }

    private static bool HasValidAddresses(ReverseProxyOptions options)
    {
        return options.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _))
               && options.KnownNetworks.All(network => System.Net.IPNetwork.TryParse(network, out _));
    }
}
