using OrderApi.Domain.Common;

namespace OrderApi.Api.Observability;

public static class SensitiveMasker
{
    public const string Redacted = "***REDACTED***";

    /// <summary>
    /// Snapshot of an object for logging, with every [Sensitive] property redacted. Use it whenever a
    /// whole entity/DTO is logged; never to build an HTTP response.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Mask(object value) =>
        value.GetType().GetProperties().ToDictionary(
            property => property.Name,
            property =>
            {
                var current = property.GetValue(value);
                return Attribute.IsDefined(property, typeof(SensitiveAttribute)) && current is not null ? Redacted : current;
            });
}
