namespace Puluj.Processing.Llm;

public sealed class LlmOptions
{
    public const string Section = "Llm";
    public bool Enabled { get; set; }
    /// <summary>Anthropic model id.</summary>
    public string Model { get; set; } = "claude-opus-5";
    /// <summary>API key; falls back to the ANTHROPIC_API_KEY environment variable when empty.</summary>
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    /// <summary>Cap on LLM calls per minute; messages beyond it fall back to rule results only.</summary>
    public int MaxCallsPerMinute { get; set; } = 20;
    /// <summary>Bumped whenever the prompt changes; stored with every LLM-derived observation.</summary>
    public string PromptVersion { get; set; } = "1";
}
