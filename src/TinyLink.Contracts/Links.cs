namespace TinyLink.Contracts;

public record CreateLinkRequest(string Url, string? CustomCode);

public record LinkResponse(string Code, string Url, DateTime CreatedAtUtc);
