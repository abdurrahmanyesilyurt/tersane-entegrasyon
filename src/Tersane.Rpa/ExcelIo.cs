using System.Globalization;
using ClosedXML.Excel;

namespace Tersane.Rpa;

/// <summary>One waybill line to be entered into the legacy warehouse form.</summary>
public sealed record ReceiptRow(
    int RowNumber,
    DateOnly? FisTarihi,
    string Depo,
    string Raf,
    string MalzemeKodu,
    decimal? Miktar,
    string Tedarikci,
    string IrsaliyeNo,
    bool KaliteKontrol,
    string? Aciklama,
    string? InputError);

public enum RowStatus { Basarili, ZatenKayitli, Hatali, Atlandi }

public sealed record RowResult(int RowNumber, RowStatus Status, string? FisNo, string Message, double Seconds, string? Screenshot, int Attempts);

/// <summary>Reads the supplier waybill sheet and writes the result workbook (ClosedXML, no Excel install needed).</summary>
public static class ExcelIo
{
    private static readonly CultureInfo Tr = new("tr-TR");

    public static readonly string[] Headers =
        ["Fiş Tarihi", "Depo", "Raf", "Malzeme Kodu", "Miktar", "Tedarikçi", "İrsaliye No", "Kalite Kontrol", "Açıklama"];

    public static List<ReceiptRow> Read(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "Irsaliye") ?? workbook.Worksheet(1);

