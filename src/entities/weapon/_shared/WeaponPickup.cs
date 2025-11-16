using Godot;

public partial class WeaponPickup : Area3D
{
	[Export] public WeaponDefinition Weapon { get; set; }
	[Export] public int BonusReserve { get; set; } = 0;
	[Export] public bool AutoPickup { get; set; } = true;

	public override void _Ready()
	{
		base._Ready();
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node body)
	{
		if (!AutoPickup)
			return;
		TryGiveTo(body as PlayerCharacter);
	}

	public bool TryGiveTo(PlayerCharacter player)
	{
		if (Weapon == null || player == null)
			return false;

		var inventory = player.GetNodeOrNull<WeaponInventory>("WeaponInventory");
		if (inventory == null)
			return false;

		var existing = inventory.Get(Weapon.Id);
		if (existing == null)
		{
			inventory.AddOrReplace(Weapon, Weapon.MagazineSize, Weapon.MaxReserveAmmo + BonusReserve, equip: true);
		}
		else
		{
			existing.AddAmmo(Weapon.MaxReserveAmmo + BonusReserve);
			inventory.EmitAmmo();
		}

		QueueFree();
		return true;
	}
}
