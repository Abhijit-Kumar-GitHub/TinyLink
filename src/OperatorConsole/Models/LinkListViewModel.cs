namespace OperatorConsole.Models;

public record LinkRow(string Code, string ShortUrl, string Url, DateTime CreatedAtUtc, long? TotalClicks, long? ClicksLast24h);

public class LinkListViewModel
{
    public string? Search { get; init; }
    public IReadOnlyList<LinkRow> Links { get; init; } = [];
    public bool LinkServiceAvailable { get; init; } = true;
    public bool AnalyticsAvailable { get; init; } = true;
}
