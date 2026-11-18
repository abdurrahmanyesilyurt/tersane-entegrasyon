using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TersaneDbContext>(o => o.UseSqlServer(
    builder.Configuration.GetConnectionString("Tersane"),
    sql => sql.EnableRetryOnFailure(3)));
builder.Services.AddScoped<TersaneSql>();
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddHealthChecks().AddDbContextCheck<TersaneDbContext>();
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.Section));
builder.Services.AddHttpClient<ErpMockAdminClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["ErpMock:BaseUrl"] ?? "http://localhost:5101/");
    c.Timeout = TimeSpan.FromSeconds(5);
});

// Read-only JSON for the optional React monitoring panel (Vite dev server).
builder.Services.AddCors(o => o.AddPolicy("panel", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:PanelOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .WithMethods("GET")));

var app = builder.Build();

// `dotnet Tersane.Web.dll --migrate` applies migrations + demo data and exits (used by docker compose).
if (args.Contains("--migrate") || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await DemoSeeder.MigrateAndSeedAsync(scope.ServiceProvider.GetRequiredService<TersaneDbContext>(),
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrate"));
    if (args.Contains("--migrate"))
        return;
}

var turkish = new CultureInfo("tr-TR");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(turkish),
    SupportedCultures = [turkish],
    SupportedUICultures = [turkish],
});

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/hata");

app.UseRouting();
app.UseCors();
app.MapStaticAssets();
app.MapControllers().WithStaticAssets();
app.MapHealthChecks("/health");

app.Run();

/// <summary>Marker type for WebApplicationFactory in tests.</summary>
public sealed class WebEntryPoint;
