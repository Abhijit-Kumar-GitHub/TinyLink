using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using TinyLink.ServiceDefaults;

namespace RedirectService;

// Entries are fresh for CacheSeconds. After that a lookup refreshes them, but if the link service
// is unreachable the last known URL is served for up to StaleSeconds instead of failing.
public sealed class LinkLookupClient(HttpClient http, IMemoryCache cache, IConfiguration config, ILogger<LinkLookupClient> logger)
{
    private sealed record Entry(string? Url, DateTime FreshUntilUtc);

    private static readonly Counter<long> Lookups = TinyLinkMetrics.Meter.CreateCounter<long>(
        "tinylink.redirect.lookups", description: "Short-code resolutions by outcome: cache_hit, fetched, not_found, stale, error");

    private readonly TimeSpan _freshTtl = TimeSpan.FromSeconds(config.GetValue("Redirect:CacheSeconds", 60));
    private readonly TimeSpan _staleTtl = TimeSpan.FromSeconds(config.GetValue("Redirect:StaleSeconds", 3600));
    private readonly TimeSpan _notFoundTtl = TimeSpan.FromSeconds(config.GetValue("Redirect:NotFoundCacheSeconds", 10));

    /// <returns>The target URL, or null when the code does not exist.</returns>
    public async Task<string?> ResolveAsync(string code, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        cache.TryGetValue(code, out Entry? entry);
        if (entry is not null && entry.FreshUntilUtc > now)
        {
            Record("cache_hit");
            return entry.Url;
        }

        string? url;
        try
        {
            url = await FetchAsync(code, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (entry?.Url is null)
            {
                Record("error");
                throw;
            }

            Record("stale");
            logger.LogWarning("Link service unavailable, serving stale URL for {Code}: {Error}", code, ex.Message);
            return entry.Url;
        }

        Record(url is null ? "not_found" : "fetched");
        cache.Set(code, new Entry(url, now + (url is null ? _notFoundTtl : _freshTtl)), new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = url is null ? _notFoundTtl : _staleTtl,
        });
        return url;
    }

    private static void Record(string result) => Lookups.Add(1, new KeyValuePair<string, object?>("result", result));

    private async Task<string?> FetchAsync(string code, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/links/{Uri.EscapeDataString(code)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var link = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.LinkResponse, ct);
        return link?.Url;
    }
}
