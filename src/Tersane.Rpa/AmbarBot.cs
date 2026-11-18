using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Tersane.Rpa;

public sealed class BotOptions
{
    public required Uri BaseUrl { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public required string OutputDir { get; init; }
    public bool Headless { get; init; } = true;
    public int SlowMoMs { get; init; }
    public int MaxAttempts { get; init; } = 2;
    public bool Trace { get; init; }
    public int TimeoutMs { get; init; } = 15_000;
}

/// <summary>A row the target system rejected on purpose. Not retried: the data must be fixed.</summary>
public sealed class BusinessRuleException(string message, string? fisNo = null) : Exception(message)
{
    public string? FisNo { get; } = fisNo;
}

/// <summary>
/// Enters waybill lines into the legacy "Ambar Giriş Fişi" form, the way a person would:
/// sign in, fill the form, wait for its AJAX lookups, confirm the dialog, read the result.
///
/// Robustness rules:
///  • Selectors use the visible labels (GetByLabel), not the generated WebForms ids, wherever possible.
///  • Never sleep: wait for a concrete state (option present, lookup finished, result label shown).
///  • Business rejections are recorded and not retried; technical failures (timeouts) are retried on a fresh page.
///  • Every row gets a screenshot; an optional Playwright trace records the whole run.
///  • Re-running the same file is safe: the target rejects duplicate waybill lines and the bot reports "Zaten kayıtlı".
/// </summary>
public sealed partial class AmbarBot(BotOptions options, RunLog log) : IAsyncDisposable
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    public async Task StartAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = options.Headless, SlowMo = options.SlowMoMs });
        _context = await _browser.NewContextAsync(new() { Locale = "tr-TR", ViewportSize = new() { Width = 1100, Height = 760 } });
        _context.SetDefaultTimeout(options.TimeoutMs);
        if (options.Trace)
            await _context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = false });
    }

    public async Task<RowResult> ProcessAsync(ReceiptRow row)
    {
        var sw = Stopwatch.StartNew();
        if (row.InputError is not null)
        {
            log.Warn($"Satır {row.RowNumber}: Excel verisi hatalı, girilmedi → {row.InputError}");
            return new RowResult(row.RowNumber, RowStatus.Atlandi, null, row.InputError, sw.Elapsed.TotalSeconds, null, 0);
        }

        for (var attempt = 1; ; attempt++)
        {
            var page = await _context!.NewPageAsync();
            string? dialogMessage = null;
            page.Dialog += async (_, dialog) =>
            {
                // confirm("Fiş kaydedilsin mi?") is accepted; an alert() means the form refused the input.
                if (dialog.Type == DialogType.Alert) dialogMessage = dialog.Message;
                await dialog.AcceptAsync();
            };

            try
            {
                var fisNo = await EnterAsync(page, row, () => dialogMessage);
                var shot = await ScreenshotAsync(page, row, "2-basarili");
                log.Info($"Satır {row.RowNumber}: kaydedildi → {fisNo} ({sw.Elapsed.TotalSeconds:0.0} sn)");
                return new RowResult(row.RowNumber, RowStatus.Basarili, fisNo, "Kaydedildi", sw.Elapsed.TotalSeconds, shot, attempt);
            }
            catch (BusinessRuleException ex)
            {
                var duplicate = ex.FisNo is not null;
                var shot = await ScreenshotAsync(page, row, duplicate ? "2-mukerrer" : "2-red");
                log.Warn($"Satır {row.RowNumber}: {(duplicate ? "zaten kayıtlı" : "sistem reddetti")} → {ex.Message}");
                return new RowResult(row.RowNumber, duplicate ? RowStatus.ZatenKayitli : RowStatus.Hatali, ex.FisNo, ex.Message,
                    sw.Elapsed.TotalSeconds, shot, attempt);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                var shot = await ScreenshotAsync(page, row, $"teknik-{attempt}");
                if (attempt >= options.MaxAttempts)
                {
                    log.Error($"Satır {row.RowNumber}: {attempt} denemede tamamlanamadı → {FirstLine(ex.Message)}");
                    return new RowResult(row.RowNumber, RowStatus.Hatali, null, "Teknik hata: " + FirstLine(ex.Message), sw.Elapsed.TotalSeconds, shot, attempt);
                }
                log.Warn($"Satır {row.RowNumber}: teknik hata, yeniden deneniyor ({attempt}/{options.MaxAttempts}) → {FirstLine(ex.Message)}");
            }
            finally
            {
                await page.CloseAsync();
            }
        }
    }

    private async Task<string> EnterAsync(IPage page, ReceiptRow row, Func<string?> dialog)
    {
        await OpenFormAsync(page);

        await page.GetByLabel("Fiş Tarihi").FillAsync(row.FisTarihi!.Value.ToString("dd.MM.yyyy", Tr));
        await page.GetByLabel("İrsaliye No").FillAsync(row.IrsaliyeNo);

        // Depo → the rack list is loaded by AJAX; wait until the wanted rack is an option.
        await page.GetByLabel("Depo:").SelectOptionAsync(row.Depo);
        var rack = page.Locator("#ctl00_cph_ddlRaf");
        try
        {
            await rack.Locator($"option[value=\"{row.Raf}\"]").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 8000 });
        }
        catch (TimeoutException)
        {
            if (await rack.Locator("option").CountAsync() > 1)
                throw new BusinessRuleException($"Raf '{row.Raf}' bu depoda yok ({row.Depo}).");
            throw; // list never loaded: technical, retry
        }
        await rack.SelectOptionAsync(row.Raf);

        await page.GetByLabel("Tedarikçi").FillAsync(row.Tedarikci);

        // Material code → lookup on blur fills the name or shows "Malzeme bulunamadı!".
        await page.GetByLabel("Malzeme Kodu").FillAsync(row.MalzemeKodu);
        await page.GetByLabel("Malzeme Kodu").BlurAsync();
        await page.WaitForFunctionAsync("""
            () => {
                const name = document.querySelector('#ctl00_cph_txtMalzemeAdi').value;
                const err = document.querySelector('#ctl00_cph_lblMalzemeHata').textContent;
                return (name && name !== 'Sorgulanıyor...') || err;
            }
            """);
        var lookupError = (await page.Locator("#ctl00_cph_lblMalzemeHata").TextContentAsync())?.Trim();
        if (!string.IsNullOrEmpty(lookupError))
            throw new BusinessRuleException($"{lookupError} ({row.MalzemeKodu})");

        await page.GetByLabel("Miktar").FillAsync(row.Miktar!.Value.ToString("0.###", Tr)); // "450,5"
        await page.GetByLabel("Kalite kontrol").SetCheckedAsync(row.KaliteKontrol);
        if (row.Aciklama is not null)
            await page.GetByLabel("Açıklama").FillAsync(row.Aciklama);

        // Evidence of what was typed, before the system answers.
        await ScreenshotAsync(page, row, "1-dolduruldu");

        // Kaydet → confirm() (accepted by the handler) → full postback → result label.
        await page.GetByRole(AriaRole.Button, new() { Name = "Kaydet" }).ClickAsync();
        var result = page.Locator("#ctl00_cph_lblSonuc, #ctl00_cph_lblHata");
        try
        {
            await result.First.WaitForAsync(new() { Timeout = options.TimeoutMs });
        }
        catch (TimeoutException) when (dialog() is { } alert)
        {
            throw new BusinessRuleException("Form uyarısı: " + alert.ReplaceLineEndings(" "));
        }

        if (await page.Locator("#ctl00_cph_lblSonuc").CountAsync() > 0)
        {
            var text = await page.Locator("#ctl00_cph_lblSonuc").InnerTextAsync();
            return FisNoPattern().Match(text) is { Success: true } m ? m.Value : throw new PlaywrightException($"Fiş no okunamadı: {text}");
        }

        var error = (await page.Locator("#ctl00_cph_lblHata").InnerTextAsync()).ReplaceLineEndings(" ").Replace("•", "").Trim();
        var existing = error.Contains("daha önce girilmiş", StringComparison.Ordinal) ? FisNoPattern().Match(error) : null;
        throw new BusinessRuleException(error, existing is { Success: true } ? existing.Value : null);
    }

    /// <summary>Opens the form; if the session expired (redirect to the login page) signs in again.</summary>
    private async Task OpenFormAsync(IPage page)
    {
        await page.GotoAsync(new Uri(options.BaseUrl, "/AmbarGiris.aspx").ToString());
        // Exact path check: "/AmbarGiris.aspx" also ends with "Giris.aspx".
        if (!new Uri(page.Url).AbsolutePath.Equals("/Giris.aspx", StringComparison.OrdinalIgnoreCase))
            return;

        log.Info("Oturum yok / süresi doldu → giriş yapılıyor.");
        await page.GetByLabel("Kullanıcı Adı").FillAsync(options.User);
        await page.GetByLabel("Şifre").FillAsync(options.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Giriş" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("AmbarGiris\\.aspx", RegexOptions.IgnoreCase));
        if (await page.Locator("#lblHata").CountAsync() > 0)
            throw new InvalidOperationException("Eski sisteme giriş yapılamadı: kullanıcı adı/şifre hatalı.");
    }

    private async Task<string?> ScreenshotAsync(IPage page, ReceiptRow row, string tag)
    {
        try
        {
            var dir = Path.Combine(options.OutputDir, "screenshots");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"satir-{row.RowNumber:000}-{tag}.png");
            await page.ScreenshotAsync(new() { Path = file, FullPage = true });
            return Path.GetRelativePath(options.OutputDir, file).Replace('\\', '/');
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            if (options.Trace)
                await _context.Tracing.StopAsync(new() { Path = Path.Combine(options.OutputDir, "trace.zip") });
            await _context.CloseAsync();
        }
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();

    [GeneratedRegex(@"AG-\d{6}")]
    private static partial Regex FisNoPattern();
}

/// <summary>Writes the run log to the console and to rpa.log in the output folder.</summary>
public sealed class RunLog : IDisposable
{
    private readonly StreamWriter _file;
    private readonly Lock _gate = new();

    public RunLog(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _file = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public void Info(string message) => Write("INFO ", message, ConsoleColor.Gray);
    public void Warn(string message) => Write("WARN ", message, ConsoleColor.Yellow);
    public void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

    private void Write(string level, string message, ConsoleColor color)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        lock (_gate)
        {
            _file.WriteLine(line);
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
        }
    }

    public void Dispose() => _file.Dispose();
}
