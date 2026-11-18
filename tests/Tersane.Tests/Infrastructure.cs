using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tersane.Data;
using Tersane.Data.Entities;
using Tersane.Worker;
using Tersane.Worker.Outbox;
using Tersane.Worker.Sync;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Tersane.Tests.SqlServer))]

namespace Tersane.Tests;

/// <summary>
/// One real SQL Server 2022 container for the whole test run (Testcontainers).
/// Every test class gets its own freshly migrated database, so classes can run in parallel.
/// </summary>
public sealed class SqlServer : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>Creates and migrates a new database (procedures, views, TVF and TVP type included) with demo data.</summary>
    public async Task<string> CreateDatabaseAsync(string name, bool seed = true)
    {
        var cs = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"{name}_{Guid.NewGuid():N}"[..Math.Min(name.Length + 9, 100)],
        }.ConnectionString;

        await using var db = CreateContext(cs);
        if (seed)
            await DemoSeeder.MigrateAndSeedAsync(db, NullLogger.Instance);
        else
            await db.Database.MigrateAsync();
        return cs;
    }

    public static TersaneDbContext CreateContext(string connectionString)
        => new(new DbContextOptionsBuilder<TersaneDbContext>().UseSqlServer(connectionString).Options);
}

/// <summary>The mock ERP hosted in memory (TestServer), with the background simulation switched off.</summary>
public sealed class ErpMockApp : WebApplicationFactory<ErpMockEntryPoint>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Simulation:Enabled", "false");
    }

    public async Task SetChaosAsync(bool? outage = null, int? failNext = null)
    {
        using var client = CreateClient();
        var response = await client.PutAsJsonAsync("/mock-admin/chaos", new { outage, failNextRequests = failNext });
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> AdminPostAsync(string path)
    {
        using var client = CreateClient();
        return await client.PostAsync(path, null);
    }

    public async Task<string> TokenAsync()
    {
        using var client = CreateClient();
        var response = await client.PostAsync("/mock-erp/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "tersane-entegrasyon", ["client_secret"] = "mock-erp-demo-secret",
        }));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }

    public async Task<HttpClient> AuthorizedClientAsync()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await TokenAsync());
        return client;
    }
}

/// <summary>
/// The integration worker's real DI graph (Polly pipeline, token + audit handlers, EF Core, stored procedures)
/// wired to a fresh database and an in-memory mock ERP. Tests call the processors directly instead of
/// waiting for the BackgroundService timers.
/// </summary>
public sealed class IntegrationHarness : IAsyncDisposable
{
    private IntegrationHarness(ErpMockApp erp, ServiceProvider services, string connectionString)
        => (Erp, Services, ConnectionString) = (erp, services, connectionString);

    public ErpMockApp Erp { get; }
    public ServiceProvider Services { get; }
    public string ConnectionString { get; }
    public OutboxProcessor Outbox => Services.GetRequiredService<OutboxProcessor>();
    public ErpSyncRunner Sync => Services.GetRequiredService<ErpSyncRunner>();

    public TersaneDbContext Db() => SqlServer.CreateContext(ConnectionString);

    public static async Task<IntegrationHarness> CreateAsync(SqlServer sql, string name)
    {
        var cs = await sql.CreateDatabaseAsync(name);
        var erp = new ErpMockApp();
        _ = erp.Server; // start it

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Tersane"] = cs,
            ["Erp:BaseUrl"] = "http://localhost/",
            ["Erp:ClientSecret"] = "mock-erp-demo-secret",
            ["Erp:RetryCount"] = "3",
            ["Erp:RetryBaseDelayMs"] = "10",
            ["Erp:HonourRetryAfter"] = "false",
            ["Erp:AttemptTimeoutSeconds"] = "5",
            ["Erp:TotalTimeoutSeconds"] = "20",
            ["Erp:CircuitBreakerMinimumThroughput"] = "1000",
            ["Integration:OutboxBackoffBaseSeconds"] = "0",
            ["Integration:SyncOverlapSeconds"] = "0",
        }).Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(config)
            .AddTersaneIntegration(config, http => http.ConfigurePrimaryHttpMessageHandler(() => erp.Server.CreateHandler()))
            .BuildServiceProvider();

        var harness = new IntegrationHarness(erp, services, cs);
        await harness.Sync.RunOnceAsync(CancellationToken.None); // materials + stock from the ERP
        return harness;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await Erp.DisposeAsync();
    }
}

/// <summary>Small helpers to arrange test data.</summary>
public static class TestData
{
    public static async Task<MaterialRequest> CreateDraftAsync(TersaneDbContext db, params (string PartNo, decimal Qty)[] lines)
    {
        var project = await db.Projects.OrderBy(p => p.Id).FirstAsync();
        var materials = await db.Materials.ToDictionaryAsync(m => m.PartNo);
        var request = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedBy = "test.user",
            NeedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            Status = MaterialRequestStatus.Draft,
            Lines = lines.Select((l, i) => new MaterialRequestLine
            {
                LineNo = i + 1, MaterialId = materials[l.PartNo].Id, Quantity = l.Qty, Unit = materials[l.PartNo].Unit,
            }).ToList(),
        };
        db.MaterialRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    /// <summary>Materials for database-only tests (no ERP involved), inserted through the OPENJSON procedure.</summary>
    public static async Task SeedMaterialsAsync(TersaneDbContext db)
    {
        const string json = """
            [
              {"PartNo":"ST-AH36-08","Description":"Sac 8 mm","UnitMeas":"AD","PartCategory":"Sac","SafetyStock":40,"PartStatus":"A","ChangedAt":"2026-11-01T08:00:00Z"},
              {"PartNo":"PR-HP160-9","Description":"HP profil","UnitMeas":"M","PartCategory":"Profil","SafetyStock":300,"PartStatus":"A","ChangedAt":"2026-11-01T08:00:00Z"},
              {"PartNo":"WL-E6013-32","Description":"Eski elektrot","UnitMeas":"KG","PartCategory":"Kaynak sarf","SafetyStock":0,"PartStatus":"O","ChangedAt":"2026-11-01T08:00:00Z"}
            ]
            """;
        await new TersaneSql(db).UpsertMaterialsFromErpJsonAsync(json);
    }
}
