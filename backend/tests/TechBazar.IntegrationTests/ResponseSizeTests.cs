using TechBazar.IntegrationTests.Support;
using Xunit.Abstractions;

namespace TechBazar.IntegrationTests;

/// <summary>Measures what response compression saves on the heaviest public JSON responses, and guards against it silently turning off.</summary>
public class ResponseSizeTests(ApiFactory factory, ITestOutputHelper output) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<int> Bytes(string url, string? encoding)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (encoding is not null) req.Headers.AcceptEncoding.ParseAdd(encoding);
        var r = await _client.SendAsync(req);
        r.EnsureSuccessStatusCode();
        if (encoding is not null) Assert.Contains(r.Content.Headers.ContentEncoding, e => e == encoding);
        return (await r.Content.ReadAsByteArrayAsync()).Length;
    }

    [Theory]
    [InlineData("/api/v1/products?pageSize=60")]
    [InlineData("/api/v1/products/facets?category=processor")]
    [InlineData("/api/v1/categories")]
    [InlineData("/api/v1/products/amd-ryzen-5-5600-processor")]
    public async Task CompressionShrinksJsonSubstantially(string url)
    {
        var raw = await Bytes(url, null);
        var gzip = await Bytes(url, "gzip");
        var br = await Bytes(url, "br");
        output.WriteLine($"{url,-52} raw {raw,7:N0} B   gzip {gzip,6:N0} B ({100.0 * gzip / raw:0}%)   br {br,6:N0} B ({100.0 * br / raw:0}%)");
        Assert.True(br < raw * 0.5, $"{url}: br {br} vs raw {raw}");
        Assert.True(gzip < raw * 0.5, $"{url}: gzip {gzip} vs raw {raw}");
    }
}
