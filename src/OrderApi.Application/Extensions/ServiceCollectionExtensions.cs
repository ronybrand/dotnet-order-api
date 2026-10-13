using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderApi.Application.Services;
using OrderApi.Application.Services.Interfaces;

namespace OrderApi.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<Validators.ItemRequestValidator>();
        return services;
    }
}
