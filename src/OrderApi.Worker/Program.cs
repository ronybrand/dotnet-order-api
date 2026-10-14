using OrderApi.Infrastructure.Messaging;
using OrderApi.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRabbitMqOptions(builder.Configuration);
builder.Services.AddSmtpOptions(builder.Configuration);
builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
builder.Services.AddSingleton<NotificationProcessor>();
builder.Services.AddHostedService<OrderStatusConsumer>();

await builder.Build().RunAsync();
