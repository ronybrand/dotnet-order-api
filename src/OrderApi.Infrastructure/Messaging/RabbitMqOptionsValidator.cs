using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OrderApi.Application.Events;

namespace OrderApi.Infrastructure.Messaging;

public sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.UserName) || string.IsNullOrWhiteSpace(options.Password))
        {
            return ValidateOptionsResult.Fail("RabbitMq:UserName and RabbitMq:Password must be configured");
        }

        return options.UseTls || options.AllowInsecure
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("RabbitMq:UseTls must be true (RabbitMq:AllowInsecure is for local development only)");
    }
}

public static class RabbitMqOptionsExtensions
{
    /// <summary>Binds RabbitMq options and fails the host startup when credentials or TLS are missing.</summary>
    public static IServiceCollection AddRabbitMqOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>());
        return services;
    }
}
