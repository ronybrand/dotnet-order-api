namespace OrderApi.Domain.Common;

/// <summary>Column limits shared by the EF configuration and the request validators, so an oversized value is a 400 and never a database error.</summary>
public static class FieldLimits
{
    public const int CustomerName = 200;
    public const int Email = 320;
    public const int ItemDescription = 255;

    /// <summary>Per line item: keeps a single order total far from absurd values and the decimal range.</summary>
    public const int MaxQuantity = 10_000;

    /// <summary>Largest value of the numeric(12,2) unit price column.</summary>
    public const decimal MaxUnitPrice = 9_999_999_999.99m;
}
