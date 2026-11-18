using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Tersane.Data.Entities;

namespace Tersane.Tests;

/// <summary>The MVC app rendering against a real database, including an anti-forgery protected form post.</summary>
public sealed partial class WebTests(SqlServer sql) : IAsyncLifetime
{
    private WebApplicationFactory<WebEntryPoint> _app = null!;
    private string _cs = "";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _cs = await sql.CreateDatabaseAsync("web");
        await using (var db = SqlServer.CreateContext(_cs))
            await TestData.SeedMaterialsAsync(db);

        _app = new WebApplicationFactory<WebEntryPoint>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Tersane", _cs);
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("ErpMock:BaseUrl", "http://127.0.0.1:9/"); // nothing listens there; the panel shows "unreachable"
        });
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData("/", "Pano")]
    [InlineData("/projeler", "NB-1042")]
    [InlineData("/projeler/1", "fn_ProjectMaterialSummary")]
    [InlineData("/is-emirleri", "IE-")]
    [InlineData("/malzeme-talepleri", "Malzeme Talepleri")]
    [InlineData("/malzeme-talepleri/yeni", "Malzeme kalemleri")]
    [InlineData("/stok", "vw_StockStatus")]
    [InlineData("/entegrasyon", "Outbox mesajları")]
    public async Task Page_renders(string url, string expected)
    {
        using var client = _app.CreateClient();
        var response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Material_request_form_post_creates_and_queues_a_request()
    {
        using var client = _app.CreateClient(new() { AllowAutoRedirect = false });
        var form = await client.GetStringAsync("/malzeme-talepleri/yeni", Ct);
        var token = TokenPattern().Match(form).Groups[1].Value;
        int materialId;
        await using (var db = SqlServer.CreateContext(_cs))
            materialId = (await db.Materials.SingleAsync(m => m.PartNo == "PR-HP160-9", Ct)).Id;

        var response = await client.PostAsync("/malzeme-talepleri/yeni", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ProjectId"] = "1",
            ["NeedDate"] = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
            ["Lines[0].MaterialId"] = materialId.ToString(),
            ["Lines[0].Quantity"] = "12,5", // Turkish decimal comma, bound with tr-TR culture
            ["submitNow"] = "true",
        }), Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var check = SqlServer.CreateContext(_cs);
        var request = await check.MaterialRequests.Include(r => r.Lines).OrderByDescending(r => r.Id).FirstAsync(Ct);
        Assert.Equal(MaterialRequestStatus.Submitted, request.Status);
        Assert.Equal(12.5m, Assert.Single(request.Lines).Quantity);
        Assert.True(await check.OutboxMessages.AnyAsync(m => m.AggregateId == request.Id.ToString(), Ct));
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        using var client = _app.CreateClient();
        var response = await client.PostAsync("/entegrasyon/mesajlar/yeniden-isle", new FormUrlEncodedContent([]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
