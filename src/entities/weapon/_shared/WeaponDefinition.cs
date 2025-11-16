using Godot;

public partial class WeaponDefinition : Resource
{
	[Export] public WeaponType Id { get; set; } = WeaponType.RocketLauncher;
	[Export] public string DisplayName { get; set; } = "Rocket Launcher";
	[Export] public PackedScene? WorldScene { get; set; }
	[Export] public PackedScene? ViewScene { get; set; }
	[Export] public PackedScene? ProjectileScene { get; set; }
	[Export] public ProjectileDefinition? ProjectileConfig { get; set; }
	[Export] public Transform3D ProjectileSpawn { get; set; } = Transform3D.Identity;
	[Export] public int MagazineSize { get; set; } = 1;
	[Export] public int MaxReserveAmmo { get; set; } = 0;
	[Export] public float FireCooldownSec { get; set; } = 0.5f;
	[Export] public float ReloadDurationSec { get; set; } = 1.0f;
	[Export] public float Damage { get; set; } = 10.0f;
	[Export] public bool IsAutomatic { get; set; } = false;
	[Export] public bool ConsumeAmmoPerShot { get; set; } = true;
	[Export] public bool AllowHoldToFire { get; set; } = true;
	[Export] public float EquipTimeSec { get; set; } = 0.2f;
	[Export] public float UnequipTimeSec { get; set; } = 0.2f;
	[Export] public int ProjectilePoolPrewarm { get; set; } = 4;
	[Export] public RecoilProfile? Recoil { get; set; }
	[Export] public MuzzleFxSet? MuzzleFx { get; set; }
	[Export] public WeaponAudioSet? FireAudio { get; set; }
	[Export] public WeaponAudioSet? ReloadAudio { get; set; }
	[Export] public WeaponAudioSet? DryFireAudio { get; set; }
	[Export] public Godot.Collections.Array<AttachmentSlotDefinition> AttachmentSlots { get; set; } = new();

	public bool HasProjectile => ProjectileScene != null;
}
