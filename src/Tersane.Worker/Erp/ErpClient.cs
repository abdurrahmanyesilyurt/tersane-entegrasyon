using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Tersane.Worker.Erp;

/// <summary>
/// Typed HTTP client for the mock ERP's OData projections. Retries, timeouts and the circuit breaker are
/// NOT in here: they are configured once on the HttpClient pipeline (Polly, see ServiceCollectionExtensions).
/// This class only turns HTTP outcomes into <see cref="ErpException"/> kinds the worker can act on.
/// </summary>
public sealed class ErpClient(HttpClient http)
{
    public const string Requisitions = "mock-erp/projection/v1/PurchaseRequisitionHandling.svc/PurchaseRequisitionSet";
    public const string Stock = "mock-erp/projection/v1/InventoryPartInStockHandling.svc/InventoryPartInStockSet";
    public const string Parts = "mock-erp/projection/v1/PartHandling.svc/InventoryPartSet";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null, // ERP fields are PascalCase
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ErpRequisition?> FindRequisitionByExternalRefAsync(string externalRef, ErpCallContext context, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString($"ExternalRef eq '{externalRef.Replace("'", "''", StringComparison.Ordinal)}'");
        var page = await GetPageAsync<ErpRequisition>($"{Requisitions}?$filter={filter}&$select=RequisitionNo,ExternalRef,Objstate,ChangedAt", context, ct);
        return page.Value.SingleOrDefault();
    }

    public async Task<ErpRequisition> CreateRequisitionAsync(ErpRequisitionCreate body, ErpCallContext context, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Requisitions) { Content = JsonContent.Create(body, options: Json) };
        request.Options.Set(ErpAuditHandler.ContextKey, context);
        using var response = await SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ErpRequisition>(Json, ct))!;
    }

    /// <summary>Reads every page of a delta query (follows @odata.nextLink).</summary>
    public async Task<List<T>> GetChangedSinceAsync<T>(string entitySet, DateTime sinceUtc, string? select, ErpCallContext context, CancellationToken ct)
    {
        var since = sinceUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var url = $"{entitySet}?$filter={Uri.EscapeDataString($"ChangedAt gt {since}")}&$orderby=ChangedAt";
        if (select is not null) url += $"&$select={select}";

        var all = new List<T>();
        string? next = url;
        var guard = 0;
        while (next is not null)
        {
            if (++guard > 1000) throw new InvalidOperationException("Sayfalama döngüsü sonlanmadı.");
            var page = await GetPageAsync<T>(next, context, ct);
            all.AddRange(page.Value);
            next = page.NextLink;
        }
        return all;
    }

    /// <summary>Raw JSON array of a delta query, handed straight to an OPENJSON procedure.</summary>
    public async Task<(string Json, DateTimeOffset? MaxChangedAt, int Count)> GetChangedSinceAsJsonAsync(string entitySet, DateTime sinceUtc, string? select, ErpCallContext context, CancellationToken ct)
    {
        var rows = await GetChangedSinceAsync<JsonElement>(entitySet, sinceUtc, select, context, ct);
        DateTimeOffset? max = rows.Count == 0 ? null : rows.Max(r => r.GetProperty("ChangedAt").GetDateTimeOffset());
        return (JsonSerializer.Serialize(rows), max, rows.Count);
    }

    private async Task<ODataPage<T>> GetPageAsync<T>(string url, ErpCallContext context, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(ErpAuditHandler.ContextKey, context);
        using var response = await SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        var value = doc.GetProperty("value").Deserialize<List<T>>(Json) ?? [];
        var next = doc.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        return new ODataPage<T>(value, next);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new ErpException(ErpFailureKind.Transient, "ERP devre kesici açık: çağrı yapılmadı, bir süre sonra tekrar denenecek.", inner: ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new ErpException(ErpFailureKind.Transient, "ERP zaman aşımı.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ErpException(ErpFailureKind.Transient, $"ERP'ye ulaşılamadı: {ex.Message}", ex.StatusCode, inner: ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var (code, message) = await ReadErrorAsync(response, ct);
        var status = response.StatusCode;
        var kind = status switch
        {
            HttpStatusCode.Conflict => ErpFailureKind.Conflict,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => ErpFailureKind.Transient,
            >= HttpStatusCode.InternalServerError => ErpFailureKind.Transient,
            _ => ErpFailureKind.Permanent,
        };
        throw new ErpException(kind, $"ERP {(int)status}: {message}", status, code);
    }

    private static async Task<(string? Code, string Message)> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var error = doc.RootElement.GetProperty("error");
            return (error.GetProperty("code").GetString(), error.GetProperty("message").GetString() ?? text);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, string.IsNullOrWhiteSpace(text) ? response.ReasonPhrase ?? "Bilinmeyen hata" : text);
        }
    }
}
