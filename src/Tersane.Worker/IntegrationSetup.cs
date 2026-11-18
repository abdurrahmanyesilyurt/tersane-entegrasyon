using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Tersane.Data;
using Tersane.Worker.Erp;
using Tersane.Worker.Outbox;
using Tersane.Worker.Sync;

namespace Tersane.Worker;

public static class IntegrationSetup
{
    /// <summary>
    /// Registers the ERP client pipeline and the integration services (no hosted services).
    /// <paramref name="configureHttp"/> lets tests swap the primary handler for an in-memory ERP.
    /// </summary>
    public static IServiceCollection AddTersaneIntegration(this IServiceCollection services, IConfiguration configuration,
        Action<IHttpClientBuilder>? configureHttp = null)
    {
        services.TryAddSingletonTimeProvider();
        services.Configure<ErpOptions>(configuration.GetSection(ErpOptions.Section));
        services.Configure<IntegrationOptions>(configuration.GetSection(IntegrationOptions.Section));

        // No EnableRetryOnFailure here: the worker uses explicit transactions, and a crashed or failed
        // round is recovered by the outbox lease instead.
        services.AddDbContextFactory<TersaneDbContext>(o => o.UseSqlServer(configuration.GetConnectionString("Tersane")));

        var tokenClient = services.AddHttpClient(ErpTokenProvider.TokenClientName);
        configureHttp?.Invoke(tokenClient);

        services.AddSingleton<ErpTokenProvider>();
        services.AddTransient<ErpTokenHandler>();
        services.AddTransient<ErpAuditHandler>();

        var erp = services.AddHttpClient<ErpClient>((sp, client) =>
        {
            client.BaseAddress = sp.GetRequiredService<IOptions<ErpOptions>>().Value.BaseUrl;
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.Timeout = Timeout.InfiniteTimeSpan; // Polly owns the timeouts
        });

        // Handler order, outermost → innermost:
        // Polly (retries the whole chain) → audit log (one row per attempt) → bearer token → network.
        erp.AddResilienceHandler("erp", (builder, context) =>
            {
                var o = context.ServiceProvider.GetRequiredService<IOptions<ErpOptions>>().Value;
                builder
                    .AddTimeout(TimeSpan.FromSeconds(o.TotalTimeoutSeconds))
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = o.RetryCount,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromMilliseconds(o.RetryBaseDelayMs),
                        // Default predicate: HttpRequestException, timeouts, 5xx, 408, 429.
                        ShouldRetryAfterHeader = o.HonourRetryAfter,
                    })
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        MinimumThroughput = o.CircuitBreakerMinimumThroughput,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        BreakDuration = TimeSpan.FromSeconds(o.CircuitBreakerBreakSeconds),
                    })
                    .AddTimeout(TimeSpan.FromSeconds(o.AttemptTimeoutSeconds));
            });
        erp.AddHttpMessageHandler<ErpAuditHandler>()
           .AddHttpMessageHandler<ErpTokenHandler>();
        configureHttp?.Invoke(erp);

        services.AddTransient<OutboxProcessor>();
        services.AddTransient<ErpSyncRunner>();
        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
    }
}
