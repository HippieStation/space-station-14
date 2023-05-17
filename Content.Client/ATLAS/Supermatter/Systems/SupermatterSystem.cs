using Robust.Shared.GameStates;
using Content.Shared.ATLAS.Supermatter.Components;
using Content.Shared.ATLAS.Supermatter.EntitySystems;

namespace Content.Client.ATLAS.Supermatter.EntitySystems;

public sealed class SupermatterSystem : SharedSupermatterSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupermatterComponent, ComponentHandleState>(HandleSupermatterState);
    }

    private void HandleSupermatterState(EntityUid uid, SupermatterComponent comp, ref ComponentHandleState args)
    {
        if (args.Current is not SupermatterComponentState state)
            return;
    }
}
