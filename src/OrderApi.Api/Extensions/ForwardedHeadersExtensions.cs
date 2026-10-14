using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace OrderApi.Api.Extensions;

public static class ForwardedHeadersExtensions
{
    public const string ProxiesKey = "ForwardedHeaders:KnownProxies";

    /// <summary>
    /// Behind a reverse proxy the remote address is the proxy's, so every anonymous caller would share one
    /// rate limit partition. X-Forwarded-For is only honoured from the proxies listed (comma separated) in
    /// ForwardedHeaders:KnownProxies; when none is configured the header is ignored, so it cannot be spoofed.
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = (configuration[ProxiesKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(IPAddress.Parse)
            .ToArray();
        if (proxies.Length == 0)
        {
            return services;
        }

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });
    }
}
