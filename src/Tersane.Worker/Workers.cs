using Microsoft.Extensions.Options;
using Tersane.Worker.Erp;
using Tersane.Worker.Outbox;
using Tersane.Worker.Sync;

namespace Tersane.Worker;

/// <summary>Polls the outbox; drains it quickly while there is work, then waits.</summary>
public sealed class OutboxWorker(OutboxProcessor processor, IOptions<IntegrationOptions> options, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox worker başladı ({WorkerId}).", processor.WorkerId);
        var idle = TimeSpan.FromSeconds(options.Value.OutboxPollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessBatchAsync(stoppingToken);
                if (result.Claimed > 0)
                    logger.LogInformation("Outbox: {Claimed} alındı, {Ok} başarılı, {Retry} tekrar bekliyor, {Failed} hatalı.",
                        result.Claimed, result.Succeeded, result.Retrying, result.Failed);
                if (result.Claimed == options.Value.OutboxBatchSize)
                    continue; // more work waiting
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // e.g. SQL Server restarting. Claimed messages come back after their lease expires.
                logger.LogError(ex, "Outbox turu başarısız.");
            }
            await Task.Delay(idle, stoppingToken);
        }
    }
}

/// <summary>Runs the ERP → internal delta sync on a fixed interval.</summary>
public sealed class ErpSyncWorker(ErpSyncRunner runner, IOptions<IntegrationOptions> options, ILogger<ErpSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SyncIntervalSeconds));
        do
        {
            try
            {
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "ERP senkron turu başarısız.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
