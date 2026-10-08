using Content.Shared._Serenity.Medical.Wounds;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Serenity.Medical.Sterility;

/// <summary>
/// A strain of bacteria an infection can be caused by. Every infection carries one. Only its narrow-spectrum
/// antibiotic cures it (a broad-spectrum one just holds it back), and which strain a patient has can only be learned
/// from a blood sample in the blood culture analyzer.
/// </summary>
[Prototype]
public sealed partial class PathogenPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// The narrow-spectrum antibiotic that cures this strain. Named on the culture report and made by the antibiotic
    /// synthesizer.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Drug;

    /// <summary>
    /// The status effect <see cref="Drug"/> applies to the patient while it is in their blood.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId CureEffect;

    /// <summary>
    /// How likely this strain is to cause an infection from a dirty operation, relative to the other strains.
    /// </summary>
    [DataField]
    public float SurgeryWeight = 1f;

    /// <summary>
    /// How likely this strain is to cause an infection that starts in an untreated open wound of each category,
    /// relative to the other strains. A category not listed here is never this strain's source.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<WoundCategoryPrototype>, float> WoundWeights = new();
}
