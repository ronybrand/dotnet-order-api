namespace OrderApi.Domain.Common;

/// <summary>Marks PII/credential properties so logging and JSON serialization mask them centrally.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute : Attribute;
