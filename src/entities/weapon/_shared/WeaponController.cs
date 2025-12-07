using Godot;
using System;

/// <summary>
/// Main weapon controller that coordinates input, state, and delegates to specialized systems.
/// </summary>
public partial class WeaponController : Node
{
	[Export] public NodePath InventoryPath { get; set; } = "../WeaponInventory";
	[Export] public NodePath ProjectileParentPath { get; set; } = "";
	[Export] public bool IgnoreGameModeWeapons { get; set; } = false;

	private WeaponInventory _inventory;
	private PlayerCharacter _player;
	private GameModeManager _gameMode;
	private NetworkController _network;

	private const float BurstResetDelaySec = 0.35f;
	private const float BurstDecayRate = 8f;

	private WeaponState _state = WeaponState.Idle;
	private float _cooldownTimer = 0f;
	private float _reloadTimer = 0f;
	private int _fireSequence = 0;
	private float _burstSequence = 0f;
	private float _timeSinceLastShot = 0f;
	private PlayerInputState _lastInput = new PlayerInputState();
	private long _ownerPeerId = 0;

	// Specialized systems
	private WeaponFireSystem _fireSystem;
	private WeaponFxSystem _fxSystem;
	private WeaponRecoilSystem _recoilSystem;

	public override void _Ready()
	{
		_player = GetParent() as PlayerCharacter ?? GetOwner() as PlayerCharacter;
		_inventory = GetNodeOrNull<WeaponInventory>(InventoryPath);
		_gameMode = GetNodeOrNull<GameModeManager>("/root/GameModeManager");
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");
		_ownerPeerId = _network?.ClientPeerId ?? 0;
		if (_ownerPeerId == 0 && _player != null && _player.OwnerPeerId != 0)
		{
			_ownerPeerId = _player.OwnerPeerId;
		}

		InitializeSystems();
		SetPhysicsProcess(true);
	}

	private void InitializeSystems()
	{
		_fxSystem = new WeaponFxSystem { Name = "FxSystem" };
		AddChild(_fxSystem);
		_fxSystem.Initialize(_player);

		_recoilSystem = new WeaponRecoilSystem { Name = "RecoilSystem" };
		AddChild(_recoilSystem);
		_recoilSystem.Initialize(_player);

		_fireSystem = new WeaponFireSystem
		{
			Name = "FireSystem",
			ProjectileParentPath = ProjectileParentPath
		};
		AddChild(_fireSystem);
		_fireSystem.Initialize(_player, _fxSystem, _recoilSystem, _ownerPeerId);
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);
		var dt = (float)delta;
		UpdateTimers(dt);

		if (_player != null && !_player.IsPhysicsProcessing())
			return;

		if (!WeaponsAllowed())
		{
			_state = WeaponState.Disabled;
			return;
		}

		if (_state == WeaponState.Disabled)
		{
			_state = WeaponState.Idle;
		}

		if (_player != null && !_player.IsAuthority())
			return;

