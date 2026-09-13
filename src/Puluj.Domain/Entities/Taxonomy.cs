using System.Text.Json;
using Puluj.Domain.Enums;

namespace Puluj.Domain.Entities;

/// <summary>Spec §7. Top level: UAV, Missile, Aircraft, GuidedBomb, Unknown.</summary>
public class TargetCategory
{
    public int TargetCategoryId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public ICollection<TargetClass> Classes { get; set; } = [];
}

/// <summary>Spec §7. E.g. StrikeUAV, CruiseMissile, BallisticMissile.</summary>
public class TargetClass
{
    public int TargetClassId { get; set; }
    public int TargetCategoryId { get; set; }
    public TargetCategory? Category { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Class-level defaults: speed range, ETA enabled, fade profile, correlation windows.</summary>
    public JsonDocument? Metadata { get; set; }
    public ICollection<TargetFamily> Families { get; set; } = [];
}

/// <summary>Spec §7. E.g. ShahedFamily, Kh-101/555 family.</summary>
public class TargetFamily
{
    public int TargetFamilyId { get; set; }
    public int TargetClassId { get; set; }
    public TargetClass? Class { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public JsonDocument? Metadata { get; set; }
    public ICollection<TargetModel> Models { get; set; } = [];
}

/// <summary>Spec §8. Concrete model (Shahed-136, Kh-101...).</summary>
public class TargetModel
{
    public int TargetModelId { get; set; }
    public int TargetFamilyId { get; set; }
    public TargetFamily? Family { get; set; }
    public required string Code { get; set; }
    public required string CanonicalName { get; set; }
    public string? Manufacturer { get; set; }
    public string? Country { get; set; }
    public bool Enabled { get; set; } = true;
    public JsonDocument? Metadata { get; set; }
}

/// <summary>Spec §8. Text alias mapped to some level of the hierarchy.</summary>
public class TargetModelAlias
{
    public int TargetModelAliasId { get; set; }
    public required string Alias { get; set; }
    /// <summary>ISO 639-1 (uk, ru, en) or "*".</summary>
    public string Language { get; set; } = "*";
    public AliasTargetLevel TargetLevel { get; set; }
    public int TargetId { get; set; }
    /// <summary>When set, alias applies only to messages from this source.</summary>
    public int? SourceId { get; set; }
    /// <summary>Higher wins when several aliases overlap in text.</summary>
    public int Priority { get; set; }
    /// <summary>When true the alias must match whole tokens; otherwise each alias word is a token prefix (stem).</summary>
    public bool ExactMatch { get; set; }
    /// <summary>Confidence implied by the alias itself (e.g. "шахед" is Medium, "Shahed-136" is High).</summary>
    public ConfidenceLevel ImpliedConfidence { get; set; } = ConfidenceLevel.Medium;
}
