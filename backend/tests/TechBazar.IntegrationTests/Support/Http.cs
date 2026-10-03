using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechBazar.IntegrationTests.Support;

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<T>(Json))!;

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient c, string url, object body) =>
        c.PostAsJsonAsync(url, body, Json);

    public static HttpRequestMessage WithBearer(this HttpRequestMessage m, string token)
    {
        m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return m;
    }

    public static async Task<JsonDocument> ProblemAsync(this HttpResponseMessage r)
    {
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync());
    }
}
