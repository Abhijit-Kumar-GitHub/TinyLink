namespace TinyLink.Contracts;

public record RecordClickRequest(string Code, DateTime ClickedAtUtc, string? Referrer, string? UserAgent);

public record LinkStatsResponse(string Code, long TotalClicks, long ClicksLast24h, DateTime? LastClickedAtUtc, DateTime? StatsUpdatedAtUtc);
