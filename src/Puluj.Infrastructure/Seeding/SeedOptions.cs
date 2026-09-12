namespace Puluj.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    /// <summary>Directory containing taxonomy/, sources.json, gazetteer/. Relative paths resolve against the content root.</summary>
    public string DataDirectory { get; set; } = "data";
    public bool SeedTaxonomy { get; set; } = true;
    public bool SeedSources { get; set; } = true;
    public bool SeedGazetteer { get; set; } = true;
}
