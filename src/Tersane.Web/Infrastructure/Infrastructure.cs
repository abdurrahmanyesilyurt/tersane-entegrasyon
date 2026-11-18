using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Tersane.Data.Entities;

namespace Tersane.Web.Infrastructure;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>The app has no login in the demo; on an intranet this would come from Windows auth / AD.</summary>
    public string UserName { get; set; } = "demo.planlama";
}

/// <summary>
/// Talks to the mock ERP's demo controls (chaos switch). Exists only so the failure scenarios can be shown
/// from the UI during a demo; a real ERP has no such endpoint.
/// </summary>
public sealed class ErpMockAdminClient(HttpClient http)
{
    public async Task<JsonElement?> GetChaosAsync(CancellationToken ct)
    {
        try { return await http.GetFromJsonAsync<JsonElement>("mock-admin/chaos", ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    public async Task<bool> SetChaosAsync(bool? outage, double? failureRate, int? latencyMs, CancellationToken ct)
    {
        try
        {
            using var response = await http.PutAsJsonAsync("mock-admin/chaos", new { outage, failureRate, latencyMs }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }
}

/// <summary>Turkish labels and formatting used by the views.</summary>
public static class Ui
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static string Local(DateTime? utc, string format = "dd.MM.yyyy HH:mm:ss")
        => utc is null ? "—" : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), Istanbul).ToString(format, Tr);

    public static string Date(DateOnly d) => d.ToString("dd.MM.yyyy", Tr);

    public static string Qty(decimal q) => q.ToString("#,0.###", Tr);

    public static string Ago(DateTime? utc)
    {
        if (utc is null) return "hiç";
        var span = DateTime.UtcNow - utc.Value;
        return span.TotalSeconds < 60 ? $"{Math.Max(0, (int)span.TotalSeconds)} sn önce"
            : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes} dk önce"
            : span.TotalHours < 24 ? $"{(int)span.TotalHours} sa önce"
            : $"{(int)span.TotalDays} gün önce";
    }

    public static (string Label, string Css) Status(MaterialRequestStatus s) => s switch
    {
        MaterialRequestStatus.Draft => ("Taslak", "st-muted"),
        MaterialRequestStatus.Submitted => ("ERP kuyruğunda", "st-info"),
        MaterialRequestStatus.SentToErp => ("ERP'ye iletildi", "st-info"),
        MaterialRequestStatus.ApprovedInErp => ("ERP'de onaylandı", "st-ok"),
        MaterialRequestStatus.Received => ("Ambara girdi", "st-done"),
        MaterialRequestStatus.Rejected => ("ERP'de iptal", "st-warn"),
        MaterialRequestStatus.Failed => ("Entegrasyon hatası", "st-err"),
        _ => (s.ToString(), "st-muted"),
    };

    public static (string Label, string Css) Status(OutboxStatus s) => s switch
    {
        OutboxStatus.Pending => ("Bekliyor", "st-info"),
        OutboxStatus.Processing => ("İşleniyor", "st-warn"),
        OutboxStatus.Processed => ("İşlendi", "st-ok"),
        OutboxStatus.Failed => ("Hatalı", "st-err"),
        _ => (s.ToString(), "st-muted"),
    };

    public static (string Label, string Css) Status(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Open => ("Açık", "st-info"),
        WorkOrderStatus.InProgress => ("Devam ediyor", "st-warn"),
        WorkOrderStatus.Completed => ("Tamamlandı", "st-ok"),
        WorkOrderStatus.Cancelled => ("İptal", "st-muted"),
        _ => (s.ToString(), "st-muted"),
    };

    public static (string Label, string Css) Status(ProjectStatus s) => s switch
    {
        ProjectStatus.Planning => ("Planlama", "st-muted"),
        ProjectStatus.InProduction => ("İmalatta", "st-info"),
        ProjectStatus.Delivered => ("Teslim edildi", "st-done"),
        _ => (s.ToString(), "st-muted"),
    };

    public static (string Label, string Css) Status(BlockStatus s) => s switch
    {
        BlockStatus.Planned => ("Planlandı", "st-muted"),
        BlockStatus.Fabrication => ("İmalat", "st-warn"),
        BlockStatus.Assembly => ("Montaj", "st-info"),
        BlockStatus.Erected => ("Kızakta", "st-ok"),
        _ => (s.ToString(), "st-muted"),
    };

    public static (string Label, string Css) StockLevel(string level) => level switch
    {
        "OUT" => ("Stok yok", "st-err"),
        "LOW" => ("Kritik", "st-warn"),
        _ => ("Yeterli", "st-ok"),
    };

    public static (string Label, string Css) LogLevel(string level) => level switch
    {
        "Error" => ("Hata", "st-err"),
        "Warning" => ("Uyarı", "st-warn"),
        _ => ("Bilgi", "st-muted"),
    };

    public static string Direction(string d) => d switch
    {
        "OUT" => "İç → ERP",
        "IN" => "ERP → İç",
        _ => "Sistem",
    };

    public static readonly string[] Departments =
        ["Çelik İmalat", "Boru Atölyesi", "Makine Montaj", "Elektrik", "Boya", "Donatım", "Planlama", "Kalite Kontrol"];
}
