using Content.Server.Administration;
using Content.Shared._Serenity.Medical.Sterility;
using Content.Shared._Serenity.Medical.Wounds;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Medical.Sterility;

/// <summary>
/// Gives a mob a wound infection for testing: <c>infect [entity uid] [tier] [pathogen]</c>. With no entity it targets
/// the admin's own mob; with no pathogen the strain is picked at random.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class InfectCommand : LocalizedEntityCommands
{
    [Dependency] private InfectionSystem _infections = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override string Command => "infect";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 3)
        {
            shell.WriteLine(Help);
            return;
        }

        EntityUid target;
        if (args.Length >= 1)
        {
            if (!NetEntity.TryParse(args[0], out var netEntity) || !EntityManager.TryGetEntity(netEntity, out var found))
            {
                shell.WriteError(Loc.GetString("cmd-infect-bad-entity", ("entity", args[0])));
                return;
            }

            target = found.Value;
        }
        else if (shell.Player?.AttachedEntity is { } attached)
        {
            target = attached;
        }
        else
        {
            shell.WriteError(Loc.GetString("cmd-infect-no-target"));
            return;
        }

        var tier = 1;
        if (args.Length == 2 && (!int.TryParse(args[1], out tier) || tier is < 1 or > WoundsConstants.MaxWoundTier))
        {
            shell.WriteError(Loc.GetString("cmd-infect-bad-tier", ("max", WoundsConstants.MaxWoundTier)));
            return;
        }

        ProtoId<PathogenPrototype>? pathogen = null;
        if (args.Length == 3)
        {
            if (!_proto.HasIndex<PathogenPrototype>(args[2]))
            {
                shell.WriteError(Loc.GetString("cmd-infect-bad-pathogen", ("pathogen", args[2])));
                return;
            }

            pathogen = args[2];
        }

        if (!EntityManager.TryGetComponent<WoundComponent>(target, out var wounds))
        {
            shell.WriteError(Loc.GetString("cmd-infect-no-wounds", ("entity", EntityManager.ToPrettyString(target))));
            return;
        }

        var set = _infections.SetInfection(target, wounds, tier, pathogen);
        shell.WriteLine(Loc.GetString("cmd-infect-done", ("entity", EntityManager.ToPrettyString(target)), ("tier", set)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-infect-hint-entity")),
            2 => CompletionResult.FromHintOptions(["1", "2", "3"], Loc.GetString("cmd-infect-hint-tier")),
            3 => CompletionResult.FromHintOptions(
                CompletionHelper.PrototypeIDs<PathogenPrototype>(proto: _proto),
                Loc.GetString("cmd-infect-hint-pathogen")),
            _ => CompletionResult.Empty,
        };
    }
}
