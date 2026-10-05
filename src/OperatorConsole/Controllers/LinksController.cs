using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OperatorConsole.Models;
using OperatorConsole.Services;
using TinyLink.Contracts;

namespace OperatorConsole.Controllers;

[Authorize]
public class LinksController(LinkApiClient links, AnalyticsApiClient analytics, IConfiguration config, ILogger<LinksController> logger)
    : Controller
{
    private const string SearchSessionKey = "Links.Search";
    private const int PageSize = 100;

    private string ShortUrlBase => config["Console:ShortUrlBase"] ?? "";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        // An explicit ?search= (even empty) updates the remembered filter; no parameter reuses it.
        if (Request.Query.ContainsKey("search"))
        {
            HttpContext.Session.SetString(SearchSessionKey, search ?? "");
        }
        else
        {
            search = HttpContext.Session.GetString(SearchSessionKey);
        }

        IReadOnlyList<LinkResponse> linkList;
        try
        {
            linkList = await links.ListAsync(search, PageSize, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Link service unavailable: {Error}", ex.Message);
            return View(new LinkListViewModel { Search = search, LinkServiceAvailable = false });
        }

        Dictionary<string, LinkStatsResponse>? stats = null;
        try
        {
            stats = (await analytics.ListStatsAsync(200, ct)).ToDictionary(s => s.Code, StringComparer.Ordinal);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Analytics service unavailable: {Error}", ex.Message);
        }

        var rows = linkList.Select(l =>
        {
            var s = stats?.GetValueOrDefault(l.Code);
            return new LinkRow(l.Code, ShortUrlBase + l.Code, l.Url, l.CreatedAtUtc,
                stats is null ? null : s?.TotalClicks ?? 0,
                stats is null ? null : s?.ClicksLast24h ?? 0);
        }).ToList();

        return View(new LinkListViewModel { Search = search, Links = rows, AnalyticsAvailable = stats is not null });
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateLinkViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CreateLinkViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        CreateLinkResult result;
        try
        {
            result = await links.CreateAsync(new CreateLinkRequest(model.Url, model.CustomCode), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Create link failed: {Error}", ex.Message);
            ModelState.AddModelError("", "The link service is unavailable. Try again shortly.");
            return View(model);
        }

        if (result.Link is null)
        {
            ModelState.AddModelError(result.ErrorField ?? "", result.Error ?? "Could not create the link.");
            return View(model);
        }

        TempData["Created"] = ShortUrlBase + result.Link.Code;
        return RedirectToAction(nameof(Index));
    }
}
