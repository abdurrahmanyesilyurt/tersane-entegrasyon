using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Tersane.EskiAmbar.Pages;

public sealed partial class AmbarGirisModel(IFisRepository repository, IConfiguration config) : PageModel
{
    private static readonly CultureInfo Tr = new("tr-TR");

    [BindProperty(Name = "ctl00$cph$txtFisTarihi")] public string? FisTarihi { get; set; }
    [BindProperty(Name = "ctl00$cph$ddlDepo")] public string? Depo { get; set; }
    [BindProperty(Name = "ctl00$cph$ddlRaf")] public string? Raf { get; set; }
    [BindProperty(Name = "ctl00$cph$txtTedarikci")] public string? Tedarikci { get; set; }
    [BindProperty(Name = "ctl00$cph$txtIrsaliyeNo")] public string? IrsaliyeNo { get; set; }
    [BindProperty(Name = "ctl00$cph$txtMalzemeKodu")] public string? MalzemeKodu { get; set; }
    [BindProperty(Name = "ctl00$cph$txtMiktar")] public string? Miktar { get; set; }
    [BindProperty(Name = "ctl00$cph$chkKaliteKontrol")] public bool KaliteKontrol { get; set; }
    [BindProperty(Name = "ctl00$cph$txtAciklama")] public string? Aciklama { get; set; }

    public List<string> Hatalar { get; } = [];
    public string? KayitFisNo { get; private set; }

    public void OnGet(string? kayit)
    {
        KayitFisNo = kayit;
        FisTarihi = DateTime.Today.ToString("dd.MM.yyyy", Tr);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // The old server is slow.
        await Task.Delay(config.GetValue("Legacy:SaveDelayMs", 800), ct);

        if (!DateOnly.TryParseExact(FisTarihi?.Trim(), "dd.MM.yyyy", Tr, DateTimeStyles.None, out var tarih))
            Hatalar.Add("Fiş tarihi gg.aa.yyyy biçiminde olmalıdır.");
        else if (tarih > DateOnly.FromDateTime(DateTime.Today))
            Hatalar.Add("Fiş tarihi ileri bir tarih olamaz.");

        if (string.IsNullOrEmpty(Depo) || !LegacyCatalog.Depots.TryGetValue(Depo, out var depot))
            Hatalar.Add("Depo seçiniz.");
        else if (string.IsNullOrEmpty(Raf) || !depot.Racks.Contains(Raf))
            Hatalar.Add("Raf / lokasyon seçilen depoya ait değil.");

        var kod = MalzemeKodu?.Trim().ToUpperInvariant() ?? ""; // Invariant: "i".ToUpper() is "İ" under tr-TR
        if (!LegacyCatalog.Materials.TryGetValue(kod, out var malzeme))
            Hatalar.Add($"Malzeme kodu bulunamadı: {kod}");

        if (!decimal.TryParse(Miktar?.Trim(), NumberStyles.Number, Tr, out var miktar) || miktar <= 0 || miktar > 100_000)
            Hatalar.Add("Miktar 0'dan büyük bir sayı olmalıdır (ondalık ayırıcı virgül).");

        if (string.IsNullOrWhiteSpace(Tedarikci))
            Hatalar.Add("Tedarikçi zorunludur.");

        var irsaliye = IrsaliyeNo?.Trim().ToUpperInvariant() ?? "";
        if (!IrsaliyePattern().IsMatch(irsaliye))
            Hatalar.Add("İrsaliye no 3-30 karakter olmalı (harf, rakam, tire).");

        if (Hatalar.Count == 0 && await repository.FindDuplicateAsync(irsaliye, kod, ct) is { } mevcut)
            Hatalar.Add($"Bu irsaliye satırı daha önce girilmiş (Fiş No: {mevcut}).");

        if (Hatalar.Count > 0)
            return Page();

        var fisNo = await repository.InsertAsync(new AmbarGirisFisi(
            "", tarih, Depo!, Raf!, kod, miktar, malzeme.Unit, Tedarikci!.Trim(), irsaliye, KaliteKontrol,
            string.IsNullOrWhiteSpace(Aciklama) ? null : Aciklama.Trim(), User.Identity?.Name ?? "?", DateTime.UtcNow), ct);

        // Post/Redirect/Get: a browser refresh never saves the same receipt twice.
        return RedirectToPage(null, new { kayit = fisNo });
    }

    [GeneratedRegex("^[A-Z0-9-]{3,30}$")]
    private static partial Regex IrsaliyePattern();
}
