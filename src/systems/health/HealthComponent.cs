using System;
using Godot;

public partial class HealthComponent : Node
{
	[Export] public int MaxHealth { get; set; } = 100;
	[Export] public int MaxArmor { get; set; } = 100;

	public int Health => _health;
	public int Armor => _armor;
	public bool IsDead => _isDead;
	public long LastHitByPeerId { get; private set; }
	public WeaponType LastHitWeapon { get; private set; } = WeaponType.None;

	public event Action<int> HealthChanged;
	public event Action<int> ArmorChanged;
	public event Action<long> Died;
	public event Action Revived;

	private int _health = 100;
	private int _armor = 100;
	private bool _isDead;

	public override void _Ready()
	{
		ClampVitals();
	}

	public void Initialize(int maxHealth, int maxArmor)
	{
		MaxHealth = maxHealth;
		MaxArmor = maxArmor;
		ResetVitals(notify: false);
	}

	public void ApplyDamage(int amount, long instigatorPeerId = 0, WeaponType weaponType = WeaponType.None)
	{
		if (amount <= 0 || _isDead)
			return;

		if (instigatorPeerId != 0)
			LastHitByPeerId = instigatorPeerId;
		LastHitWeapon = weaponType;

		var remaining = amount;
		if (_armor > 0)
		{
			var armorDamage = Mathf.Min(_armor, remaining);
			SetArmorInternal(_armor - armorDamage, true, true);
			remaining -= armorDamage;
		}

		if (remaining > 0)
		{
			SetHealthInternal(_health - remaining, true, true);
		}
	}

	public void SetHealth(int value)
	{
		SetHealthInternal(value, true, true);
	}

	public void SetArmor(int value)
	{
		SetArmorInternal(value, true, true);
	}

	public void SetHealthFromReplication(int value)
	{
		SetHealthInternal(value, true, false);
	}

	public void SetArmorFromReplication(int value)
	{
		SetArmorInternal(value, true, false);
	}

	public void SetFromReplication(int health, int armor)
	{
		SetHealthInternal(health, true, false);
		SetArmorInternal(armor, true, false);
	}

	public void ResetVitals(bool notify = true)
	{
		var wasDead = _isDead;
		SetHealthInternal(MaxHealth, notify, false);
		SetArmorInternal(MaxArmor, notify, false);
		LastHitByPeerId = 0;
		LastHitWeapon = WeaponType.None;
		_isDead = false;
		if (notify && wasDead)
			Revived?.Invoke();
	}

	private void SetHealthInternal(int value, bool emitChangeEvents, bool emitDeathEvent)
	{
		var clamped = Mathf.Clamp(value, 0, MaxHealth);
		var changed = clamped != _health;
		_health = clamped;

		if (emitChangeEvents && changed)
			HealthChanged?.Invoke(_health);

		UpdateDeathState(emitDeathEvent);
	}

	private void SetArmorInternal(int value, bool emitChangeEvents, bool emitDeathEvent)
	{
		var clamped = Mathf.Clamp(value, 0, MaxArmor);
		var changed = clamped != _armor;
		_armor = clamped;

		if (emitChangeEvents && changed)
			ArmorChanged?.Invoke(_armor);

		UpdateDeathState(emitDeathEvent);
	}

	private void UpdateDeathState(bool emitDeathEvent)
	{
		var newDead = _health <= 0 && _armor <= 0;
		if (!_isDead && newDead && emitDeathEvent)
		{
			_isDead = true;
			Died?.Invoke(LastHitByPeerId);
		}
		else
		{
			_isDead = newDead;
		}
	}

	private void ClampVitals()
	{
		_health = Mathf.Clamp(_health, 0, MaxHealth);
		_armor = Mathf.Clamp(_armor, 0, MaxArmor);
		_isDead = _health <= 0 && _armor <= 0;
	}
}
