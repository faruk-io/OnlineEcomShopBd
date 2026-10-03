using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechBazar.Api.Extensions;

namespace TechBazar.IntegrationTests;

/// <summary>
/// Runs the real forwarded-headers configuration on a tiny host with a controlled TCP peer address. The security property under test:
/// the client address is only taken from X-Forwarded-For when the direct peer is a configured proxy, and spoofed leading entries never win.
/// </summary>
public class ForwardedHeadersTests
{
    private static async Task<string> ClientIpSeenByApi(string peer, string? xForwardedFor, string? proto, params (string Key, string Value)[] config)
    {
        using var host = await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(config.ToDictionary(x => x.Key, x => (string?)x.Value)))
            .ConfigureServices((ctx, s) => s.AddForwardedHeadersFromConfig(ctx.Configuration))
            .Configure(app =>
            {
                app.Use((c, next) => { c.Connection.RemoteIpAddress = IPAddress.Parse(peer); return next(); });   // what the socket says
                app.UseForwardedHeaders();
                app.Run(c => c.Response.WriteAsync($"{c.Connection.RemoteIpAddress}|{c.Request.Scheme}"));
            })).StartAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        if (xForwardedFor is not null) req.Headers.Add("X-Forwarded-For", xForwardedFor);
        if (proto is not null) req.Headers.Add("X-Forwarded-Proto", proto);
        return await (await host.GetTestClient().SendAsync(req)).Content.ReadAsStringAsync();
    }

    private static readonly (string, string) DockerNet = ("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12");

    [Fact]
    public async Task ATrustedProxyChainYieldsTheRealVisitor_AndASpoofedLeadingEntryIsIgnored()
    {
        // visitor 203.0.113.9 sent "X-Forwarded-For: 1.2.3.4" (a lie); the SSR container appended the visitor's real address and its own peer is in the docker network
        var seen = await ClientIpSeenByApi("172.18.0.5", "1.2.3.4, 203.0.113.9", "https", DockerNet);
        Assert.Equal("203.0.113.9|https", seen);
    }

    [Fact]
    public async Task AUntrustedPeerCannotSetItsOwnAddressOrScheme()
    {
        var seen = await ClientIpSeenByApi("198.51.100.77", "10.0.0.1", "https", DockerNet);   // a direct internet client pretending to be internal / TLS
        Assert.Equal("198.51.100.77|http", seen);
    }

    [Fact]
    public async Task WithNothingConfigured_NoForwardedHeaderIsTrusted()
    {
        var seen = await ClientIpSeenByApi("172.18.0.5", "203.0.113.9", "https");
        Assert.Equal("172.18.0.5|http", seen);
    }

    [Fact]
    public async Task AnExplicitlyListedProxyAddressIsTrusted()
    {
        var seen = await ClientIpSeenByApi("10.9.8.7", "203.0.113.9", null, ("ForwardedHeaders:KnownProxies:0", "10.9.8.7"));
        Assert.Equal("203.0.113.9|http", seen);
    }

    [Fact]
    public async Task ADirectRequestWithoutHeadersKeepsThePeerAddress()
    {
        var seen = await ClientIpSeenByApi("172.18.0.5", null, null, DockerNet);
        Assert.Equal("172.18.0.5|http", seen);
    }
}
