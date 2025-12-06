using Godot;

public partial class ProjectileDefinition : Resource
{
	[Export] public float Speed { get; set; } = 30.0f;
	[Export] public float LifetimeSec { get; set; } = 6.0f;
	[Export] public float GravityScale { get; set; } = 0.0f;
	[Export] public float ExplosionRadius { get; set; } = 0.0f;
	[Export] public float Damage { get; set; } = 100.0f;
	[Export] public float ArmDelaySec { get; set; } = 0.0f;
	[Export] public float SelfDamageScale { get; set; } = 1.0f;
	[Export] public float KnockbackImpulse { get; set; } = 24.0f;
	[Export] public float KnockbackUpBias { get; set; } = 0.6f;
}