		RecoverRecoil(dt);
		ProcessInputFrame();
	}

	public void SetInput(PlayerInputState input)
	{
		_lastInput.CopyFrom(input);
	}

	public void ResetState()
	{
		_state = WeaponState.Idle;
		_cooldownTimer = 0f;
		_reloadTimer = 0f;
		if (_inventory != null && _inventory.TryGetEquipped(out var instance) && instance != null)
		{
			instance.CancelReload();
		}
	}

	private void UpdateTimers(float dt)
	{
		if (_cooldownTimer > 0f)
		{
			_cooldownTimer = Mathf.Max(0f, _cooldownTimer - dt);
			if (_cooldownTimer <= 0.0001f && _state == WeaponState.FiringCooldown)
			{
				_state = WeaponState.Idle;
			}
		}

		if (_reloadTimer > 0f)
		{
			_reloadTimer = Mathf.Max(0f, _reloadTimer - dt);
			if (_reloadTimer <= 0.0001f)
			{
				FinishReload();
			}
		}

		// Decay burst sequence after not firing for a moment
		_timeSinceLastShot += dt;
		if (_timeSinceLastShot > BurstResetDelaySec && _burstSequence > 0f)
		{
			_burstSequence = Mathf.Max(0f, _burstSequence - BurstDecayRate * dt);
		}
	}

	private bool WeaponsAllowed()
	{
		if (IgnoreGameModeWeapons)
			return true;

		if (_network != null && _network.IsClient)
		{
			var matchStateClient = MatchStateClient.Instance;
			if (matchStateClient != null)
				return matchStateClient.WeaponsEnabled;
		}

		if (_gameMode != null)
			return _gameMode.WeaponsEnabled;

		return true;
	}

	private void ProcessInputFrame()
	{
		if (_inventory == null || !_inventory.TryGetEquipped(out var instance) || instance?.Definition == null)
			return;

		if (_lastInput.WeaponToggle)
		{
			TryToggleWeapon();
		}

		if (_lastInput.Reload)
		{
			TryStartReload(instance);
		}

		var wantsFire = _lastInput.PrimaryFire;
		if (!instance.Definition.AllowHoldToFire)
		{
			wantsFire = _lastInput.PrimaryFireJustPressed;
		}

		if (wantsFire)
		{
			TryFire(instance);
		}
	}

	private void TryFire(WeaponInstance instance)
	{
		if (instance == null || instance.Definition == null)
			return;

		if (_state == WeaponState.Reloading || _state == WeaponState.Equipping)
			return;

		if (_cooldownTimer > 0.0001f)
			return;

		if (!instance.HasAmmoInMagazine && instance.Definition.ConsumeAmmoPerShot)
		{
			_fxSystem?.PlayAudio(instance.Definition.DryFireAudio, _player?.GlobalPosition ?? Vector3.Zero);
			return;
		}

		if (!instance.ConsumeRound())
		{
			_fxSystem?.PlayAudio(instance.Definition.DryFireAudio, _player?.GlobalPosition ?? Vector3.Zero);
			return;
		}

		_fireSequence++;
		_burstSequence += 1f;
		_timeSinceLastShot = 0f;

		var burstIndex = Mathf.RoundToInt(_burstSequence);
		var adsBlend = GetAdsBlend();
		var spawnTransform = _fireSystem.BuildFiringTransform(instance.Definition, instance, _fireSequence, burstIndex, _ownerPeerId, adsBlend, out var spreadRad);

		_fireSystem.SpawnProjectile(instance.Definition, true, _fireSequence, spawnTransform, _ownerPeerId);
		_recoilSystem.ApplyVisualSpread(spreadRad);
		_recoilSystem.ApplyRecoilKick(instance, burstIndex, adsBlend);
		_fxSystem.PlayAudio(instance.Definition.FireAudio, _player?.GlobalPosition ?? Vector3.Zero);
		_fxSystem.SpawnMuzzleFx(instance.Definition);
		_inventory?.EmitAmmo();

		_cooldownTimer = Mathf.Max(instance.Definition.FireCooldownSec, 0f);
		_state = WeaponState.FiringCooldown;
	}

	private void TryStartReload(WeaponInstance instance)
	{
		if (instance == null || instance.Definition == null)
			return;

		if (!instance.CanReload)
			return;

		if (_state == WeaponState.Reloading)
			return;

		var durationMs = instance.Definition.ReloadDurationSec * 1000.0;
		instance.BeginReload(Time.GetTicksMsec(), durationMs);
		_reloadTimer = instance.Definition.ReloadDurationSec;
		_state = WeaponState.Reloading;
		_inventory?.EmitAmmo();

		_fxSystem?.PlayAudio(instance.Definition.ReloadAudio, _player?.GlobalPosition ?? Vector3.Zero);
	}

	private void FinishReload()
	{
		if (_inventory == null || !_inventory.TryGetEquipped(out var instance) || instance == null)
			return;

		instance.CompleteReload();
		_reloadTimer = 0f;
		_state = WeaponState.Idle;
		_inventory.EmitAmmo();
	}

	private void TryToggleWeapon()
	{
		if (_inventory == null)
			return;

		if (_inventory.TryGetEquipped(out var current) && current != null)
		{
			current.CancelReload();
		}

		_reloadTimer = 0f;
		_cooldownTimer = 0f;
		_state = WeaponState.Idle;

		_inventory.TogglePrevious();
	}

	private float GetAdsBlend()
	{
		return _player?.GetAdsBlend() ?? 0f;
	}

	private void RecoverRecoil(float dt)
	{
		if (_inventory == null || !_inventory.TryGetEquipped(out var instance))
			return;

		_recoilSystem?.RecoverRecoil(dt, instance);
	}

	public int GetFireSequence() => _fireSequence;

	public AdsConfig GetEquippedAdsConfig()
	{
		return _inventory?.Equipped?.Definition?.Ads;
	}

	public void PlayRemoteFireFx(WeaponType type)
	{
		if (_player == null || _inventory == null)
			return;
		var def = _inventory.Get(type)?.Definition ?? _inventory.Equipped?.Definition;
		if (def == null)
			return;
		_fxSystem?.SpawnMuzzleFx(def);
		_fxSystem?.PlayAudio(def.FireAudio, _player.GlobalPosition);
	}

	public void SpawnRemoteProjectile(WeaponType type, long ownerPeerId, int fireSequence)
	{
		if (_inventory == null)
			return;

		var instance = _inventory.Get(type) ?? _inventory.Equipped;
		var def = instance?.Definition ?? _inventory.Equipped?.Definition;
		if (def == null)
			return;

		var adsBlend = GetAdsBlend();
		// For remote projectiles, use fireSequence as burst index since we don't track remote bursts
		var spawnTransform = _fireSystem.BuildFiringTransform(def, instance, fireSequence, fireSequence, ownerPeerId, adsBlend, out _);
		_fireSystem.SpawnProjectile(def, serverAuthority: false, fireSequence: fireSequence, spawnTransform: spawnTransform, ownerPeerId: ownerPeerId);
	}
}

public static class PlayerCharacterWeaponExtensions
{
	public static bool IsAuthority(this PlayerCharacter player)
	{
		return player != null && player.HasAuthority();
	}
}
