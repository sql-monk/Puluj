namespace Puluj.Collectors.AlertsInUa;

public sealed class AlertsInUaOptions
{
    public const string Section = "Collectors:AlertsInUa";
    public bool Enabled { get; set; }
    public string? Token { get; set; }
    public string BaseUrl { get; set; } = "https://api.alerts.in.ua/";
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
}
