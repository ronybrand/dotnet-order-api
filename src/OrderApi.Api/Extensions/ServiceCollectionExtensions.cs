using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization.Metadata;
using OrderApi.Api.Filters;
using OrderApi.Api.Serialization;
using OrderApi.Api.Services;
using OrderApi.Application.Common;

namespace OrderApi.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddValidatorsFromAssemblyContaining<SearchQueryValidator>();

        services.AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddJsonOptions(options =>
            {
                ApiJson.Configure(options.JsonSerializerOptions);
                options.JsonSerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
                {
                    Modifiers = { SensitiveJsonModifier.Modify },
                };
            })
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = context =>
                {
                    // Malformed JSON, unknown members or unparsable ids: same error contract as validators.
                    var entry = context.ModelState.FirstOrDefault(e => e.Value?.Errors.Count > 0);
                    var field = string.IsNullOrEmpty(entry.Key) ? "body" : entry.Key;
                    return new BadRequestObjectResult(
                        ValidationExtensions.ValidationError("Invalid request.", field, context.HttpContext.TraceIdentifier));
                });

        return services;
    }

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Order API", Version = "v1" });
            var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(ServiceCollectionExtensions).Assembly.GetName().Name}.xml");
            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml);
            }

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
            });
        });
        return services;
    }
}
