using Godot;
using System.Collections.Generic;

public partial class WeaponInventory : Node
{
	[Signal] public delegate void EquippedChangedEventHandler(WeaponType weapon);
	[Signal] public delegate void AmmoChangedEventHandler(WeaponType weapon, int magazine, int capacity, bool reloading, double reloadMs);

	[Export] public Godot.Collections.Array<WeaponDefinition> StartingWeapons { get; set; } = new();

	private readonly Dictionary<WeaponType, WeaponInstance> _weapons = new();
	private WeaponInstance _equipped;
	private WeaponType _lastEquippedType = WeaponType.None;

	public WeaponInstance Equipped => _equipped;
	public WeaponType EquippedType => _equipped?.Definition?.Id ?? WeaponType.None;

	public override void _Ready()
	{
		base._Ready();
		foreach (var def in StartingWeapons)
		{
			if (def == null) continue;
			AddOrReplace(def, def.MagazineSize, def.MaxReserveAmmo, equip: _equipped == null);
		}
	}

	public WeaponInstance Get(WeaponType type)
	{
		_weapons.TryGetValue(type, out var instance);
		return instance;
	}

	public bool TryGetEquipped(out WeaponInstance instance)
	{
		instance = _equipped;
		return instance != null;
	}

	public WeaponInstance AddOrReplace(WeaponDefinition def, int? magazineOverride = null, int? reserveOverride = null, bool equip = false)
	{
		if (def == null)
			return null;

		var previousType = _equipped?.Definition?.Id ?? WeaponType.None;
		var instance = new WeaponInstance(
			def,
			magazineOverride ?? def.MagazineSize,
			reserveOverride ?? def.MaxReserveAmmo);
		_weapons[def.Id] = instance;

		if (equip || _equipped == null)
		{
			_lastEquippedType = previousType;
			_equipped = instance;
			EmitSignal(SignalName.EquippedChanged, (int)def.Id);
			EmitAmmo();
		}
		return instance;
	}

	public bool Equip(WeaponType type)
	{
		if (!_weapons.TryGetValue(type, out var instance) || instance == null)
			return false;

		if (_equipped == instance)
			return true;

		var previousType = _equipped?.Definition?.Id ?? WeaponType.None;
		_lastEquippedType = previousType;
		_equipped = instance;
		EmitSignal(SignalName.EquippedChanged, (int)type);
		EmitAmmo();
		return true;
	}

	public void AddAmmo(WeaponType type, int amount)
	{
		if (amount == 0 || !_weapons.TryGetValue(type, out var instance) || instance == null)
			return;

		instance.AddAmmo(amount);
		EmitAmmo();
	}

	public bool TogglePrevious()
	{
		if (_lastEquippedType == WeaponType.None)
			return false;
		return Equip(_lastEquippedType);
	}

	public void EmitAmmo()
	{
		if (_equipped?.Definition == null)
			return;
		EmitSignal(
			SignalName.AmmoChanged,
			(int)_equipped.Definition.Id,
			_equipped.Magazine,
			_equipped.Definition.MagazineSize,
			_equipped.IsReloading,
			_equipped.IsReloading ? _equipped.ReloadEndTimeMs - Time.GetTicksMsec() : 0.0
		);
	}
}
