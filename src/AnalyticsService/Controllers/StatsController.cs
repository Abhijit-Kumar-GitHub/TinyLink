using AnalyticsService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinyLink.Contracts;
using TinyLink.ServiceDefaults;

namespace AnalyticsService.Controllers;

[ApiController]
[Route("api/stats")]
[ApiKey]
public class StatsController(AnalyticsDbContext db, StatsRecalculator recalculator) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<LinkStatsResponse>> List([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var rows = await db.VwLinkClickSummaries.AsNoTracking()
            .OrderByDescending(s => s.TotalClicks)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    [HttpGet("{code}")]
    public async Task<LinkStatsResponse> GetByCode(string code, CancellationToken ct)
    {
        var row = await db.VwLinkClickSummaries.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code, ct);
        return row is null ? new LinkStatsResponse(code, 0, 0, null, null) : ToResponse(row);
    }

    [HttpPost("recalculate")]
    public async Task<IActionResult> Recalculate(CancellationToken ct)
    {
        await recalculator.RunAsync(ct);
        return NoContent();
    }

    private static LinkStatsResponse ToResponse(VwLinkClickSummary s) =>
        new(s.Code, s.TotalClicks, s.ClicksLast24h ?? 0, s.LastClickedAtUtc, s.UpdatedAtUtc);
}
