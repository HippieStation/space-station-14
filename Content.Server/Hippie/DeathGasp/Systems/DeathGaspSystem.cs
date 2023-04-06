using Content.Server.Chat.Systems;
using Content.Server.Hippie.DeathGasp.Components;
using Content.Shared.Mobs;
using Robust.Shared.Random;

namespace Content.Server.Hippie.DeathGasp.Systems;

public sealed class DeathGaspSystem : EntitySystem
{

    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DeathGaspComponent, MobStateChangedEvent>(HandleDeathState);
    }

    private void HandleDeathState(EntityUid uid, DeathGaspComponent comp, MobStateChangedEvent ev)
    {
        // Exit if the mob's state is not dead.
        if (ev.NewMobState != MobState.Dead)
            return;

        // Selects a random death-gasp message.
        var deathGaspMessage = SelectRandomDeathGaspMessage(comp);

        // Localize the message
        var localizedMessage = Loc.GetString(deathGaspMessage);

        // Send the message as an force-emote to the in-game chat.
        SendDeathGaspMessage(uid, localizedMessage);
    }

    private string SelectRandomDeathGaspMessage(DeathGaspComponent comp)
        => comp.DeathGaspMessages[_random.Next(comp.DeathGaspMessages.Length)];

    private void SendDeathGaspMessage(EntityUid uid, string message)
        => _chat.TrySendInGameICMessage(uid, message, InGameICChatType.Emote, false, force: true);
}
