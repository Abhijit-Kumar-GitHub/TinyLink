namespace TinyLink.Contracts;

public record RecordClickRequest(string Code, DateTime ClickedAtUtc, string? Referrer, string? UserAgent);

public record RecordClicksBatchRequest(IReadOnlyList<RecordClickRequest> Clicks);

public record RecordClicksBatchResponse(int Accepted, int Rejected);

public record LinkStatsResponse(string Code, long TotalClicks, long ClicksLast24h, DateTime? LastClickedAtUtc, DateTime? StatsUpdatedAtUtc);
