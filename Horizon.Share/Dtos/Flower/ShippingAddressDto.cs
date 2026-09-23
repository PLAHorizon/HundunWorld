using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>收货地址 API DTO（对应 ShippingAddressState，纯 POCO）。</summary>
public class ShippingAddressDto
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string ShipTo { get; set; } = "";
    public string Phone { get; set; } = "";
    public int? ProvinceId { get; set; }
    public string ProvinceName { get; set; } = "";
    public int? CityId { get; set; }
    public string CityName { get; set; } = "";
    public int? DistrictId { get; set; }
    public string DistrictName { get; set; } = "";
    public string Address { get; set; } = "";
    public bool IsDefault { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Passport { get; set; } = "";
}
