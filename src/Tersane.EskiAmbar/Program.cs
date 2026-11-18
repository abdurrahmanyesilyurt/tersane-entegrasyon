using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Tersane.EskiAmbar;

// ─────────────────────────────────────────────────────────────────────────────
//  "Eski Ambar" — a deliberately old-fashioned web form (fictional, demo only).
//  It has no API: the only way to automate it is to drive the UI, which is
//  what the RPA bot (src/Tersane.Rpa, Playwright) does.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EskiAmbar");
builder.Services.AddSingleton<IFisRepository>(string.IsNullOrWhiteSpace(connectionString)
    ? new InMemoryFisRepository()
    : new SqlFisRepository(connectionString));

builder.Services.AddSingleton<LegacyUsers>();
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Giris");
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Giris.aspx";
        o.Cookie.Name = "ASP.NET_SessionId"; // legacy look; it is a normal auth cookie
        // Short sliding session, like the old system: the bot has to notice the login page and sign in again.
        o.ExpireTimeSpan = TimeSpan.FromMinutes(builder.Configuration.GetValue("Legacy:SessionMinutes", 3));
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.GetRequiredService<IFisRepository>().InitializeAsync(CancellationToken.None);

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/", () => Results.Redirect("/AmbarGiris.aspx"));
app.MapGet("/Cikis.aspx", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Redirect("/Giris.aspx");
});

// Old-style AJAX handlers (.ashx). Both are slow on purpose, so the bot must wait for them.
var slowMs = app.Configuration.GetValue("Legacy:AjaxDelayMs", 600);
app.MapGet("/Servis/Raflar.ashx", async (string depo) =>
{
    await Task.Delay(Random.Shared.Next(slowMs / 2, slowMs + 1));
    return LegacyCatalog.Depots.TryGetValue(depo, out var d) ? Results.Json(d.Racks) : Results.Json(Array.Empty<string>());
}).RequireAuthorization();

app.MapGet("/Servis/MalzemeBilgi.ashx", async (string kod) =>
{
    await Task.Delay(Random.Shared.Next(slowMs / 3, slowMs / 2 + 1));
    return LegacyCatalog.Materials.TryGetValue(kod.Trim(), out var m)
        ? Results.Json(new { bulundu = true, ad = m.Name, birim = m.Unit })
        : Results.Json(new { bulundu = false, ad = "", birim = "" });
}).RequireAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", system = "eski-ambar" })).AllowAnonymous();

app.Run();

/// <summary>Demo users of the legacy app (test values only).</summary>
public sealed class LegacyUsers(IConfiguration config)
{
    public bool IsValid(string user, string password)
    {
        var expected = config[$"Legacy:Users:{user}"];
        return expected is not null && expected == password;
    }

    public static ClaimsPrincipal Principal(string user) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, user)], CookieAuthenticationDefaults.AuthenticationScheme));
}

/// <summary>Marker type for WebApplicationFactory in tests.</summary>
public sealed class EskiAmbarEntryPoint;
