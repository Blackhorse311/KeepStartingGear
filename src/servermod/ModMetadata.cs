// ============================================================================
// Keep Starting Gear - Mod Metadata
// ============================================================================
// This class provides metadata required by the SPT server to load the mod.
// It tells SPT about the mod's name, version, author, and compatibility.
//
// SPT MOD SYSTEM:
// SPT server mods must include a class that inherits from AbstractModMetadata.
// The server discovers this class automatically and uses it to:
// - Display mod information in the server console
// - Check version compatibility with SPT
// - Track mod dependencies
//
// VERSIONING:
// Uses Semantic Versioning (SemVer): MAJOR.MINOR.PATCH
// - MAJOR: Breaking changes
// - MINOR: New features (backwards compatible)
// - PATCH: Bug fixes (backwards compatible)
//
// SPT VERSION COMPATIBILITY:
// The SptVersion property uses a range expression (e.g., "~4.0.0")
// - ~4.0.0 means compatible with 4.0.x
// - ^4.0.0 would mean compatible with 4.x.x
//
// AUTHOR: Blackhorse311
// LICENSE: MIT
// ============================================================================

using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Blackhorse311.KeepStartingGear.Server;

/// <summary>
/// Mod metadata required by SPT server to load and identify the mod.
/// Inherits from AbstractModMetadata to provide required information.
/// </summary>
/// <remarks>
/// <para>
/// This class uses C# record syntax for immutable properties with default values.
/// All properties are initialized with default values but can be overridden.
/// </para>
/// <para>
/// <b>Key Properties:</b>
/// </para>
/// <list type="bullet">
///   <item>ModGuid: Unique identifier (lowercase, dot-separated)</item>
///   <item>Name: Human-readable mod name</item>
///   <item>Version: Current mod version (Semantic Versioning)</item>
///   <item>SptVersion: Compatible SPT version range</item>
/// </list>
/// </remarks>
public record ModMetadata : AbstractModMetadata
{
    /// <summary>
    /// Unique identifier for this mod.
    /// Must be lowercase and use dot notation (author.modname).
    /// </summary>
    public override string ModGuid { get; init; } = "com.blackhorse311.keepstartinggear";

    /// <summary>
    /// Human-readable name displayed in server console and mod lists.
    /// </summary>
    public override string Name { get; init; } = "Keep Starting Gear (Server)";

    /// <summary>
    /// Primary author of the mod.
    /// </summary>
    public override string Author { get; init; } = "Blackhorse311";

    /// <summary>
    /// List of additional contributors who helped with development.
    /// Bug hunters and testers who helped improve the mod.
    /// </summary>
    public override List<string>? Contributors { get; init; } = ["Troyoza", "Wolthon", "rSlade"];

    /// <summary>
    /// Current version of the mod using Semantic Versioning.
    /// Format: MAJOR.MINOR.PATCH
    /// </summary>
    public override SemanticVersioning.Version Version { get; init; } = new("2.0.4");

    /// <summary>
    /// Range of compatible SPT server versions.
    /// ~4.0.0 means compatible with SPT 4.0.x
    /// </summary>
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");

    /// <summary>
    /// List of mod GUIDs that are incompatible with this mod.
    /// Empty list means no known incompatibilities.
    /// </summary>
    public override List<string>? Incompatibilities { get; init; } = [];

    /// <summary>
    /// Dictionary of required mod dependencies and their version ranges.
    /// Empty dictionary means no dependencies on other mods.
    /// </summary>
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = [];

    /// <summary>
    /// URL for the mod's homepage or repository.
    /// </summary>
    public override string? Url { get; init; } = "https://github.com/Blackhorse311/KeepStartingGear";

    /// <summary>
    /// Whether this mod includes asset bundles.
    /// False for code-only mods.
    /// </summary>
    public override bool? IsBundleMod { get; init; } = false;

    /// <summary>
    /// License under which this mod is distributed.
    /// </summary>
    public override string License { get; init; } = "MIT";
}
