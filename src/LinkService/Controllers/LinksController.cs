using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LinkService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinyLink.Contracts;

namespace LinkService.Controllers;

[ApiController]
[Route("api/links")]
public partial class LinksController(LinkDbContext db) : ControllerBase
{
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int GeneratedCodeLength = 7;
    private const int MaxGenerateAttempts = 5;

    [GeneratedRegex("^[A-Za-z0-9_-]{3,16}$")]
    private static partial Regex CustomCodePattern();

    [HttpPost]
    [ApiKey]
    public async Task<ActionResult<LinkResponse>> Create(CreateLinkRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            request.Url.Length > 2048)
        {
            ModelState.AddModelError(nameof(request.Url), "Url must be an absolute http or https URL up to 2048 characters.");
            return ValidationProblem(ModelState);
        }

        var custom = string.IsNullOrWhiteSpace(request.CustomCode) ? null : request.CustomCode.Trim();
        if (custom is not null && !CustomCodePattern().IsMatch(custom))
        {
            ModelState.AddModelError(nameof(request.CustomCode), "Custom code must be 3-16 characters: letters, digits, '-' or '_'.");
            return ValidationProblem(ModelState);
        }

        for (var attempt = 0; attempt < (custom is null ? MaxGenerateAttempts : 1); attempt++)
        {
            var link = new Link
            {
                Code = custom ?? RandomNumberGenerator.GetString(Alphabet, GeneratedCodeLength),
                OriginalUrl = uri.AbsoluteUri,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Links.Add(link);
            try
            {
                await db.SaveChangesAsync(ct);
                return CreatedAtAction(nameof(GetByCode), new { code = link.Code }, ToResponse(link));
            }
            catch (DbUpdateException)
            {
                db.Entry(link).State = EntityState.Detached;
                if (!await CodeExists(link.Code, ct))
                {
                    throw;
                }
            }
        }

        return custom is not null
            ? Conflict(new ProblemDetails { Title = "Code already in use", Detail = $"'{custom}' is taken." })
            : Problem("Could not generate a unique code. Try again.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<LinkResponse>> GetByCode(string code, CancellationToken ct)
    {
        var link = await db.Links.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code, ct);
        return link is null ? NotFound() : ToResponse(link);
    }

    [HttpGet]
    [ApiKey]
    public async Task<IReadOnlyList<LinkResponse>> List([FromQuery] string? search, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var query = db.Links.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(l => l.Code.Contains(search) || l.OriginalUrl.Contains(search));
        }

        return await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .Select(l => new LinkResponse(l.Code, l.OriginalUrl, l.CreatedAtUtc))
            .ToListAsync(ct);
    }

    private Task<bool> CodeExists(string code, CancellationToken ct) =>
        db.Links.AsNoTracking().AnyAsync(l => l.Code == code, ct);

    private static LinkResponse ToResponse(Link link) => new(link.Code, link.OriginalUrl, link.CreatedAtUtc);
}
