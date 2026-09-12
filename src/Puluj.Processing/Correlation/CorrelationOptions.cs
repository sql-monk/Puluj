namespace Puluj.Processing.Correlation;

public sealed class CorrelationOptions
{
    public const string Section = "Correlation";
    /// <summary>Observations of the same thing from different messages within this window are duplicates.</summary>
    public TimeSpan DuplicateWindow { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>Minimum association score to attach an observation to an existing track.</summary>
    public double AttachThreshold { get; set; } = 0.6;
    /// <summary>Extra distance tolerance on top of speed × time and location accuracies.</summary>
    public double SlackKm { get; set; } = 30;
    /// <summary>Tracks without updates for this many correlation windows are closed by the watchdog.</summary>
    public double CloseAfterWindows { get; set; } = 2;
    public TimeSpan WatchdogInterval { get; set; } = TimeSpan.FromMinutes(1);
}
