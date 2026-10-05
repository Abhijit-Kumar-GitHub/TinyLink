using LinkService.Data;
using Microsoft.EntityFrameworkCore;
using TinyLink.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApiDefaults();
builder.Services.AddTinyLinkMetrics();
builder.Services.AddDbContext<LinkDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LinkServiceDb"),
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<LinkDbContext>(tags: [ServiceDefaultsExtensions.ReadyTag]);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<LinkDbContext>().Database.MigrateAsync();
}

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
