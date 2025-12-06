using Godot;
using System.Collections.Generic;

public partial class WeaponPickup : Area3D
{
	private static readonly List<WeaponPickup> _allPickups = new();
	private static bool _subscribedToRoundChange;

	[Export] public WeaponDefinition Weapon { get; set; }
	[Export] public int BonusReserve { get; set; } = 0;
	[Export] public bool AutoPickup { get; set; } = true;

	private bool _collected;
	private Node3D _parentNode;

	public override void _Ready()
	{
		base._Ready();
		BodyEntered += OnBodyEntered;
		_parentNode = GetParentOrNull<Node3D>();
		_allPickups.Add(this);

		CallDeferred(nameof(DeferredSubscribe));
	}

	private void DeferredSubscribe()
	{
		SubscribeToRoundChange();
	}

	private static void SubscribeToRoundChange()
	{
		if (_subscribedToRoundChange)
			return;

		var matchClient = MatchStateClient.Instance;
		if (matchClient != null)
		{
			matchClient.RoundChanged += OnRoundChanged;
			_subscribedToRoundChange = true;
			GD.Print("[WeaponPickup] Subscribed to RoundChanged event");
		}
	}

	private static void OnRoundChanged(int roundNumber)
	{
		GD.Print($"[WeaponPickup] RoundChanged to {roundNumber}, respawning pickups");
		RespawnAll();
	}

	public override void _ExitTree()
	{
		_allPickups.Remove(this);
		if (_allPickups.Count == 0 && _subscribedToRoundChange)
		{
			var matchClient = MatchStateClient.Instance;
			if (matchClient != null)
			{
				matchClient.RoundChanged -= OnRoundChanged;
			}
			_subscribedToRoundChange = false;
		}
	}

	private void OnBodyEntered(Node body)
	{
		if (!AutoPickup || _collected)
			return;
		TryGiveTo(body as PlayerCharacter);
	}

	public bool TryGiveTo(PlayerCharacter player)
	{
		if (Weapon == null || player == null || _collected)
			return false;

		var inventory = player.GetNodeOrNull<WeaponInventory>("WeaponInventory");
		if (inventory == null)
			return false;

		var existing = inventory.Get(Weapon.Id);
		if (existing == null)
		{
			if (!inventory.CanAddWeapon)
				return false;

			inventory.AddOrReplace(Weapon, Weapon.MagazineSize, Weapon.MaxReserveAmmo + BonusReserve, equip: true);
		}
		else
		{
			existing.AddAmmo(Weapon.MaxReserveAmmo + BonusReserve);
			inventory.EmitAmmo();
		}

		SetCollected(true);
		return true;
	}

	private void SetCollected(bool collected)
	{
		_collected = collected;
		if (_parentNode != null)
		{
			_parentNode.Visible = !collected;
			_parentNode.SetDeferred("process_mode", (int)(collected ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit));
			if (_parentNode is RigidBody3D rb)
			{
				rb.SetDeferred("freeze", collected);
				foreach (var child in rb.GetChildren())
				{
					if (child is CollisionShape3D col)
						col.SetDeferred("disabled", collected);
				}
			}
		}
		foreach (var child in GetChildren())
		{
			if (child is CollisionShape3D col)
				col.SetDeferred("disabled", collected);
		}
		SetDeferred("monitoring", !collected);
	}

	public static void RespawnAll()
	{
		foreach (var pickup in _allPickups)
		{
			if (pickup != null && GodotObject.IsInstanceValid(pickup))
			{
				pickup.Respawn();
			}
		}
		GD.Print($"[WeaponPickup] Respawned {_allPickups.Count} pickups");
	}

	private void Respawn()
	{
		_collected = false;
		if (_parentNode != null)
		{
			_parentNode.Visible = true;
			_parentNode.ProcessMode = ProcessModeEnum.Inherit;
			if (_parentNode is RigidBody3D rb)
			{
				rb.SetDeferred("freeze", false);
				foreach (var child in rb.GetChildren())
				{
					if (child is CollisionShape3D col)
						col.SetDeferred("disabled", false);
				}
			}
		}
		foreach (var child in GetChildren())
		{
			if (child is CollisionShape3D col)
				col.SetDeferred("disabled", false);
		}
		SetDeferred("monitoring", true);
		GD.Print($"[WeaponPickup] Respawned: {Name}, parent visible: {_parentNode?.Visible}");
	}
}
