using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Tersane.ErpMock;

/// <summary>Issues opaque bearer tokens for the OAuth2 client-credentials flow (mock).</summary>
public sealed class TokenStore(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();

    public (string Token, int ExpiresIn) Issue(int lifetimeSeconds)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tokens[token] = clock.GetUtcNow().AddSeconds(lifetimeSeconds);
        return (token, lifetimeSeconds);
    }

    public bool IsValid(string token) => _tokens.TryGetValue(token, out var expires) && expires > clock.GetUtcNow();

    public void RevokeAll() => _tokens.Clear();
}

/// <summary>
/// Sits in front of every projection call: checks the bearer token, then applies the chaos settings
/// (outage, random 503s, latency) so the integration worker's resilience can be demonstrated.
/// </summary>
public sealed class ErpGatewayMiddleware(RequestDelegate next, ChaosSettings chaos, TokenStore tokens, ILogger<ErpGatewayMiddleware> logger)
{
    private readonly Random _random = new();

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/" + ErpProjections.Root))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !tokens.IsValid(header["Bearer ".Length..]))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            await Write(context, StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Geçerli bir erişim jetonu gerekli.");
            return;
        }

        if (chaos.LatencyMs > 0)
            await Task.Delay(chaos.LatencyMs, context.RequestAborted);

        if (chaos.ShouldFail(_random))
        {
            logger.LogWarning("Kaos: {Method} {Path} → 503", context.Request.Method, context.Request.Path);
            context.Response.Headers.RetryAfter = "2";
            await Write(context, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "ERP geçici olarak hizmet veremiyor (simülasyon).");
            return;
        }

        await next(context);
    }

    private static Task Write(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = new { code, message } });
    }
}

/// <summary>Moves requisitions forward and consumes stock now and then, so the demo is alive.</summary>
public sealed class ErpSimulation(ErpStore store, IOptions<SimulationOptions> options, ILogger<ErpSimulation> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled)
            return;

        var random = new Random();
        var lastConsumption = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var no in store.DueForAdvance("Released", TimeSpan.FromSeconds(o.AutoAuthorizeSeconds)))
                logger.LogInformation("Simülasyon: {No} onaylandı (Authorized).", store.Advance(no)?.RequisitionNo);

            foreach (var no in store.DueForAdvance("Authorized", TimeSpan.FromSeconds(o.AutoCloseSeconds)))
                logger.LogInformation("Simülasyon: {No} teslim alındı (Closed), stok arttı.", store.Advance(no)?.RequisitionNo);

            if (o.ConsumptionEnabled && DateTimeOffset.UtcNow - lastConsumption > TimeSpan.FromSeconds(o.ConsumptionIntervalSeconds))
            {
                lastConsumption = DateTimeOffset.UtcNow;
                if (store.ConsumeRandom(random) is { } row)
                    logger.LogInformation("Simülasyon: üretim tüketimi {Part} @ {Loc} → {Qty}", row.PartNo, row.LocationNo, row.QtyOnhand);
            }
        }
    }
}
