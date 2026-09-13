using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Puluj.Processing.Correlation;
using Puluj.Processing.Indexes;
using Puluj.Processing.Llm;
using Puluj.Processing.Parsing;
using Puluj.Processing.Pipeline;
using Puluj.Processing.Structured;
using Puluj.Processing.Text;

namespace Puluj.Processing;

public static class DependencyInjection
{
    public static IServiceCollection AddPulujProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProcessingOptions>(configuration.GetSection(ProcessingOptions.Section));

        services.AddSingleton<IndexProvider>();
        services.AddSingleton<IIndexes>(sp => sp.GetRequiredService<IndexProvider>());
        services.AddHostedService(sp => sp.GetRequiredService<IndexProvider>());

        services.AddSingleton<INormalizer, Normalizer>();
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.Section));
        services.AddSingleton<RuleParser>();
        services.AddSingleton<IParser, LlmParser>(); // rules first, model only as a fallback
        services.AddSingleton<TargetBuilder>();
        services.AddSingleton<AlertsInUaHandler>();
        services.AddSingleton<RawMessageProcessor>();
        services.AddHostedService<ProcessingLoop>();

        services.Configure<CorrelationOptions>(configuration.GetSection(CorrelationOptions.Section));
        services.AddSingleton<ITargetSink, Structured.TextAlertSink>(); // before correlation: it needs the intervals in place
        services.AddSingleton<ITargetSink, CorrelationSink>();
        services.AddHostedService<TrackWatchdog>();
        return services;
    }
}
