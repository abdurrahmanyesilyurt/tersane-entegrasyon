using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tersane.Data;
using Tersane.Data.Entities;

namespace Tersane.Worker.Erp;

/// <summary>
/// OAuth2 client-credentials token for the ERP, cached (singleton) until shortly before it expires.
/// </summary>
public sealed class ErpTokenProvider(IOptions<ErpOptions> options, IHttpClientFactory httpClientFactory, TimeProvider clock)
{
    public const string TokenClientName = "erp-token";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _token is not null && clock.GetUtcNow() < _expiresAt)
            return _token;

        await _gate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _token is not null && clock.GetUtcNow() < _expiresAt)
                return _token;

            var o = options.Value;
            using var client = httpClientFactory.CreateClient(TokenClientName);
            using var response = await client.PostAsync(new Uri(o.BaseUrl, "mock-erp/auth/token"), new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = o.ClientId,
                ["client_secret"] = o.ClientSecret,
            }), ct);

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw new ErpException(ErpFailureKind.Permanent, "ERP kimlik doğrulaması başarısız (client id/secret).", response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"ERP token servisi {(int)response.StatusCode} döndü.", null, response.StatusCode);

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new HttpRequestException("ERP token yanıtı boş.");
            _token = token.AccessToken;
            _expiresAt = clock.GetUtcNow().AddSeconds(Math.Max(10, token.ExpiresIn - 30));
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Adds the bearer token; on 401 (token revoked/expired early) refreshes it and repeats the call once.</summary>
public sealed class ErpTokenHandler(ErpTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(forceRefresh: false, ct));
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(forceRefresh: true, ct));
        return await base.SendAsync(request, ct);
    }
}

/// <summary>
/// Writes every ERP HTTP attempt (including each Polly retry) to integration.IntegrationLogs with
/// status, duration and truncated bodies. Reads are only logged when they fail, to keep the log useful.
/// </summary>
public sealed class ErpAuditHandler(IDbContextFactory<TersaneDbContext> dbFactory, ILogger<ErpAuditHandler> logger) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<ErpCallContext> ContextKey = new("erp-call-context");
    private static readonly HttpRequestOptionsKey<int> AttemptKey = new("erp-call-attempt");
    private const int MaxBody = 4000;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Options.TryGetValue(ContextKey, out var context);
        request.Options.TryGetValue(AttemptKey, out var attempt);
        request.Options.Set(AttemptKey, ++attempt);

        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, ct);
            sw.Stop();
            var failed = !response.IsSuccessStatusCode;
            if (failed || request.Method != HttpMethod.Get || context?.AlwaysLog == true)
            {
                await response.Content.LoadIntoBufferAsync(ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                await WriteAsync(request, context, attempt, failed ? "Error" : "Info", (int)response.StatusCode, sw.ElapsedMilliseconds,
                    failed ? $"ERP {(int)response.StatusCode} {response.ReasonPhrase}" : "ERP çağrısı başarılı", requestBody, responseBody);
            }
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            sw.Stop();
            await WriteAsync(request, context, attempt, "Error", null, sw.ElapsedMilliseconds, $"Bağlantı hatası: {ex.Message}", requestBody, null);
            throw;
        }
    }

    private async Task WriteAsync(HttpRequestMessage request, ErpCallContext? context, int attempt, string level, int? status, long ms,
        string message, string? requestBody, string? responseBody)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            db.IntegrationLogs.Add(new IntegrationLog
            {
                OccurredAt = DateTime.UtcNow,
                Direction = request.Method == HttpMethod.Get ? "IN" : "OUT",
                Operation = context?.Operation ?? "Erp.Call",
                Level = level,
                MessageId = context?.MessageId,
                CorrelationId = context?.CorrelationId,
                HttpMethod = request.Method.Method,
                Url = Truncate(request.RequestUri?.PathAndQuery, 500),
                HttpStatus = status,
                DurationMs = (int)ms,
                Attempt = attempt,
                Message = Truncate(message, 2000)!,
                RequestBody = Truncate(requestBody, MaxBody),
                ResponseBody = Truncate(responseBody, MaxBody),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Logging must never break the integration itself.
            logger.LogWarning(ex, "Entegrasyon logu yazılamadı.");
        }
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max] + "…";
}
