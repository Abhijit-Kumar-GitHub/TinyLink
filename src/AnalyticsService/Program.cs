using AnalyticsService;
using AnalyticsService.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AnalyticsServiceDb"),
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton<StatsRecalculator>();
builder.Services.AddHostedService<StatsRecalculationService>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AnalyticsDbContext>(tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapControllers();

app.Run();
