using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http.Resilience;
using OperatorConsole.Services;
using TinyLink.ServiceDefaults;

if (args is ["hash-password", var plainPassword])
{
    Console.WriteLine(PasswordHasher.Hash(plainPassword));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddHealthChecks();
builder.Services.AddTinyLinkMetrics();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.Cookie.Name = "tinylink.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.Name = "tinylink.session";
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.IdleTimeout = TimeSpan.FromHours(8);
});

// Behind the Traefik Ingress, RemoteIpAddress is the proxy; take the client IP from X-Forwarded-For so
// the login rate limit is per user. Safe because the console is only reachable through the Ingress,
// and Traefik replaces any client-supplied X-Forwarded-For by default.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});

var apiKey = builder.Configuration["ApiKey"] ?? "";
builder.Services.AddHttpClient<LinkApiClient>(c =>
    {
        c.BaseAddress = new Uri(builder.Configuration["Services:LinkService"]!);
        c.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    })
    // Never retry POST: a retried create could make a duplicate link.
    .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddHttpClient<AnalyticsApiClient>(c =>
    {
        c.BaseAddress = new Uri(builder.Configuration["Services:AnalyticsService"]!);
        c.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    })
    .AddStandardResilienceHandler();

var app = builder.Build();

app.UseForwardedHeaders();
// The console renders an HTML error page rather than the APIs' ProblemDetails handler.
app.UseRequestLogging();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapDefaultHealthChecks();
app.MapTinyLinkMetrics(app.Configuration);
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
