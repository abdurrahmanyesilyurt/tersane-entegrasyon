using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Tersane.ErpMock;

/// <summary>
/// OData v4 "projections" in the style of IFS Cloud: one service root per projection
/// (/mock-erp/projection/v1/{Projection}.svc) with entity sets named *Set.
/// This is a MOCK; the routes and fields are invented for the demo.
/// </summary>
public static class ErpProjections
{
    public const string Root = "mock-erp/projection/v1";
    public const string Parts = Root + "/PartHandling.svc";
    public const string Stock = Root + "/InventoryPartInStockHandling.svc";
    public const string Requisitions = Root + "/PurchaseRequisitionHandling.svc";

    public static void Configure(ODataOptions options) => options
        .Select().Filter().OrderBy().Count().SetMaxTop(1000)
        .AddRouteComponents(Parts, Model(b => b.EntitySet<InventoryPart>("InventoryPartSet"), "PartHandling"))
        .AddRouteComponents(Stock, Model(b => b.EntitySet<InventoryPartInStock>("InventoryPartInStockSet"), "InventoryPartInStockHandling"))
        .AddRouteComponents(Requisitions, Model(b =>
        {
            b.ComplexType<PurchaseRequisitionLine>();
            b.EntitySet<PurchaseRequisition>("PurchaseRequisitionSet");
        }, "PurchaseRequisitionHandling"));

    private static IEdmModel Model(Action<ODataConventionModelBuilder> configure, string projection)
    {
        var builder = new ODataConventionModelBuilder { Namespace = $"MockErp.{projection}" };
        configure(builder);
        return builder.GetEdmModel();
    }

    public static JsonResult Error(int status, string code, string message)
        => new(new { error = new { code, message } }) { StatusCode = status };
}

public sealed class InventoryPartSetController(ErpStore store) : ODataController
{
    [EnableQuery(PageSize = 200)]
    public IQueryable<InventoryPart> Get() => store.Parts().AsQueryable();

    [EnableQuery]
    public SingleResult<InventoryPart> Get([FromRoute] string key)
        => SingleResult.Create(store.Parts().Where(p => p.PartNo == key).AsQueryable());
}

public sealed class InventoryPartInStockSetController(ErpStore store) : ODataController
{
    // Small page size on purpose: the integration worker has to follow @odata.nextLink.
    [EnableQuery(PageSize = 10)]
    public IQueryable<InventoryPartInStock> Get() => store.Stock().AsQueryable();
}

public sealed class PurchaseRequisitionSetController(ErpStore store, ILogger<PurchaseRequisitionSetController> logger) : ODataController
{
    [EnableQuery(PageSize = 100)]
    public IQueryable<PurchaseRequisition> Get() => store.Requisitions().AsQueryable();

    [EnableQuery]
    public SingleResult<PurchaseRequisition> Get([FromRoute] string key)
        => SingleResult.Create(store.Requisitions().Where(r => r.RequisitionNo == key).AsQueryable());

    public IActionResult Post([FromBody] PurchaseRequisition? requisition)
    {
        if (requisition is null || !ModelState.IsValid)
            return ErpProjections.Error(400, "INVALID_BODY", "İstek gövdesi okunamadı.");

        var result = store.CreateRequisition(requisition);
        switch (result.Outcome)
        {
            case CreateOutcome.Created:
                logger.LogInformation("Satınalma talebi {No} oluşturuldu (ExternalRef {Ref}).", result.Requisition!.RequisitionNo, result.Requisition.ExternalRef);
                return Created(result.Requisition);
            case CreateOutcome.Duplicate:
                logger.LogWarning("Mükerrer ExternalRef {Ref} → 409.", requisition.ExternalRef);
                Response.Headers.Location = $"/{ErpProjections.Requisitions}/PurchaseRequisitionSet('{result.Requisition!.RequisitionNo}')";
                return ErpProjections.Error(409, result.ErrorCode!, result.ErrorMessage!);
            default:
                logger.LogWarning("Satınalma talebi reddedildi: {Code} {Message}", result.ErrorCode, result.ErrorMessage);
                return ErpProjections.Error(400, result.ErrorCode!, result.ErrorMessage!);
        }
    }
}
