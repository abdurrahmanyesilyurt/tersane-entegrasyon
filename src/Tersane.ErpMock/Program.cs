using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.Options;
using Tersane.ErpMock;

// ─────────────────────────────────────────────────────────────────────────────
//  MOCK ERP — demo/portfolio only.
//  Imitates the OData "projection" style of IFS Cloud. It is NOT IFS Cloud and
//  shares no code, schema or data with any real ERP product.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection("Simulation"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<ErpStore>();
builder.Services.AddSingleton<ChaosSettings>();
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddHostedService<ErpSimulation>();
builder.Services.AddControllers().AddOData(ErpProjections.Configure);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<ErpGatewayMiddleware>();
app.MapControllers();

// OAuth2 client credentials (mock). Real systems would use an identity provider.
app.MapPost("/mock-erp/auth/token", (HttpRequest request, TokenStore tokens, IOptions<AuthOptions> auth) =>
{
    var form = request.HasFormContentType ? request.Form : null;
    string? Field(string name) => form?[name].ToString();
    if (Field("grant_type") != "client_credentials")
        return Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400);
    if (Field("client_id") != auth.Value.ClientId || Field("client_secret") != auth.Value.ClientSecret)
        return Results.Json(new { error = "invalid_client" }, statusCode: 401);

    var (token, expiresIn) = tokens.Issue(auth.Value.TokenLifetimeSeconds);
    return Results.Json(new { access_token = token, token_type = "Bearer", expires_in = expiresIn });
}).DisableAntiforgery();

// Demo controls. Not part of the imitated API.
var admin = app.MapGroup("/mock-admin");
admin.MapGet("/chaos", (ChaosSettings chaos) => chaos.Snapshot());
admin.MapPut("/chaos", (ChaosUpdate update, ChaosSettings chaos) => { chaos.Apply(update); return chaos.Snapshot(); });
admin.MapGet("/state", (ErpStore store, ChaosSettings chaos) => new
{
    chaos = chaos.Snapshot(),
    requisitions = store.Requisitions().OrderByDescending(r => r.CreatedAt),
    stock = store.Stock().OrderBy(s => s.PartNo),
    parts = store.Parts().OrderBy(p => p.PartNo),
});
admin.MapPost("/requisitions/{no}/advance", (string no, ErpStore store)
    => store.Advance(no) is { } r ? Results.Ok(r) : Results.NotFound());
admin.MapPost("/requisitions/{no}/cancel", (string no, ErpStore store)
    => store.Cancel(no) is { } r ? Results.Ok(r) : Results.NotFound());
admin.MapPost("/parts/{partNo}/blocked/{blocked:bool}", (string partNo, bool blocked, ErpStore store)
    => store.SetPartBlocked(partNo, blocked) ? Results.NoContent() : Results.NotFound());
admin.MapPost("/tokens/revoke", (TokenStore tokens) => { tokens.RevokeAll(); return Results.NoContent(); });
admin.MapPost("/reset", (ErpStore store, ChaosSettings chaos) =>
{
    store.Reset();
    chaos.Apply(new ChaosUpdate(false, 0, 0, 0));
    return Results.NoContent();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", system = "mock-erp" }));

app.Run();

/// <summary>Marker type for WebApplicationFactory in tests.</summary>
public sealed class ErpMockEntryPoint;
