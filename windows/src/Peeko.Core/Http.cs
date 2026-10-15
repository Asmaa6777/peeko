using System.Net.Http;

namespace Peeko.Core;

public sealed record HttpRequest(
    string Url,
    string Method = "GET",
    string? Body = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>Cached per URL + body, like the macOS fetcher.</summary>
    public string CacheKey => Url + (string.IsNullOrEmpty(Body) ? "" : " " + Body);

    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u.Host : "the site";
}

public sealed record HttpResult(int Status, byte[] Body, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>The one seam for the network, so checks can inject canned responses.</summary>
public interface IHttp
{
    Task<HttpResult> SendAsync(HttpRequest request, CancellationToken ct = default);
}

public sealed class HttpClientAdapter : IHttp
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public async Task<HttpResult> SendAsync(HttpRequest request, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body is not null)
            message.Content = new StringContent(request.Body, System.Text.Encoding.UTF8, "application/json");
        foreach (var (k, v) in request.Headers ?? new Dictionary<string, string>())
        {
            if (!message.Headers.TryAddWithoutValidation(k, v))
                message.Content?.Headers.TryAddWithoutValidation(k, v);
        }
        using var response = await Client.SendAsync(message, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        return new HttpResult((int)response.StatusCode, body, headers);
    }
}
