using System.Text.Json.Serialization.Metadata;
using OrderApi.Domain.Common;

namespace OrderApi.Api.Serialization;

/// <summary>Safety net: a [Sensitive] property that ends up in a serialized type is never written.</summary>
public static class SensitiveJsonModifier
{
    public static void Modify(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider?.IsDefined(typeof(SensitiveAttribute), true) == true)
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }
}
