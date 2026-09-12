using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Puluj.Collectors.AlertsInUa;
using Puluj.Collectors.Telegram;

namespace Puluj.Collectors;

public static class DependencyInjection
{
    public static IServiceCollection AddPulujCollectors(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AlertsInUaOptions>(configuration.GetSection(AlertsInUaOptions.Section));
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.Section));

        // Retry + circuit breaker + timeout for every HTTP source (spec §29).
        services.AddHttpClient(AlertsInUaCollector.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20))
            .AddStandardResilienceHandler();

        services.AddSingleton<CollectorStateStore>();
        services.AddSingleton<ICollector, AlertsInUaCollector>();
        services.AddSingleton<ICollector, TelegramCollector>();
        services.AddHostedService<CollectorSupervisor>();
        return services;
    }
}
