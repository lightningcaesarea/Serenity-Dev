namespace Content.Server._Serenity.GameTicking.Rules;

/// <summary>
/// Serenity's default round mode, after Frontier's adventure rule: no antagonists, and the round-end summary lists
/// how many Federal Bills each character made or lost this round.
/// </summary>
[RegisterComponent, Access(typeof(SerenityAdventureRuleSystem))]
public sealed partial class SerenityAdventureRuleComponent : Component
{
    /// <summary>How many of the best and worst earners to name at the end of the summary.</summary>
    [DataField]
    public int TopCount = 3;
}
