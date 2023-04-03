using Content.Server.Hippie.DestructOnHit.Components;
using Content.Server.Stunnable;
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server.Hippie.DestructOnHit.Systems;

public sealed class DestructOnHitSystem : EntitySystem
{

    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly StunSystem _stunSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DestructOnHitComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, DestructOnHitComponent comp, MeleeHitEvent args)
    {
        foreach (var entity in args.HitEntities)
        {
            if (args.User == entity)
            {
                continue;
            }

            if (comp.ShouldStun)
            {
                _stunSystem.TryKnockdown(entity, comp.StunTime, true);
            }

            if (comp.ShouldBreak)
            {
                _damageableSystem.TryChangeDamage(uid, comp.Damage);
            }
        }
    }
}
