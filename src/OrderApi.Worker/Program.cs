using OrderApi.Application.Events;
using OrderApi.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
builder.Services.AddSingleton<NotificationProcessor>();
builder.Services.AddHostedService<OrderStatusConsumer>();

await builder.Build().RunAsync();
