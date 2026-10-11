using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Botany;

/// <summary>
/// A handheld scanner for plants. Used on a tray, a growing plant, a seed packet or a piece of produce, it opens
/// a readout that keeps updating while the scanned thing stays in range.
/// </summary>
[RegisterComponent]
public sealed partial class PlantAnalyzerComponent : Component
{
    /// <summary>
    /// How far the scanned thing can be from the analyzer before the readout closes.
    /// </summary>
    [DataField]
    public float MaxScanDistance = 10f;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public SoundSpecifier? ScanSound;

    /// <summary>
    /// What is currently being read.
    /// </summary>
    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public TimeSpan NextUpdate;
}

[Serializable, NetSerializable]
public enum PlantAnalyzerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class PlantAnalyzerUiState(
    NetEntity? target,
    string targetName,
    PlantAnalyzerTrayData? tray,
    PlantAnalyzerPlantData? plant,
    List<PlantAnalyzerReagent> soil,
    List<PlantAnalyzerReagent> produce,
    List<string> mutatesInto) : BoundUserInterfaceState
{
    public readonly NetEntity? Target = target;
    public readonly string TargetName = targetName;

    /// <summary>
    /// Null when the scanned thing is not a tray.
    /// </summary>
    public readonly PlantAnalyzerTrayData? Tray = tray;

    /// <summary>
    /// Null when the tray holds no plant.
    /// </summary>
    public readonly PlantAnalyzerPlantData? Plant = plant;

    /// <summary>
    /// The chemicals in the tray's soil.
    /// </summary>
    public readonly List<PlantAnalyzerReagent> Soil = soil;

    /// <summary>
    /// The chemicals the plant's produce will contain.
    /// </summary>
    public readonly List<PlantAnalyzerReagent> Produce = produce;

    /// <summary>
    /// Names of the species the plant can turn into when it gets unstable enough.
    /// </summary>
    public readonly List<string> MutatesInto = mutatesInto;
}

[Serializable, NetSerializable]
public sealed record PlantAnalyzerTrayData(
    float Water,
    float MaxWater,
    float Nutrition,
    float MaxNutrition,
    float Weeds,
    float MaxWeeds,
    float Pests,
    float MaxPests,
    float Toxins,
    float MaxToxins,
    float WeedGrowthChance,
    List<string> WeedRaisers,
    List<string> PestRaisers,
    List<string> ToxinRaisers);

[Serializable, NetSerializable]
public sealed record PlantAnalyzerPlantData(
    string Name,
    bool Growing,
    bool Dead,
    bool ReadyForHarvest,
    float Health,
    float Endurance,
    float AgeSeconds,
    float MaturationSeconds,
    float ProductionSeconds,
    float LifespanSeconds,
    int Yield,
    float Potency,
    float Instability);

/// <summary>
/// A reagent line: its name, how many units, and for produce the share of the total.
/// </summary>
[Serializable, NetSerializable]
public sealed record PlantAnalyzerReagent(string Name, float Units, float Percent);
