using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tersane.Tests;

/// <summary>The mock ERP's OData surface (what the worker relies on).</summary>
public sealed class ErpMockTests : IAsyncLifetime
{
    private readonly ErpMockApp _erp = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _erp.DisposeAsync();

    [Fact]
    public async Task Projections_require_a_bearer_token()
    {
        using var anonymous = _erp.CreateClient();
        var response = await anonymous.GetAsync("/mock-erp/projection/v1/PartHandling.svc/InventoryPartSet", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OData_filter_select_and_server_paging_work()
    {
        using var client = await _erp.AuthorizedClientAsync();

        var blocked = await client.GetFromJsonAsync<JsonElement>(
            "/mock-erp/projection/v1/PartHandling.svc/InventoryPartSet?$filter=PartStatus eq 'O'&$select=PartNo", Ct);
        var part = Assert.Single(blocked.GetProperty("value").EnumerateArray());
        Assert.Equal("WL-E6013-32", part.GetProperty("PartNo").GetString());
        Assert.False(part.TryGetProperty("Description", out _)); // $select

        var page = await client.GetFromJsonAsync<JsonElement>("/mock-erp/projection/v1/InventoryPartInStockHandling.svc/InventoryPartInStockSet", Ct);
        Assert.Equal(10, page.GetProperty("value").GetArrayLength()); // page size 10
        Assert.Contains("$skip=10", page.GetProperty("@odata.nextLink").GetString());
    }

    [Fact]
    public async Task Same_external_reference_twice_is_a_conflict()
    {
        using var client = await _erp.AuthorizedClientAsync();
        const string url = "/mock-erp/projection/v1/PurchaseRequisitionHandling.svc/PurchaseRequisitionSet";
        var body = new { ExternalRef = "MT-TEST-1", Lines = new[] { new { PartNo = "ST-AH36-08", OriginalQty = 1 } } };

        var first = await client.PostAsJsonAsync(url, body, Ct);
        var second = await client.PostAsJsonAsync(url, body, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("DUPLICATE_EXTERNAL_REF", error.GetProperty("error").GetProperty("code").GetString());
    }
}
