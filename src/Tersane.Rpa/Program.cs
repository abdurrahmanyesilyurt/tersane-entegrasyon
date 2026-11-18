using System.Globalization;
using Tersane.Rpa;

// Tersane.Rpa — Excel'deki irsaliye satırlarını eski "Ambar Giriş Fişi" formuna giren bot.
//
//   dotnet run --project src/Tersane.Rpa -- --excel samples/ornek-irsaliye.xlsx --url http://localhost:5102
//   dotnet run --project src/Tersane.Rpa -- --ornek-olustur samples/ornek-irsaliye.xlsx
//   dotnet run --project src/Tersane.Rpa -- --tarayici-kur          (Chromium'u indirir)
//
// Seçenekler: --headed (tarayıcıyı göster), --slowmo 250, --trace (Playwright trace.zip), --out rpa-out
// Kullanıcı/şifre: ESKIAMBAR_USER / ESKIAMBAR_PASSWORD ortam değişkenleri (demo değerleri varsayılan).

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var arguments = Args.Parse(args);

if (arguments.Has("tarayici-kur"))
    return Microsoft.Playwright.Program.Main(["install", "--with-deps", "chromium"]);

if (arguments.Get("ornek-olustur") is { } samplePath)
{
    ExcelIo.CreateSample(samplePath);
    Console.WriteLine($"Örnek Excel oluşturuldu: {Path.GetFullPath(samplePath)}");
    return 0;
}

var excel = arguments.Get("excel") ?? Path.Combine("samples", "ornek-irsaliye.xlsx");
if (!File.Exists(excel))
{
    Console.Error.WriteLine($"Excel bulunamadı: {excel}  (örnek için: --ornek-olustur {excel})");
    return 1;
}

var runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var outDir = Path.GetFullPath(Path.Combine(arguments.Get("out") ?? "rpa-out", runId));
Directory.CreateDirectory(outDir);
using var log = new RunLog(Path.Combine(outDir, "rpa.log"));

var options = new BotOptions
{
    BaseUrl = new Uri(arguments.Get("url") ?? Environment.GetEnvironmentVariable("ESKIAMBAR_URL") ?? "http://localhost:5102"),
    User = Environment.GetEnvironmentVariable("ESKIAMBAR_USER") ?? "rpa.bot",
    Password = Environment.GetEnvironmentVariable("ESKIAMBAR_PASSWORD") ?? "Demo-Bot-2026",
    OutputDir = outDir,
    Headless = !arguments.Has("headed"),
    SlowMoMs = int.TryParse(arguments.Get("slowmo"), out var slow) ? slow : 0,
    Trace = arguments.Has("trace"),
};

var started = DateTime.UtcNow;
log.Info($"RPA başladı. Excel: {Path.GetFullPath(excel)} → hedef: {options.BaseUrl}  çıktı: {outDir}");

List<ReceiptRow> rows;
try
{
    rows = ExcelIo.Read(excel);
}
catch (Exception ex) when (ex is InvalidDataException or IOException)
{
    log.Error($"Excel okunamadı: {ex.Message}");
    return 1;
}
log.Info($"{rows.Count} satır okundu ({rows.Count(r => r.InputError is not null)} tanesinde veri hatası var).");

var results = new List<RowResult>();
await using (var bot = new AmbarBot(options, log))
{
    try
    {
        await bot.StartAsync();
    }
    catch (Microsoft.Playwright.PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
    {
        log.Error("Chromium kurulu değil. Önce: dotnet run --project src/Tersane.Rpa -- --tarayici-kur");
        return 1;
    }

    foreach (var row in rows)
        results.Add(await bot.ProcessAsync(row));
}

var resultFile = Path.Combine(outDir, "sonuc.xlsx");
ExcelIo.WriteResults(excel, resultFile, results, started, options.BaseUrl.ToString());

log.Info("──────── ÖZET ────────");
foreach (var group in results.GroupBy(r => r.Status).OrderBy(g => g.Key))
    log.Info($"{ExcelIo.Label(group.Key),-26} {group.Count(),3}");
log.Info($"Sonuç dosyası: {resultFile}");
log.Info($"Ekran görüntüleri: {Path.Combine(outDir, "screenshots")}");

// Exit code 0: the run finished (rejected rows are business results, listed in the file).
// 2: at least one technical failure that needs a look.
return results.Any(r => r.Status == RowStatus.Hatali && r.Message.StartsWith("Teknik hata", StringComparison.Ordinal)) ? 2 : 0;

internal sealed class Args
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] args)
    {
        var result = new Args();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
            result._values[key] = value;
        }
        return result;
    }

    public bool Has(string key) => _values.ContainsKey(key);
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
}