        // Map columns by header text, so column order in the supplier's file does not matter.
        var headerRow = sheet.Row(1);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in headerRow.CellsUsed())
            columns[Normalize(cell.GetString())] = cell.Address.ColumnNumber;
        var missing = Headers.Where(h => !columns.ContainsKey(Normalize(h))).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"Excel'de eksik sütun(lar): {string.Join(", ", missing)}");

        var rows = new List<ReceiptRow>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            IXLCell Cell(string header) => row.Cell(columns[Normalize(header)]);
            if (Headers.All(h => Cell(h).IsEmpty())) continue;

            var errors = new List<string>();
            var date = ReadDate(Cell("Fiş Tarihi"), errors);
            var qty = ReadDecimal(Cell("Miktar"), errors);
            string Text(string header, bool required)
            {
                var value = Cell(header).GetFormattedString().Trim();
                if (required && value.Length == 0) errors.Add($"{header} boş");
                return value;
            }

            rows.Add(new ReceiptRow(
                RowNumber: row.RowNumber(),
                FisTarihi: date,
                Depo: Text("Depo", true).ToUpperInvariant(),
                Raf: Text("Raf", true).ToUpperInvariant(),
                MalzemeKodu: Text("Malzeme Kodu", true).ToUpperInvariant(),
                Miktar: qty,
                Tedarikci: Text("Tedarikçi", true),
                IrsaliyeNo: Text("İrsaliye No", true).ToUpperInvariant(),
                KaliteKontrol: Text("Kalite Kontrol", false).ToUpperInvariant() is "E" or "EVET" or "X" or "1" or "TRUE",
                Aciklama: NullIfEmpty(Text("Açıklama", false)),
                InputError: errors.Count == 0 ? null : string.Join("; ", errors)));
        }
        return rows;
    }

    /// <summary>Copies the input workbook and appends result columns plus a summary sheet.</summary>
    public static void WriteResults(string inputPath, string outputPath, IReadOnlyList<RowResult> results, DateTime startedUtc, string target)
    {
        using var workbook = new XLWorkbook(inputPath);
        var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "Irsaliye") ?? workbook.Worksheet(1);
        var first = sheet.Row(1).LastCellUsed().Address.ColumnNumber + 1;
        string[] resultHeaders = ["RPA Durum", "Fiş No", "Mesaj", "Süre (sn)", "Deneme", "Ekran Görüntüsü"];
        for (var i = 0; i < resultHeaders.Length; i++)
        {
            var header = sheet.Cell(1, first + i);
            header.Value = resultHeaders[i];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#102A44");
            header.Style.Font.FontColor = XLColor.White;
        }

        foreach (var r in results)
        {
            var color = r.Status switch
            {
                RowStatus.Basarili => XLColor.FromHtml("#E3F4EA"),
                RowStatus.ZatenKayitli => XLColor.FromHtml("#E5EFFA"),
                RowStatus.Atlandi => XLColor.FromHtml("#FFF4D6"),
                _ => XLColor.FromHtml("#FDE8E6"),
            };
            sheet.Cell(r.RowNumber, first).Value = Label(r.Status);
            sheet.Cell(r.RowNumber, first + 1).Value = r.FisNo ?? "";
            sheet.Cell(r.RowNumber, first + 2).Value = r.Message;
            sheet.Cell(r.RowNumber, first + 3).Value = Math.Round(r.Seconds, 1);
            sheet.Cell(r.RowNumber, first + 4).Value = r.Attempts;
            sheet.Cell(r.RowNumber, first + 5).Value = r.Screenshot ?? "";
            sheet.Range(r.RowNumber, first, r.RowNumber, first + resultHeaders.Length - 1).Style.Fill.BackgroundColor = color;
        }
        sheet.Columns().AdjustToContents(8, 60);

        var summary = workbook.Worksheets.Contains("RPA Özet") ? workbook.Worksheet("RPA Özet") : workbook.AddWorksheet("RPA Özet");
        summary.Clear();
        var lines = new (string, object)[]
        {
            ("Hedef sistem", target),
            ("Başlangıç (UTC)", startedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("Süre (sn)", Math.Round((DateTime.UtcNow - startedUtc).TotalSeconds, 1)),
            ("Toplam satır", results.Count),
            ("Başarılı", results.Count(r => r.Status == RowStatus.Basarili)),
            ("Zaten kayıtlı (atlandı)", results.Count(r => r.Status == RowStatus.ZatenKayitli)),
            ("Excel verisi hatalı (girilmedi)", results.Count(r => r.Status == RowStatus.Atlandi)),
            ("Sistem reddetti / hata", results.Count(r => r.Status == RowStatus.Hatali)),
        };
        for (var i = 0; i < lines.Length; i++)
        {
            summary.Cell(i + 1, 1).Value = lines[i].Item1;
            summary.Cell(i + 1, 1).Style.Font.Bold = true;
            summary.Cell(i + 1, 2).Value = XLCellValue.FromObject(lines[i].Item2);
        }
        summary.Columns().AdjustToContents();
        workbook.SaveAs(outputPath);
    }

    /// <summary>Creates the demo input file: mostly valid lines plus a few deliberately broken ones.</summary>
    public static void CreateSample(string path)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Irsaliye");
        for (var i = 0; i < Headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = Headers[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
            sheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8710A");
            sheet.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }

        var today = DateTime.Today;
        object[][] data =
        [
            [today.AddDays(-2), "SAC", "SAC-SAHA", "ST-AH36-08", 24, "Örnek Çelik San. (kurgusal)", "IRS-2026-1001", "E", "Panel hattı sevkiyatı"],
            [today.AddDays(-2), "SAC", "SAC-SAHA", "ST-AH36-12", 10, "Örnek Çelik San. (kurgusal)", "IRS-2026-1001", "E", ""],
            [today.AddDays(-1), "ANA", "A-01", "WL-E71T1-12", 450.5, "Demo Kaynak Malz. (kurgusal)", "IRS-2026-1002", "H", "Ondalıklı miktar"],
            [today.AddDays(-1), "BORU", "BORU-01", "PP-ST37-DN50", 120, "Kurgu Boru Ltd.", "IRS-2026-1003", "E", ""],
            [today.AddDays(-1), "ANA", "A-02", "FL-DN50-PN16", 48, "Kurgu Boru Ltd.", "IRS-2026-1003", "E", ""],
            [today, "BOYA", "BOYA-01", "PT-EPX-PRM20", 30, "Demo Boya A.Ş.", "IRS-2026-1004", "H", ""],
            [today, "ANA", "A-01", "XX-BILINMEYEN-1", 5, "Demo Boya A.Ş.", "IRS-2026-1004", "H", "Eski sistemde olmayan kod → form reddeder"],
            [today, "ANA", "A-03", "EL-CBL-3X25", "", "Demo Elektrik (kurgusal)", "IRS-2026-1005", "E", "Miktar boş → bot girmez"],
            [today, "SAC", "SAC-SAHA", "ST-AH36-08", 24, "Örnek Çelik San. (kurgusal)", "IRS-2026-1001", "E", "1. satırın tekrarı → mükerrer"],
            [today, "GAZ", "GAZ-01", "GS-ARCO2-50", 12, "Demo Gaz (kurgusal)", "IRS-2026-1006", "E", ""],
        ];
        for (var r = 0; r < data.Length; r++)
            for (var c = 0; c < data[r].Length; c++)
                sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(data[r][c]);

        sheet.Column(1).Style.DateFormat.Format = "dd.mm.yyyy";
        sheet.Column(5).Style.NumberFormat.Format = "#,##0.###";
        sheet.Columns().AdjustToContents();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        workbook.SaveAs(path);
    }

    public static string Label(RowStatus s) => s switch
    {
        RowStatus.Basarili => "Başarılı",
        RowStatus.ZatenKayitli => "Zaten kayıtlı",
        RowStatus.Atlandi => "Girilmedi (Excel hatası)",
        _ => "Hata",
    };

    private static DateOnly? ReadDate(IXLCell cell, List<string> errors)
    {
        if (cell.IsEmpty()) { errors.Add("Fiş Tarihi boş"); return null; }
        if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
        var text = cell.GetString().Trim();
        if (DateOnly.TryParseExact(text, ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"], Tr, DateTimeStyles.None, out var d)) return d;
        errors.Add($"Fiş Tarihi okunamadı: '{text}'");
        return null;
    }

    private static decimal? ReadDecimal(IXLCell cell, List<string> errors)
    {
        if (cell.IsEmpty()) { errors.Add("Miktar boş"); return null; }
        if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
        var text = cell.GetString().Trim();
        if (decimal.TryParse(text, NumberStyles.Number, Tr, out var value)) return value;
        errors.Add($"Miktar sayı değil: '{text}'");
        return null;
    }

    private static string Normalize(string header)
        => header.Trim().ToLower(Tr).Replace("ş", "s").Replace("ç", "c").Replace("ı", "i").Replace("ü", "u").Replace("ö", "o").Replace("ğ", "g").Replace(" ", "");

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
