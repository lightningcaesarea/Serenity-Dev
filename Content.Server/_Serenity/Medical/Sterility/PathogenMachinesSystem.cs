using Content.Server.Power.EntitySystems;
using Content.Shared._Serenity.Medical.Sterility;
using Content.Shared._Serenity.Medical.Wounds;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics.Components;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Medical.Sterility;

/// <summary>
/// The blood culture analyzer and the antibiotic synthesizer. The analyzer finds the patient a blood sample came
/// from (blood carries its owner's DNA) and prints which pathogen is infecting them. The synthesizer is programmed
/// from that report and turns broad-spectrum antibiotic into the narrow-spectrum drug for it.
/// </summary>
public sealed partial class PathogenMachinesSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private InfectionSystem _infection = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PathogenAnalyzerComponent, InteractUsingEvent>(OnAnalyzerUsed);
        SubscribeLocalEvent<PathogenAnalyzerComponent, PathogenAnalyzeDoAfterEvent>(OnAnalyzed);
        SubscribeLocalEvent<PathogenSynthesizerComponent, InteractUsingEvent>(OnSynthesizerUsed);
        SubscribeLocalEvent<PathogenSynthesizerComponent, PathogenSynthesizeDoAfterEvent>(OnSynthesized);
    }

    #region Analyzer

    private void OnAnalyzerUsed(Entity<PathogenAnalyzerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<SolutionManagerComponent>(args.Used))
            return;

        args.Handled = true;

        if (!_power.IsPowered(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("pathogen-machine-unpowered"), ent, args.User);
            return;
        }

        if (FindDna(args.Used) == null)
        {
            _popup.PopupEntity(Loc.GetString("pathogen-analyzer-no-blood"), ent, args.User);
            return;
        }

        StartDoAfter(args.User, ent, args.Used, ent.Comp.AnalyzeTime, new PathogenAnalyzeDoAfterEvent());
    }

    private void OnAnalyzed(Entity<PathogenAnalyzerComponent> ent, ref PathogenAnalyzeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } sample || !_power.IsPowered(ent.Owner))
            return;

        args.Handled = true;

        if (FindDna(sample) is not { } dna)
        {
            _popup.PopupEntity(Loc.GetString("pathogen-analyzer-no-blood"), ent, args.User);
            return;
        }

        var patient = FindPatient(dna);
        ProtoId<PathogenPrototype>? pathogen = null;
        string text;
        if (patient == null)
        {
            text = Loc.GetString("pathogen-report-inconclusive");
        }
        else if (TryComp<WoundComponent>(patient, out var wounds) && _infection.IsInfected(wounds))
        {
            pathogen = _infection.GetPathogen(wounds);
            text = pathogen is { } id && _proto.TryIndex(id, out var proto)
                ? Loc.GetString("pathogen-report-found",
                    ("pathogen", Loc.GetString(proto.Name)),
                    ("drug", _proto.Index(proto.Drug).LocalizedName))
                : Loc.GetString("pathogen-report-unknown");
        }
        else
        {
            text = Loc.GetString("pathogen-report-clear");
        }

        var report = Spawn(ent.Comp.ReportPrototype, Transform(ent).Coordinates);
        EnsureComp<PathogenReportComponent>(report).Pathogen = pathogen;
        if (TryComp<PaperComponent>(report, out var paper))
            _paper.SetContent((report, paper), text);

        _popup.PopupEntity(Loc.GetString("pathogen-analyzer-done"), ent, args.User);
    }

    private string? FindDna(EntityUid container)
    {
        foreach (var (_, solution) in _solutions.EnumerateSolutions(container))
        {
            foreach (var quantity in solution.Comp.Solution.Contents)
            {
                if (quantity.Reagent.Data == null)
                    continue;

                foreach (var data in quantity.Reagent.Data)
                {
                    if (data is DnaData dna && !string.IsNullOrEmpty(dna.DNA))
                        return dna.DNA;
                }
            }
        }

        return null;
    }

    private EntityUid? FindPatient(string dna)
    {
        var query = EntityQueryEnumerator<DnaComponent, WoundComponent>();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (comp.DNA == dna)
                return uid;
        }

        return null;
    }

    #endregion

    #region Synthesizer

    private void OnSynthesizerUsed(Entity<PathogenSynthesizerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<PathogenReportComponent>(args.Used, out var report))
        {
            args.Handled = true;

            if (!_power.IsPowered(ent.Owner))
            {
                _popup.PopupEntity(Loc.GetString("pathogen-machine-unpowered"), ent, args.User);
                return;
            }

            if (report.Pathogen is not { } pathogen)
            {
                _popup.PopupEntity(Loc.GetString("pathogen-synthesizer-bad-report"), ent, args.User);
                return;
            }

            ent.Comp.Programmed = pathogen;
            _popup.PopupEntity(Loc.GetString("pathogen-synthesizer-programmed",
                ("pathogen", Loc.GetString(_proto.Index(pathogen).Name))), ent, args.User);
            return;
        }

        if (!HasComp<SolutionManagerComponent>(args.Used))
            return;

        args.Handled = true;

        if (!_power.IsPowered(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("pathogen-machine-unpowered"), ent, args.User);
            return;
        }

        if (ent.Comp.Programmed == null)
        {
            _popup.PopupEntity(Loc.GetString("pathogen-synthesizer-not-programmed"), ent, args.User);
            return;
        }

        if (FindSource(args.Used, ent.Comp.Source) == null)
        {
            _popup.PopupEntity(Loc.GetString("pathogen-synthesizer-no-source",
                ("reagent", _proto.Index(ent.Comp.Source).LocalizedName)), ent, args.User);
            return;
        }

        StartDoAfter(args.User, ent, args.Used, ent.Comp.SynthesisTime, new PathogenSynthesizeDoAfterEvent());
    }

    private void OnSynthesized(Entity<PathogenSynthesizerComponent> ent, ref PathogenSynthesizeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } container || !_power.IsPowered(ent.Owner))
            return;

        args.Handled = true;

        if (ent.Comp.Programmed is not { } pathogenId
            || FindSource(container, ent.Comp.Source) is not { } soln)
        {
            _popup.PopupEntity(Loc.GetString("pathogen-synthesizer-not-programmed"), ent, args.User);
            return;
        }

        var drug = _proto.Index(pathogenId).Drug;
        var amount = soln.Comp.Solution.GetTotalPrototypeQuantity(ent.Comp.Source);
        var removed = _solutions.RemoveReagent(soln, ent.Comp.Source, amount);
        _solutions.TryAddReagent(soln, drug, removed, out _);

        _popup.PopupEntity(Loc.GetString("pathogen-synthesizer-done",
            ("amount", removed), ("drug", _proto.Index(drug).LocalizedName)), ent, args.User);
    }

    private Entity<SolutionComponent>? FindSource(EntityUid container, ProtoId<ReagentPrototype> source)
    {
        foreach (var (_, solution) in _solutions.EnumerateSolutions(container))
        {
            if (solution.Comp.Solution.GetTotalPrototypeQuantity(source) > FixedPoint2.Zero)
                return solution;
        }

        return null;
    }

    #endregion

    private void StartDoAfter(EntityUid user, EntityUid machine, EntityUid used, TimeSpan delay, DoAfterEvent ev)
    {
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, delay, ev, machine, target: machine, used: used)
        {
            NeedHand = true,
            BreakOnDamage = true,
            BreakOnMove = true,
            DistanceThreshold = 1.5f,
        });
    }
}
