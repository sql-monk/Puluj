namespace Puluj.Processing;

public sealed class ProcessingOptions
{
    public const string Section = "Processing";
    public TimeSpan PendingPollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxAttempts { get; set; } = 3;
    public int PendingBatchSize { get; set; } = 200;
}
