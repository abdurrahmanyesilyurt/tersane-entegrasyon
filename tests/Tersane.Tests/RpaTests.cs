using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Tersane.Rpa;

namespace Tersane.Tests;

public sealed class RpaExcelTests
{
    [Fact]
    public void Sample_workbook_is_read_with_turkish_numbers_and_input_errors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"irsaliye-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelIo.CreateSample(path);

            var rows = ExcelIo.Read(path);

            Assert.Equal(10, rows.Count);
            Assert.Equal(450.5m, rows.Single(r => r.MalzemeKodu == "WL-E71T1-12").Miktar);
            var broken = Assert.Single(rows, r => r.InputError is not null);
            Assert.Contains("Miktar boş", broken.InputError);
            Assert.True(rows[0].KaliteKontrol);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// The bot against the real legacy app on a real Kestrel port, in headless Chromium.
/// Skipped when Chromium is not installed (`dotnet run --project src/Tersane.Rpa -- --tarayici-kur`); CI installs it.
/// </summary>
[Trait("Category", "E2E")]
public sealed class RpaEndToEndTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<EskiAmbarEntryPoint> _legacy = new WebApplicationFactory<EskiAmbarEntryPoint>()
        .WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:EskiAmbar", ""); // in-memory store
            b.UseSetting("Legacy:AjaxDelayMs", "150");
            b.UseSetting("Legacy:SaveDelayMs", "50");
            b.UseUrls("http://127.0.0.1:0");
        });

    private readonly string _out = Path.Combine(Path.GetTempPath(), $"rpa-test-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        _legacy.UseKestrel();
        _legacy.StartServer();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _legacy.DisposeAsync();
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    [Fact]
    public async Task Bot_enters_valid_rows_reports_rejections_and_never_duplicates()
    {
        var address = _legacy.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var excel = Path.Combine(_out, "irsaliye.xlsx");
        Directory.CreateDirectory(_out);
        ExcelIo.CreateSample(excel);
        var rows = ExcelIo.Read(excel);
        using var log = new RunLog(Path.Combine(_out, "rpa.log"));
        var options = new BotOptions { BaseUrl = new Uri(address), User = "rpa.bot", Password = "Demo-Bot-2026", OutputDir = _out, TimeoutMs = 10_000 };

        var results = new List<RowResult>();
        await using (var bot = new AmbarBot(options, log))
        {
            try
            {
                await bot.StartAsync();
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
            {
                Assert.Skip("Chromium kurulu değil (dotnet run --project src/Tersane.Rpa -- --tarayici-kur).");
            }

            foreach (var row in rows)
                results.Add(await bot.ProcessAsync(row));

            // Second pass over the same file: everything already entered is recognised, nothing is saved twice.
            var rerun = await bot.ProcessAsync(rows[1]);
            Assert.Equal(RowStatus.ZatenKayitli, rerun.Status);
        }

        Assert.Equal(7, results.Count(r => r.Status == RowStatus.Basarili));
        Assert.Equal(1, results.Count(r => r.Status == RowStatus.ZatenKayitli));   // duplicate line inside the file
        Assert.Equal(1, results.Count(r => r.Status == RowStatus.Atlandi));        // empty quantity in Excel
        Assert.Contains(results, r => r.Status == RowStatus.Hatali && r.Message.Contains("Malzeme bulunamadı"));
        Assert.All(results.Where(r => r.Status == RowStatus.Basarili), r => Assert.Matches(@"^AG-\d{6}$", r.FisNo));
        Assert.True(File.Exists(Path.Combine(_out, results.First(r => r.Screenshot is not null).Screenshot!)));

        var resultFile = Path.Combine(_out, "sonuc.xlsx");
        ExcelIo.WriteResults(excel, resultFile, results, DateTime.UtcNow, address);
        Assert.True(new FileInfo(resultFile).Length > 0);
    }
}
