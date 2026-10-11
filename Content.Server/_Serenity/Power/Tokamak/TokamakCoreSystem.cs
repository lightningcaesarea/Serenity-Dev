using System.Linq;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Serenity.Power.Tokamak;
using Content.Shared.Audio;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.Emp;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Radiation.Components;
using Content.Shared.Radio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Power.Tokamak;

/// <summary>
/// Simulates the plasma of tokamak cores: heating, fuelling, fusion reactions, instability and power output.
/// </summary>
/// <remarks>
/// Mechanics are an original implementation inspired by the R-UST / INDRA tokamaks of other space station games.
/// </remarks>
public sealed partial class TokamakCoreSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedEmpSystem _emp = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private static readonly TimeSpan TickLength = TimeSpan.FromSeconds(1);

    /// <summary>Reactants that are fusion ash. They vent out of the plasma and crowd it if they build up.</summary>
    private static readonly HashSet<string> AshReactants = ["helium4"];

    private const float AshVentFraction = 0.03f;
    private const float AshCrowdingThreshold = 100f;
    private const float BaseInstabilityDecay = 0.6f;
    private const float RadiationDecay = 0.2f;
    private const float MaxPlasmaTemperature = 50_000_000f;
    private const float AmbientTemperature = 300f;

    private List<FusionReactionPrototype> _reactions = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TokamakCoreComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        CacheReactions();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<FusionReactionPrototype>())
            CacheReactions();
    }

    private void CacheReactions()
    {
        _reactions = _proto.EnumeratePrototypes<FusionReactionPrototype>()
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.ID)
            .ToList();
    }

    private void OnMapInit(Entity<TokamakCoreComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + TickLength;
        UpdateAppearance(ent);
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<TokamakCoreComponent>();
        while (query.MoveNext(out var uid, out var core))
        {
            if (curTime < core.NextUpdate)
                continue;

            core.NextUpdate = curTime + TickLength;
            Step((uid, core), (float) TickLength.TotalSeconds);
        }
    }

    #region Controls

    public bool SetActive(Entity<TokamakCoreComponent> ent, bool active, EntityUid? user = null)
    {
        if (ent.Comp.Active == active)
            return false;

        if (active && _timing.CurTime < ent.Comp.ScramLockedUntil)
        {
            if (user != null)
            {
                var remaining = ent.Comp.ScramLockedUntil - _timing.CurTime;
                _popup.PopupEntity(Loc.GetString("tokamak-core-scram-lockout", ("seconds", (int) Math.Ceiling(remaining.TotalSeconds))), ent.Owner, user.Value);
            }

            return false;
        }

        if (active && !_receiver.IsPowered(ent.Owner))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("tokamak-core-no-power"), ent.Owner, user.Value);

            return false;
        }

        ent.Comp.Active = active;
        _audio.PlayPvs(active ? ent.Comp.StartSound : ent.Comp.ShutdownSound, ent.Owner);
        _ambient.SetAmbience(ent.Owner, active);

        if (!active)
            Vent(ent);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):user} switched the tokamak field of {ToPrettyString(ent.Owner):core} {(active ? "on" : "off")}");

        UpdateAppearance(ent);
        return true;
    }

    public void SetFieldStrength(Entity<TokamakCoreComponent> ent, int strength, EntityUid? user = null)
    {
        var clamped = Math.Clamp(strength, ent.Comp.MinFieldStrength, ent.Comp.MaxFieldStrength);
        if (clamped == ent.Comp.FieldStrength)
            return;

        ent.Comp.FieldStrength = clamped;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):user} set the field strength of {ToPrettyString(ent.Owner):core} to {clamped}");
    }

    /// <summary>
    /// Emergency shutdown. The field is dumped, which irradiates the area in proportion to the plasma lost,
    /// but the plasma is settled at once, so instability drops to zero. The field magnets are then locked out for a while.
    /// A normal stop leaves instability to decay and can be restarted straight away.
    /// </summary>
    public void Scram(Entity<TokamakCoreComponent> ent, EntityUid? user = null)
    {
        if (!ent.Comp.Active)
            return;

        ent.Comp.RadiationLevel += Total(ent.Comp.Reactants) * 0.1f;
        ent.Comp.Instability = 0f;
        ent.Comp.ScramLockedUntil = _timing.CurTime + ent.Comp.ScramLockout;
        SetActive(ent, false, user);
    }

    private void Vent(Entity<TokamakCoreComponent> ent)
    {
        ent.Comp.Reactants.Clear();
        ent.Comp.PowerOutput = 0f;
        ent.Comp.PowerDraw = 0f;
        _receiver.SetLoad(ent.Owner, 0f);
        SetSupply(ent.Owner, 0f);
    }

    #endregion

    #region Simulation

    private void Step(Entity<TokamakCoreComponent> ent, float dt)
    {
        var core = ent.Comp;

        if (!core.Active)
        {
            Cool(core, dt);
            ProcessRadiation(ent);
            UpdateAppearance(ent);
            return;
        }

        if (!_receiver.IsPowered(ent.Owner))
        {
            // The field collapses without power. Everything in it is lost, which is messy but not catastrophic.
            core.RadiationLevel += Total(core.Reactants) * 0.05f;
            core.Reactants.Clear();
            core.PlasmaTemperature = MathF.Max(AmbientTemperature, core.PlasmaTemperature * 0.7f);
            core.PowerOutput = 0f;
            core.PowerDraw = 0f;
            SetSupply(ent.Owner, 0f);
            DecayInstability(core, dt, 2f);
            ProcessRadiation(ent);
            UpdateAppearance(ent);
            return;
        }

        core.PowerDraw = core.FieldStrength * core.DrawPerFieldStrength;
        _receiver.SetLoad(ent.Owner, core.PowerDraw);

        var coreXform = Transform(ent.Owner);
        var corePos = _transform.GetWorldPosition(coreXform);

        var instabilityGain = MathF.Pow(core.FieldStrength, 1.1f) / 200f;

        ApplyGyrotrons(core, coreXform.MapID, corePos);
        ApplyInjectors(core, coreXform.MapID, corePos);
        instabilityGain += React(core, dt);
        instabilityGain += Disturbances(ent, coreXform);

        // Ash crowds the plasma and makes it harder to hold.
        var ash = 0f;
        foreach (var reactant in AshReactants)
        {
            if (core.Reactants.TryGetValue(reactant, out var amount))
            {
                ash += amount;
                core.Reactants[reactant] = amount * (1f - AshVentFraction);
            }
        }

        if (ash > AshCrowdingThreshold)
            instabilityGain += (ash - AshCrowdingThreshold) * 0.01f;

        // Plasma that is far too hot is hard to hold.
        if (core.PlasmaTemperature > core.HotPlasmaTemperature)
            instabilityGain += (core.PlasmaTemperature - core.HotPlasmaTemperature) / core.HotPlasmaTemperature * 0.5f;

        core.PlasmaTemperature = Math.Clamp(core.PlasmaTemperature * (1f - core.HeatLossFraction), AmbientTemperature, MaxPlasmaTemperature);
        core.Instability = MathF.Max(0f, core.Instability + instabilityGain * dt);
        DecayInstability(core, dt, 1f);

        // Output follows plasma temperature, but only while there is fuel in the field.
        var output = 0f;
        if (Total(core.Reactants) >= 1f)
        {
            output = core.OutputCoefficient * MathF.Pow(core.PlasmaTemperature, core.OutputExponent)
                     * (1f + core.FieldStrength / 60f);
            output = Math.Clamp(output, 0f, core.MaxOutput);
        }

        core.PowerOutput = output;
        SetSupply(ent.Owner, output);

        ProcessRadiation(ent);
        CheckWarnings(ent);

        if (core.Instability >= core.RuptureInstability)
            Rupture(ent);

        UpdateAppearance(ent);
    }

    private void Cool(TokamakCoreComponent core, float dt)
    {
        core.PlasmaTemperature = MathF.Max(AmbientTemperature, core.PlasmaTemperature * 0.8f);
        DecayInstability(core, dt, 3f);
        core.AnnouncedStage = 0;
    }

    private static void DecayInstability(TokamakCoreComponent core, float dt, float multiplier)
    {
        core.Instability = MathF.Max(0f, core.Instability - BaseInstabilityDecay * multiplier * dt);
    }

    private void ApplyGyrotrons(TokamakCoreComponent core, MapId map, Vector2 corePos)
    {
        var query = EntityQueryEnumerator<TokamakGyrotronComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gyrotron, out var xform))
        {
            if (!gyrotron.Active || xform.MapID != map || !_receiver.IsPowered(uid))
                continue;

            var pos = _transform.GetWorldPosition(xform);
            var rot = _transform.GetWorldRotation(xform);
            if (!TokamakBeam.IsAligned(pos, rot, corePos, gyrotron.Range))
                continue;

            core.PlasmaTemperature += gyrotron.Rate * gyrotron.MegaEnergy * gyrotron.HeatPerMegaEnergy;
        }
    }

    private void ApplyInjectors(TokamakCoreComponent core, MapId map, Vector2 corePos)
    {
        var query = EntityQueryEnumerator<TokamakFuelInjectorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var injector, out var xform))
        {
            if (!injector.Active || xform.MapID != map || !_receiver.IsPowered(uid))
                continue;

            var pos = _transform.GetWorldPosition(xform);
            var rot = _transform.GetWorldRotation(xform);
            if (!TokamakBeam.IsAligned(pos, rot, corePos, injector.Range))
                continue;

            var rodUid = _slots.GetItemOrNull(uid, injector.RodSlotId);
            if (rodUid == null || !TryComp<TokamakFuelRodComponent>(rodUid, out var rod))
                continue;

            var room = core.MaxReactants - Total(core.Reactants);
            var budget = MathF.Min(injector.Rate, room);
            if (budget <= 0f)
                continue;

            var available = Total(rod.Reactants);
            if (available <= 0f)
                continue;

            var scale = MathF.Min(1f, budget / available);
            foreach (var (reactant, amount) in rod.Reactants.ToList())
            {
                var moved = amount * scale;
                if (moved <= 0f)
                    continue;

                rod.Reactants[reactant] = amount - moved;
                core.Reactants[reactant] = core.Reactants.GetValueOrDefault(reactant) + moved;
            }
        }
    }

    /// <summary>
    /// Runs every reaction that has the reactants and temperature for it.
    /// </summary>
    /// <returns>Instability gained this tick.</returns>
    private float React(TokamakCoreComponent core, float dt)
    {
        var instability = 0f;
        var fieldRate = 1f + core.FieldStrength / 50f;

        foreach (var reaction in _reactions)
        {
            if (core.PlasmaTemperature < reaction.MinTemperature)
                continue;

            core.Reactants.TryGetValue(reaction.ReactantA, out var a);
            core.Reactants.TryGetValue(reaction.ReactantB, out var b);

            var same = reaction.ReactantA == reaction.ReactantB;
            var limit = same ? a / 2f : MathF.Min(a, b);
            var amount = MathF.Min(reaction.Rate * fieldRate * dt, limit);
            if (amount <= 0.001f)
                continue;

            core.Reactants[reaction.ReactantA] = a - amount * (same ? 2f : 1f);
            if (!same)
                core.Reactants[reaction.ReactantB] = b - amount;

            foreach (var (product, yield) in reaction.Products)
            {
                core.Reactants[product] = core.Reactants.GetValueOrDefault(product) + amount * yield;
            }

            core.PlasmaTemperature += reaction.HeatPerUnit * amount;
            core.RadiationLevel += reaction.Radiation * amount;
            instability += reaction.Instability * amount;
        }

        // Cap storage so products cannot grow without bound.
        var total = Total(core.Reactants);
        if (total > core.MaxReactants)
        {
            var scale = core.MaxReactants / total;
            foreach (var key in core.Reactants.Keys.ToList())
            {
                core.Reactants[key] *= scale;
            }
        }

        foreach (var key in core.Reactants.Keys.ToList())
        {
            if (core.Reactants[key] < 0.001f)
                core.Reactants.Remove(key);
        }

        return instability;
    }

    /// <summary>
    /// People standing next to the field disturb it.
    /// </summary>
    private float Disturbances(Entity<TokamakCoreComponent> ent, TransformComponent xform)
    {
        if (ent.Comp.PlasmaTemperature < 10_000f)
            return 0f;

        var count = _lookup.GetEntitiesInRange<MobStateComponent>(xform.Coordinates, ent.Comp.DisturbanceRange).Count;
        return count * 1.5f;
    }

    private void ProcessRadiation(Entity<TokamakCoreComponent> ent)
    {
        var core = ent.Comp;
        core.RadiationLevel = MathF.Max(0f, core.RadiationLevel * (1f - RadiationDecay));
        var source = EnsureComp<RadiationSourceComponent>(ent.Owner);
        source.Intensity = MathF.Min(core.RadiationLevel, 60f);
    }

    private void SetSupply(EntityUid uid, float watts)
    {
        if (!TryComp<PowerSupplierComponent>(uid, out var supplier))
            return;

        supplier.MaxSupply = watts;
    }

    private static float Total(Dictionary<string, float> reactants)
    {
        var total = 0f;
        foreach (var amount in reactants.Values)
        {
            total += amount;
        }

        return total;
    }

    #endregion

    #region Warnings and rupture

    private void CheckWarnings(Entity<TokamakCoreComponent> ent)
    {
        var core = ent.Comp;
        var stage = core.Instability switch
        {
            >= 90f => 3,
            >= 75f => 2,
            >= 50f => 1,
            _ => 0,
        };

        if (stage > core.AnnouncedStage)
        {
            core.AnnouncedStage = stage;
            var channel = _proto.Index<RadioChannelPrototype>(core.EngineeringChannel);
            _radio.SendRadioMessage(ent.Owner,
                Loc.GetString($"tokamak-core-warning-{stage}", ("core", ent.Owner), ("instability", MathF.Round(core.Instability))),
                channel,
                ent.Owner);
        }
        else if (stage < core.AnnouncedStage && core.Instability < 40f)
        {
            core.AnnouncedStage = stage;
        }

        if (stage >= 1 && _timing.CurTime >= core.NextWarning)
        {
            core.NextWarning = _timing.CurTime + TimeSpan.FromSeconds(stage >= 2 ? 2 : 5);
            _audio.PlayPvs(core.WarningSound, ent.Owner);
        }
    }

    private void Rupture(Entity<TokamakCoreComponent> ent)
    {
        var core = ent.Comp;
        var loss = Total(core.Reactants);

        _adminLog.Add(LogType.Explosion, LogImpact.Extreme,
            $"The field of {ToPrettyString(ent.Owner):core} ruptured at {core.PlasmaTemperature:0} K with {loss:0} units of plasma");

        var channel = _proto.Index<RadioChannelPrototype>(core.EngineeringChannel);
        _radio.SendRadioMessage(ent.Owner, Loc.GetString("tokamak-core-rupture", ("core", ent.Owner)), channel, ent.Owner);

        _audio.PlayPvs(core.RuptureSound, ent.Owner);

        var intensity = Math.Clamp(150f + loss + core.PlasmaTemperature / 20_000f, 150f, 600f);
        _explosion.QueueExplosion(ent.Owner, "Radioactive", intensity, 3f, 40f, canCreateVacuum: false);
        _emp.EmpPulse(_transform.GetMapCoordinates(ent.Owner), 12f, 50_000f, TimeSpan.FromSeconds(30));

        core.RadiationLevel += loss;
        core.Reactants.Clear();
        core.PlasmaTemperature = AmbientTemperature;
        core.Instability = core.RuptureInstability * 0.5f;
        core.AnnouncedStage = 0;
        core.PowerOutput = 0f;
        core.Active = false;
        _ambient.SetAmbience(ent.Owner, false);
        SetSupply(ent.Owner, 0f);
        _receiver.SetLoad(ent.Owner, 0f);
    }

    #endregion

    private void UpdateAppearance(Entity<TokamakCoreComponent> ent)
    {
        var state = !ent.Comp.Active
            ? TokamakCoreState.Off
            : ent.Comp.Instability >= 50f
                ? TokamakCoreState.Unstable
                : TokamakCoreState.On;

        _appearance.SetData(ent.Owner, TokamakVisuals.CoreState, state);
    }
}
