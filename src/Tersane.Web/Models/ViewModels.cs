using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Tersane.Data.Entities;

namespace Tersane.Web.Models;

public sealed class DashboardVm
{
    public required IntegrationHealth Health { get; init; }
    public int OpenWorkOrders { get; init; }
    public int ActiveProjects { get; init; }
    public int OpenRequests { get; init; }
    public int CriticalStockItems { get; init; }
    public required List<MaterialRequest> RecentRequests { get; init; }
    public required List<IntegrationLog> RecentErrors { get; init; }
    public required List<SyncState> SyncStates { get; init; }
}

public sealed class ProjectDetailVm
{
    public required Project Project { get; init; }
    public required List<ProjectMaterialSummaryRow> MaterialSummary { get; init; }
    public required List<MaterialRequest> Requests { get; init; }
}

public sealed class WorkOrderListVm
{
    public required List<WorkOrder> Items { get; init; }
    public int? ProjectId { get; init; }
    public WorkOrderStatus? Status { get; init; }
    public required SelectList Projects { get; init; }
}

public sealed class WorkOrderInput
{
    [Display(Name = "Proje (tekne no)")]
    [Required(ErrorMessage = "Proje seçiniz.")]
    public int? ProjectId { get; set; }

    [Display(Name = "Blok")]
    public int? BlockId { get; set; }

    [Display(Name = "İş tanımı")]
    [Required(ErrorMessage = "İş tanımı zorunludur.")]
    [StringLength(200, MinimumLength = 5, ErrorMessage = "İş tanımı 5-200 karakter olmalı.")]
    public string? Title { get; set; }

    [Display(Name = "Atölye / bölüm")]
    [Required(ErrorMessage = "Bölüm seçiniz.")]
    public string? Department { get; set; }

    [Display(Name = "Planlanan başlangıç")]
    [Required(ErrorMessage = "Başlangıç tarihi zorunludur.")]
    [DataType(DataType.Date)]
    public DateOnly? PlannedStart { get; set; }

    [Display(Name = "Planlanan bitiş")]
    [Required(ErrorMessage = "Bitiş tarihi zorunludur.")]
    [DataType(DataType.Date)]
    public DateOnly? PlannedEnd { get; set; }

    [Display(Name = "Tahmini adam-saat")]
    [Range(1, 100000, ErrorMessage = "Adam-saat 1 ile 100.000 arasında olmalı.")]
    public decimal EstimatedManHours { get; set; } = 40;
}

public sealed class MaterialRequestListVm
{
    public required List<MaterialRequest> Items { get; init; }
    public MaterialRequestStatus? Status { get; init; }
    public string? Search { get; init; }
}

public sealed class MaterialRequestInput
{
    [Display(Name = "Proje (tekne no)")]
    [Required(ErrorMessage = "Proje seçiniz.")]
    public int? ProjectId { get; set; }

    [Display(Name = "Blok")]
    public int? BlockId { get; set; }

    [Display(Name = "İş emri")]
    public int? WorkOrderId { get; set; }

    [Display(Name = "İhtiyaç tarihi")]
    [Required(ErrorMessage = "İhtiyaç tarihi zorunludur.")]
    [DataType(DataType.Date)]
    public DateOnly? NeedDate { get; set; }

    [Display(Name = "Açıklama")]
    [StringLength(1000)]
    public string? Note { get; set; }

    public List<MaterialRequestLineInput> Lines { get; set; } = [new()];
}

public sealed class MaterialRequestLineInput
{
    [Required(ErrorMessage = "Malzeme seçiniz.")]
    public int? MaterialId { get; set; }

    [Required(ErrorMessage = "Miktar giriniz.")]
    [Range(0.001, 1000000, ErrorMessage = "Miktar 0'dan büyük olmalı.")]
    public decimal? Quantity { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }
}

public sealed class MaterialRequestDetailVm
{
    public required MaterialRequest Request { get; init; }
    public required List<OutboxMessage> Messages { get; init; }
    public required List<IntegrationLog> Logs { get; init; }
    public required Dictionary<int, StockStatusRow> Stock { get; init; }
}

public sealed class StockVm
{
    public required List<StockStatusRow> Rows { get; init; }
    public SyncState? StockSync { get; init; }
    public SyncState? PartsSync { get; init; }
}

public sealed class IntegrationVm
{
    public required IntegrationHealth Health { get; init; }
    public required List<OutboxMessage> Messages { get; init; }
    public required List<IntegrationLog> Logs { get; init; }
    public required List<SyncState> SyncStates { get; init; }
    public OutboxStatus? Status { get; init; }
    public string? Level { get; init; }
    public string ErpMockUrl { get; init; } = "";
}

/// <summary>Failure rate travels as an integer percentage: "0.3" would bind as 3 under tr-TR culture.</summary>
public sealed record ChaosInput(bool? Outage, int? FailurePercent, int? LatencyMs);
