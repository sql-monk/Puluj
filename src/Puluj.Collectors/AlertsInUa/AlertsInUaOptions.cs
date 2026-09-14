namespace Puluj.Collectors.AlertsInUa;

public sealed class AlertsInUaOptions
{
    public const string Section = "Collectors:AlertsInUa";
    public bool Enabled { get; set; }
    public string? Token { get; set; }
    public string BaseUrl { get; set; } = "https://api.alerts.in.ua/";
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>
    /// One-off history load for every oblast: `month_ago` (the deepest the API offers) or `week_ago`; null = off.
    /// Loaded once per period (progress in app_settings Runtime:AlertsInUa:History), see AlertsInUaHistoryCollector.
    /// </summary>
    public string? BackfillPeriod { get; set; }
}
