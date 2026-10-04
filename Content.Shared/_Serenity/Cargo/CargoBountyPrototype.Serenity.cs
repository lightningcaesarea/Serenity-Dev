namespace Content.Shared.Cargo.Prototypes;

public sealed partial class CargoBountyPrototype
{
    /// <summary>
    /// Serenity: research points paid to the station's research server when this bounty is sold,
    /// on top of the <see cref="Reward"/> in Federal Bills.
    /// </summary>
    [DataField]
    public int ResearchPoints;
}
