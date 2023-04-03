using Content.Shared.Damage;

namespace Content.Server.Hippie.DestructOnHit.Components;

[RegisterComponent]
public sealed class DestructOnHitComponent : Component
{
    [DataField("shouldBreak")]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool ShouldBreak;

    [DataField("shouldStun")]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool ShouldStun;

    [DataField("stunTime")]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan StunTime = TimeSpan.FromSeconds(1);

    [DataField("damage", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier Damage = default!;
}
