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
        services.AddSingleton<ObservationBuilder>();
        services.AddSingleton<AlertsInUaHandler>();
        services.AddSingleton<RawMessageProcessor>();
        services.AddHostedService<ProcessingLoop>();

        services.Configure<CorrelationOptions>(configuration.GetSection(CorrelationOptions.Section));
        services.AddSingleton<IObservationSink, Structured.TextAlertSink>(); // before correlation: it needs the intervals in place
        services.AddSingleton<IObservationSink, CorrelationSink>();
        services.AddHostedService<TrackWatchdog>();
        return services;
    }
}
