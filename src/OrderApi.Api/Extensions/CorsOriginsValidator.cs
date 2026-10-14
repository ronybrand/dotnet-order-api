namespace OrderApi.Api.Extensions;

public static class CorsOriginsValidator
{
    public const string ConfigKey = "Cors:AllowedOrigins";

    /// <summary>Comma separated origins. An empty list is only tolerated in Development: any other environment (Staging included) fails fast.</summary>
    public static string[] GetAllowedOrigins(IConfiguration configuration, bool isDevelopment)
    {
        var origins = (configuration[ConfigKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!isDevelopment && origins.Length == 0)
        {
            throw new InvalidOperationException($"{ConfigKey} must contain at least one origin outside Development");
        }

        return origins;
    }
}
