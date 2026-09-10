using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartnerIntegration.Core.Interfaces;
using PartnerIntegration.Infrastructure.Messaging;

namespace PartnerIntegration.Infrastructure.DependencyInjection;

public static class RabbitMqExtensions
{
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        return services;
    }
}