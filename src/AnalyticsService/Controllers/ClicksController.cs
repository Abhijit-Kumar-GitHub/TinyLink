using System.Data;
using System.Text.RegularExpressions;
using AnalyticsService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TinyLink.Contracts;

namespace AnalyticsService.Controllers;

// No API key: only the redirect service calls this, and it is never exposed through the Ingress.
[ApiController]
[Route("api/clicks")]
public partial class ClicksController(AnalyticsDbContext db) : ControllerBase
{
    public const int MaxBatchSize = 1000;
    private const int MaxTextLength = 512;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,16}$")]
    private static partial Regex CodePattern();

    [HttpPost]
    public async Task<IActionResult> Record(RecordClickRequest request, CancellationToken ct)
    {
        if (!IsValid(request))
        {
            ModelState.AddModelError(nameof(request.Code), "Code must be 1-16 characters: letters, digits, '-' or '_'.");
            return ValidationProblem(ModelState);
        }

        db.ClickEvents.Add(ToEntity(request, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);
        return Accepted();
    }

    // The redirect service's normal path: one request and one SaveChanges for many clicks.
    // Per-click requests topped out around 26 clicks/s under load.
    [HttpPost("batch")]
    public async Task<ActionResult<RecordClicksBatchResponse>> RecordBatch(RecordClicksBatchRequest request, CancellationToken ct)
    {
        if (request.Clicks is not { Count: > 0 and <= MaxBatchSize })
        {
            ModelState.AddModelError(nameof(request.Clicks), $"Send between 1 and {MaxBatchSize} clicks.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var valid = request.Clicks.Where(IsValid).Select(c => ToEntity(c, now)).ToList();
        await BulkInsertAsync(valid, ct);

        return Accepted(new RecordClicksBatchResponse(valid.Count, request.Clicks.Count - valid.Count));
    }

    // SqlBulkCopy streams rows in one bulk-load operation; EF inserted ~300 rows/s at 0.5 CPU.
    private async Task BulkInsertAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken ct)
    {
        if (clicks.Count == 0)
        {
            return;
        }

        var table = new DataTable();
        table.Columns.Add(nameof(ClickEvent.Code), typeof(string));
        table.Columns.Add(nameof(ClickEvent.ClickedAtUtc), typeof(DateTime));
        table.Columns.Add(nameof(ClickEvent.Referrer), typeof(string));
        table.Columns.Add(nameof(ClickEvent.UserAgent), typeof(string));
        foreach (var c in clicks)
        {
            table.Rows.Add(c.Code, c.ClickedAtUtc, (object?)c.Referrer ?? DBNull.Value, (object?)c.UserAgent ?? DBNull.Value);
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var bulk = new SqlBulkCopy((SqlConnection)db.Database.GetDbConnection())
            {
                DestinationTableName = "dbo.ClickEvents",
                BatchSize = clicks.Count,
            };
            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulk.WriteToServerAsync(table, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static bool IsValid(RecordClickRequest click) => CodePattern().IsMatch(click.Code ?? "");

    private static ClickEvent ToEntity(RecordClickRequest click, DateTime now) => new()
    {
        Code = click.Code,
        ClickedAtUtc = click.ClickedAtUtc == default || click.ClickedAtUtc > now + AllowedClockSkew
            ? now
            : click.ClickedAtUtc.ToUniversalTime(),
        Referrer = Truncate(click.Referrer),
        UserAgent = Truncate(click.UserAgent),
    };

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) || value.Length <= MaxTextLength ? value : value[..MaxTextLength];
}
