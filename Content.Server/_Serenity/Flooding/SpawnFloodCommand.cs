using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Flooding;

/// <summary>
///     Pours flood water onto the tile you are standing on, for testing and events.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class SpawnFloodCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private FloodSystem _flood = default!;

    private static readonly ProtoId<ReagentPrototype> DefaultReagent = "Water";

    public override string Command => "spawnflood";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (!float.TryParse(args[0], out var units) || !float.IsFinite(units) || units <= 0)
        {
            shell.WriteError(Loc.GetString("cmd-spawnflood-bad-units", ("units", args[0])));
            return;
        }

        var reagent = args.Length == 2 ? new ProtoId<ReagentPrototype>(args[1]) : DefaultReagent;
        if (!_prototype.HasIndex(reagent))
        {
            shell.WriteError(Loc.GetString("cmd-spawnflood-bad-reagent", ("reagent", reagent.Id)));
            return;
        }

        var solution = new Solution();
        solution.AddReagent(new ReagentId(reagent.Id, null), FixedPoint2.New(units));

        if (!_flood.TryFloodAt(EntityManager.GetComponent<TransformComponent>(player).Coordinates, solution, out _))
            shell.WriteError(Loc.GetString("cmd-spawnflood-no-floor"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-spawnflood-units-hint")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<ReagentPrototype>(proto: _prototype),
                Loc.GetString("cmd-spawnflood-reagent-hint")),
            _ => CompletionResult.Empty,
        };
    }
}
