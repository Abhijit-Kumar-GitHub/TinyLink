namespace LinkService.Data;

public class Link
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string OriginalUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
