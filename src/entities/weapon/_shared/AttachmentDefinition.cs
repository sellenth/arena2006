using Godot;

public enum AttachmentSlot
{
	Muzzle,
	Optic,
	Underbarrel,
	Skin,
	Utility
}

public partial class AttachmentDefinition : Resource
{
	[Export] public string Id { get; set; } = string.Empty;
	[Export] public string DisplayName { get; set; } = string.Empty;
	[Export] public AttachmentSlot Slot { get; set; } = AttachmentSlot.Muzzle;
	[Export] public float DamageMultiplier { get; set; } = 1.0f;
	[Export] public float FireRateMultiplier { get; set; } = 1.0f;
	[Export] public float ReloadMultiplier { get; set; } = 1.0f;
	[Export] public float SpreadDelta { get; set; } = 0.0f;
	[Export] public float RecoilDelta { get; set; } = 0.0f;
	[Export] public PackedScene? CosmeticScene { get; set; }
}

public partial class AttachmentSlotDefinition : Resource
{
	[Export] public AttachmentSlot Slot { get; set; } = AttachmentSlot.Muzzle;
	[Export] public Godot.Collections.Array<string> AllowedAttachmentIds { get; set; } = new();
}
