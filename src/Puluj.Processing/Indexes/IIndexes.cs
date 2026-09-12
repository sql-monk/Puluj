namespace Puluj.Processing.Indexes;

/// <summary>Current taxonomy/gazetteer snapshots. Implemented by IndexProvider (database-backed) and by test fixtures.</summary>
public interface IIndexes
{
    TaxonomyIndex Taxonomy { get; }
    GazetteerIndex Gazetteer { get; }
}
