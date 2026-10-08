using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Serenity.Medical.Sterility;

/// <summary>
/// The blood culture analyzer. Take a container holding a patient's blood, and it prints a report naming the
/// pathogen causing their infection.
/// </summary>
[RegisterComponent]
public sealed partial class PathogenAnalyzerComponent : Component
{
    [DataField]
    public TimeSpan AnalyzeTime = TimeSpan.FromSeconds(5);

    [DataField]
    public EntProtoId ReportPrototype = "DiagnosisReportPaper";
}

/// <summary>
/// A printed analyzer report. Records which pathogen was found so the antibiotic synthesizer can be programmed.
/// </summary>
[RegisterComponent]
public sealed partial class PathogenReportComponent : Component
{
    [DataField]
    public ProtoId<PathogenPrototype>? Pathogen;
}

/// <summary>
/// The antibiotic synthesizer. Programmed from an analyzer report, it turns broad-spectrum antibiotic into the
/// narrow-spectrum drug that cures that pathogen.
/// </summary>
[RegisterComponent]
public sealed partial class PathogenSynthesizerComponent : Component
{
    [DataField]
    public TimeSpan SynthesisTime = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The reagent consumed, unit for unit.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype> Source = "Antibiox";

    [DataField]
    public ProtoId<PathogenPrototype>? Programmed;
}
