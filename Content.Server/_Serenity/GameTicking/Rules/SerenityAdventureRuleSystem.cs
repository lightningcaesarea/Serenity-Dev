using System.Globalization;
using System.Linq;
using Content.Server._Serenity.Economy;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared._NullLink;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Serenity.GameTicking.Rules;

/// <summary>
/// Tracks each played character's Federal Bills over the round and reports the profit or loss at round end.
/// </summary>
public sealed partial class SerenityAdventureRuleSystem : GameRuleSystem<SerenityAdventureRuleComponent>
{
    private const string Credits = "credits";

    [Dependency] private ICharacterBalanceManager _balances = default!;
    [Dependency] private ISharedNullLinkPlayerResourcesManager _resources = default!;

    /// <summary>Every character played this round, keyed by player and character DB id.</summary>
    private readonly Dictionary<(NetUserId User, int Profile), Ledger> _ledgers = new();

    private sealed class Ledger(string name, double start)
    {
        public string Name = name;
        public readonly double Start = start;
        public double End = start;
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        _balances.ActiveBalanceChanged += OnActiveBalanceChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _balances.ActiveBalanceChanged -= OnActiveBalanceChanged;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ledgers.Clear();
    }

    private void OnActiveBalanceChanged(ICommonSession session)
    {
        var rules = QueryActiveRules();
        if (!rules.MoveNext(out _, out _, out _, out _))
            return;

        if (_balances.GetActiveProfileId(session.UserId) is not { } profile
            || !_resources.TryGetResource(session, Credits, out var balance))
            return;

        var name = session.AttachedEntity is { } body ? Name(body) : session.Name;
        var key = (session.UserId, profile);

        // The first balance seen for a character this round is what it started with.
        if (!_ledgers.TryGetValue(key, out var ledger))
        {
            _ledgers[key] = new Ledger(name, balance.Value);
            return;
        }

        ledger.End = balance.Value;
        if (session.AttachedEntity != null)
            ledger.Name = name;
    }

    protected override void AppendRoundEndText(EntityUid uid,
        SerenityAdventureRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        if (_ledgers.Count == 0)
            return;

        args.AddLine(Loc.GetString("serenity-adventure-list-start"));

        var results = _ledgers.Values
            .Select(l => (l.Name, Profit: l.End - l.Start))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var (name, profit) in results)
        {
            args.AddLine(Loc.GetString(profit < 0 ? "serenity-adventure-list-loss" : "serenity-adventure-list-profit",
                ("name", name),
                ("amount", Format(Math.Abs(profit)))));
        }

        var earners = results.Where(r => r.Profit > 0)
            .OrderByDescending(r => r.Profit)
            .Take(component.TopCount)
            .ToList();

        if (earners.Count == 0)
            return;

        args.AddLine(string.Empty);
        args.AddLine(Loc.GetString("serenity-adventure-top-earners"));
        for (var i = 0; i < earners.Count; i++)
        {
            args.AddLine(Loc.GetString("serenity-adventure-top-earner",
                ("rank", i + 1),
                ("name", earners[i].Name),
                ("amount", Format(earners[i].Profit))));
        }
    }

    private static string Format(double amount)
    {
        return amount.ToString("N0", CultureInfo.InvariantCulture);
    }
}
