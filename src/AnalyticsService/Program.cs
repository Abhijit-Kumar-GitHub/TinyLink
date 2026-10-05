using AnalyticsService;
using AnalyticsService.Data;
using Microsoft.EntityFrameworkCore;
using TinyLink.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApiDefaults();
builder.Services.AddTinyLinkMetrics();
builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AnalyticsServiceDb"),
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton<StatsRecalculator>();
builder.Services.AddHostedService<StatsRecalculationService>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AnalyticsDbContext>(tags: [ServiceDefaultsExtensions.ReadyTag]);

var app = builder.Build();

app.UseRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultHealthChecks();
app.MapTinyLinkMetrics(app.Configuration);
app.MapControllers();

app.Run();
