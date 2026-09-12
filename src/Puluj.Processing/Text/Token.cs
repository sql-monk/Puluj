namespace Puluj.Processing.Text;

/// <summary>A normalized (lower-cased) word with its span in the segment text.</summary>
public sealed record Token(string Text, int Start, int End)
{
    public int Length => End - Start;
    public bool IsNumeric => Text.Length > 0 && Text.All(char.IsDigit);
}

public sealed record Segment(int Index, string Text, IReadOnlyList<Token> Tokens)
{
    public string Slice(int fromToken, int toTokenExclusive) =>
        Tokens.Count == 0 ? "" : Text[Tokens[fromToken].Start..Tokens[Math.Min(toTokenExclusive, Tokens.Count) - 1].End];
}

public sealed record NormalizedMessage(string Text, IReadOnlyList<Segment> Segments, string Language);
