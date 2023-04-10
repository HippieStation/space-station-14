namespace Content.Server.Hippie.DeathGasp.Components;

[RegisterComponent]
public sealed class DeathGaspComponent : Component
{
    public readonly string[] DeathGaspMessages =
    {
        "death-gasp-high",
        "death-gasp-medium",
        "death-gasp-normal"
    };
}
