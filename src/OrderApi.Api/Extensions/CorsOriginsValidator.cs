namespace OrderApi.Api.Extensions;

public static class CorsOriginsValidator
{
    public const string ConfigKey = "Cors:AllowedOrigins";

    /// <summary>Comma separated origins. An empty list is only tolerated outside production.</summary>
    public static string[] GetAllowedOrigins(IConfiguration configuration, bool isProduction)
    {
        var origins = (configuration[ConfigKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (isProduction && origins.Length == 0)
        {
            throw new InvalidOperationException($"{ConfigKey} must contain at least one origin in production");
        }

        return origins;
    }
}
