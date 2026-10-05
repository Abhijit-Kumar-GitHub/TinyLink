using System.Text.RegularExpressions;
using AnalyticsService.Data;
using Microsoft.AspNetCore.Mvc;
using TinyLink.Contracts;

namespace AnalyticsService.Controllers;

// No API key: only the redirect service calls this, and it is never exposed through the Ingress.
[ApiController]
[Route("api/clicks")]
public partial class ClicksController(AnalyticsDbContext db) : ControllerBase
{
    private const int MaxTextLength = 512;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,16}$")]
    private static partial Regex CodePattern();

    [HttpPost]
    public async Task<IActionResult> Record(RecordClickRequest request, CancellationToken ct)
    {
        if (!CodePattern().IsMatch(request.Code ?? ""))
        {
            ModelState.AddModelError(nameof(request.Code), "Code must be 1-16 characters: letters, digits, '-' or '_'.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var clickedAt = request.ClickedAtUtc == default || request.ClickedAtUtc > now + AllowedClockSkew
            ? now
            : request.ClickedAtUtc.ToUniversalTime();

        db.ClickEvents.Add(new ClickEvent
        {
            Code = request.Code!,
            ClickedAtUtc = clickedAt,
            Referrer = Truncate(request.Referrer),
            UserAgent = Truncate(request.UserAgent),
        });
        await db.SaveChangesAsync(ct);

        return Accepted();
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) || value.Length <= MaxTextLength ? value : value[..MaxTextLength];
}
