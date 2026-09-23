using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>种植批次 API DTO（对应 PlantingBatchState，纯 POCO）。</summary>
public class PlantingBatchDto
{
    public long Id { get; set; }
    public string BatchName { get; set; } = "";
    public string SpeciesId { get; set; } = "";
    public string SpeciesName { get; set; } = "";
    public string GreenhouseId { get; set; } = "";
    public DateTime PlantingDate { get; set; }
    public DateTime? ExpectedHarvestDate { get; set; }
    public DateTime? ActualHarvestDate { get; set; }
    public string Status { get; set; } = "Planted";
    public int PlantingQuantity { get; set; }
    public string Remark { get; set; } = "";
    public Guid UserId { get; set; }
    public string Passport { get; set; } = "";
}

/// <summary>成本记录 API DTO（对应 CostRecordState，纯 POCO）。</summary>
public class CostRecordDto
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public string Category { get; set; } = "Other";
    public decimal Amount { get; set; }
    public DateTime CostDate { get; set; }
    public string Remark { get; set; } = "";
}

/// <summary>产量记录 API DTO（对应 YieldRecordState，纯 POCO）。</summary>
public class YieldRecordDto
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public string SpeciesId { get; set; } = "";
    public string SpeciesName { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "Stems";
    public string Grade { get; set; } = "A";
    public DateTime HarvestDate { get; set; }
    public string Remark { get; set; } = "";
}
