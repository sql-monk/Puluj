namespace Puluj.Processing.Pipeline;

/// <summary>Name of this processor instance: written to raw_messages.claimed_by and checked before a claimed row is processed.</summary>
public sealed record ProcessorIdentity(string Name);
