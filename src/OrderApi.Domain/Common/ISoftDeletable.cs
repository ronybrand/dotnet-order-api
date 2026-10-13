namespace OrderApi.Domain.Common;

public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
