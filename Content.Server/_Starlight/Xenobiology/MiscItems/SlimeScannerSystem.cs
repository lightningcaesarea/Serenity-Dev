using Content.Server._Serenity.Xenobiology; // Serenity
using Content.Server.Chat.Managers;
using Content.Shared._Starlight.Xenobiology;
using Content.Shared._Starlight.Xenobiology.MiscItems;
using Content.Shared.Chat;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Player;

namespace Content.Server._Starlight.Xenobiology.MiscItems;

public sealed partial class SlimeScannerSystem : EntitySystem
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private HungerSystem _hungerSystem = default!;
    [Dependency] private SlimeTemperamentSystem _temperament = default!; // Serenity

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeScannerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<XenobiologyConsoleComponent, ConsoleMsgToScannerEvent>(OnConsoleMsgToScanner);
    }

    private void OnAfterInteract(Entity<SlimeScannerComponent> entity, ref AfterInteractEvent args)
    {
        if (!_entityManager.TryGetComponent<ActorComponent>(args.User, out var actor)) return;
        if (!_entityManager.TryGetComponent<SlimeComponent>(args.Target, out var slime)) return;
        var metaData = MetaData(args.Target.Value);
        if (!_entityManager.TryGetComponent<HungerComponent>(args.Target, out var hunger)) return;

        SendInformation(actor, args.Target.Value, slime, metaData, hunger); // Serenity: pass the slime
        RaiseNetworkEvent(new SlimeScannerSoundMessage()
        {
            Owner = GetNetEntity(entity.Owner, MetaData(entity.Owner)),
            User = GetNetEntity(args.User, MetaData(args.User)),
        });
    }

    private void OnConsoleMsgToScanner(Entity<XenobiologyConsoleComponent> entity, ref ConsoleMsgToScannerEvent args)
    {
        if (!_entityManager.TryGetComponent<ActorComponent>(args.User, out var actor)) return;
        if (!_entityManager.TryGetComponent<SlimeComponent>(args.Target, out var slime)) return;
        var metaData = MetaData(args.Target);
        if (!_entityManager.TryGetComponent<HungerComponent>(args.Target, out var hunger)) return;

        SendInformation(actor, args.Target, slime, metaData, hunger); // Serenity: pass the slime

        args.Handled = true;
    }

    private void SendInformation(ActorComponent actor, EntityUid uid, SlimeComponent slime, MetaDataComponent metaData, HungerComponent hunger) // Serenity: uid
    {
        var channel = actor.PlayerSession.Channel;
        var name = metaData.EntityName;
        var nutrition = FixedPoint2.New(_hungerSystem.GetHunger(hunger));
        var message = $"Name:\t[Bold]{name}[/Bold]\nNutrition:\t[Bold]{nutrition}[/Bold]\nMutation Chance:\t[Bold]{slime.MutationChance * 100F}%[/Bold]";
        message += $"\nMood:\t[Bold]{_temperament.GetMoodText(uid)}[/Bold]"; // Serenity
        _chatManager.ChatMessageToOne(ChatChannel.Local, message, message, EntityUid.Invalid, false, channel);
    }
}
