using TinyLink.Contracts;

namespace OperatorConsole.Services;

public sealed class AnalyticsApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<LinkStatsResponse>> ListStatsAsync(int take, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<LinkStatsResponse>>($"api/stats?take={take}", ct) ?? [];
}
