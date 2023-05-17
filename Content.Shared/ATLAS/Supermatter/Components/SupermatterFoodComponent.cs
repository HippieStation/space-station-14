namespace Content.Shared.ATLAS.Supermatter.Components;

/// <summary>
/// Overrides exactly how much energy this object gives to Supermatter.
/// </summary>
[RegisterComponent]
public sealed class SupermatterFoodComponent : Component
{

    [ViewVariables(VVAccess.ReadWrite)]

    [DataField("energy")]
    public int Energy { get; set; } = 1;
}

