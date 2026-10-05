using System.Net;
using Microsoft.AspNetCore.Mvc;
using TinyLink.Contracts;

namespace OperatorConsole.Services;

public record CreateLinkResult(LinkResponse? Link, string? ErrorField = null, string? Error = null);

public sealed class LinkApiClient(HttpClient http)
{
    public async Task<CreateLinkResult> CreateAsync(CreateLinkRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/links", request, ct);

        if (response.IsSuccessStatusCode)
        {
            return new CreateLinkResult(await response.Content.ReadFromJsonAsync<LinkResponse>(ct));
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new CreateLinkResult(null, nameof(CreateLinkRequest.CustomCode), "That code is already taken.");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(ct);
            var (field, messages) = problem?.Errors.FirstOrDefault() ?? default;
            return new CreateLinkResult(null, field, messages?.FirstOrDefault() ?? "The link service rejected the request.");
        }

        response.EnsureSuccessStatusCode();
        throw new System.Diagnostics.UnreachableException();
    }

    public async Task<IReadOnlyList<LinkResponse>> ListAsync(string? search, int take, CancellationToken ct)
    {
        var query = $"api/links?take={take}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search)}";
        }

        return await http.GetFromJsonAsync<List<LinkResponse>>(query, ct) ?? [];
    }
}
