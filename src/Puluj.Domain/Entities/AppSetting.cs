namespace Puluj.Domain.Entities;

/// <summary>
/// Configuration entered through the admin UI. Keys use the configuration path syntax ("Collectors:Telegram:ApiId")
/// and override appsettings/env in both Worker and Api. Secret values are never returned by the API.
/// </summary>
public class AppSetting
{
    public required string Key { get; set; }
    public string? Value { get; set; }
    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
