using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderApi.Api.Serialization;

public static class ApiJson
{
    public static readonly JsonSerializerOptions ErrorOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Wire format: snake_case, unknown members rejected (equivalent to extra="forbid").</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    }

    public static string ToSnakeCase(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
