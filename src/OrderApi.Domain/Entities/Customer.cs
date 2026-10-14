using OrderApi.Domain.Common;

namespace OrderApi.Domain.Entities;

public class Customer : IAuditable, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [Sensitive] public string TaxId { get; set; } = string.Empty;
    [Sensitive] public string? PassportNumber { get; set; }
    [Sensitive] public string Email { get; set; } = string.Empty;

    /// <summary>Optimistic concurrency token (Postgres xmin).</summary>
    public uint Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
