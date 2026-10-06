using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Serenity.SectorEvents;

/// <summary>
/// Station event payload: a bluespace-error style grid (a vault, a weapons cache, a lost vessel) that FTLs into the
/// sector near a station for a while and is gone again when the event ends. Timing, weight and announcements come
/// from the sibling <c>StationEvent</c> component. Design after Frontier Station 14's sector events, written fresh.
/// </summary>
[RegisterComponent]
public sealed partial class SectorGridEventRuleComponent : Component
{
    /// <summary>
    /// Grid files to pick from, one is loaded.
    /// </summary>
    [DataField(required: true)]
    public List<ResPath> Paths = new();

    /// <summary>
    /// Localised name the grid shows up as on radar.
    /// </summary>
    [DataField]
    public LocId? GridName;

    /// <summary>
    /// Radar colour of the grid.
    /// </summary>
    [DataField]
    public Color IffColor = Color.FromHex("#E10F9B");

    /// <summary>
    /// Closest the event grid appears to the station grid's edge, in tiles.
    /// </summary>
    [DataField]
    public float MinimumDistance = 600f;

    /// <summary>
    /// Farthest the event grid appears from the station grid's edge, in tiles.
    /// </summary>
    [DataField]
    public float MaximumDistance = 1200f;

    /// <summary>
    /// The loaded grids; removed when the event ends.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Grids = new();

    /// <summary>
    /// The station this event came for; survivors left aboard when it ends are returned here.
    /// </summary>
    [ViewVariables]
    public EntityUid? Station;
}
