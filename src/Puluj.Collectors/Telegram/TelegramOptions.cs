namespace Puluj.Collectors.Telegram;

public sealed class TelegramOptions
{
    public const string Section = "Collectors:Telegram";
    public bool Enabled { get; set; }
    public int ApiId { get; set; }
    public string? ApiHash { get; set; }
    /// <summary>Phone number of the account used for reading channels, international format.</summary>
    public string? Phone { get; set; }
    /// <summary>2FA password, if the account has one.</summary>
    public string? Password { get; set; }
    /// <summary>One-time login code; alternatively drop it into &lt;SessionPath&gt;.code while the worker waits.</summary>
    public string? VerificationCode { get; set; }
    public string SessionPath { get; set; } = "session/puluj.session";
    /// <summary>Join public channels automatically so live updates arrive (history is readable without joining).</summary>
    public bool AutoJoin { get; set; } = true;
    /// <summary>How many recent messages per channel to backfill on first start.</summary>
    public int BackfillLimit { get; set; } = 30;
}
