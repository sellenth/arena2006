using Godot;
using System;

public partial class WeaponController : Node
{
	[Export] public NodePath InventoryPath { get; set; } = "../WeaponInventory";
	[Export] public NodePath ProjectileParentPath { get; set; } = "";

	private WeaponInventory _inventory;
	private PlayerCharacter _player;
	private GameModeManager _gameMode;
	private NetworkController _network;

	private WeaponState _state = WeaponState.Idle;
	private float _cooldownTimer = 0f;
	private float _reloadTimer = 0f;
	private int _fireSequence = 0;
	private PlayerInputState _lastInput = new PlayerInputState();
	private long _ownerPeerId = 0;

	public override void _Ready()
	{
		_player = GetParent() as PlayerCharacter ?? GetOwner() as PlayerCharacter;
		_inventory = GetNodeOrNull<WeaponInventory>(InventoryPath);
		_gameMode = GetNodeOrNull<GameModeManager>("/root/GameModeManager");
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");
		_ownerPeerId = _network?.ClientPeerId ?? 0;
		SetPhysicsProcess(true);
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

		ProcessInputFrame();
	}

	public void SetInput(PlayerInputState input)
	{
		_lastInput.CopyFrom(input);
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
	}

	private bool WeaponsAllowed()
	{
		if (_gameMode == null)
			return true;
		return _gameMode.WeaponsEnabled;
	}

	private void ProcessInputFrame()
	{
		if (_inventory == null || !_inventory.TryGetEquipped(out var instance) || instance?.Definition == null)
			return;

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
			PlayAudio(instance.Definition.DryFireAudio, _player?.GlobalPosition ?? Vector3.Zero);
			return;
		}

		if (!instance.ConsumeRound())
		{
			PlayAudio(instance.Definition.DryFireAudio, _player?.GlobalPosition ?? Vector3.Zero);
			return;
		}

		_fireSequence++;
		SpawnProjectile(instance.Definition);
		PlayAudio(instance.Definition.FireAudio, _player?.GlobalPosition ?? Vector3.Zero);
		SpawnMuzzleFx(instance.Definition);
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

		PlayAudio(instance.Definition.ReloadAudio, _player?.GlobalPosition ?? Vector3.Zero);
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

	private void SpawnMuzzleFx(WeaponDefinition def)
	{
		if (def?.MuzzleFx == null || _player == null)
			return;

		var muzzle = ResolveProjectileTransform(def);
		def.MuzzleFx.Spawn(_player, muzzle);
	}

	private void PlayAudio(WeaponAudioSet set, Vector3 position)
	{
		if (set == null || set.Stream == null)
			return;

		if (set.Spatial)
		{
			var spatial = set.Create3D(this, position);
			if (spatial == null)
				return;

			spatial.Bus = AudioSettingsManager.WeaponsBusName;
			spatial.PitchScale = (float)GD.RandRange(set.RandomPitchMin, set.RandomPitchMax);
			spatial.Play();
			spatial.Finished += () =>
			{
				if (IsInstanceValid(spatial)) spatial.QueueFree();
			};
			return;
		}

		var flat = new AudioStreamPlayer
		{
			Stream = set.Stream,
			VolumeDb = set.VolumeDb,
			PitchScale = (float)GD.RandRange(set.RandomPitchMin, set.RandomPitchMax)
		};

		AddChild(flat);
		flat.Bus = AudioSettingsManager.WeaponsBusName;
		flat.Play();
		flat.Finished += () =>
		{
			if (IsInstanceValid(flat)) flat.QueueFree();
		};
	}

	private Transform3D ResolveProjectileTransform(WeaponDefinition def)
	{
		if (_player == null)
			return Transform3D.Identity;

		var viewDir = _player.GetViewDirection().Normalized();
		if (viewDir.IsZeroApprox())
		{
			viewDir = -_player.GlobalTransform.Basis.Z;
		}

		var basis = Basis.LookingAt(viewDir, Vector3.Up);

		var origin = _player.GlobalTransform.Origin + (viewDir * 0.6f) + (Vector3.Up * 0.9f);

		var spawn = def.ProjectileSpawn;
		return new Transform3D(basis, origin) * spawn;
	}

	private void SpawnProjectile(WeaponDefinition def)
	{
		if (def == null || def.ProjectileScene == null || _player == null)
			return;

		var spawnTransform = ResolveProjectileTransform(def);
		var direction = -spawnTransform.Basis.Z;

		var projectileNode = def.ProjectileScene.Instantiate<Node>();
		var parent = GetProjectileParent();
		parent?.AddChild(projectileNode);

		if (projectileNode is Node3D node3D)
		{
			node3D.GlobalTransform = spawnTransform;
		}

		if (projectileNode is RocketProjectile rocket)
		{
			var speed = rocket.Speed;
			var velocity = direction * speed;
			rocket.Initialize(_fireSequence, _ownerPeerId, true, spawnTransform.Origin, velocity);
			rocket.RegisterCollisionException(_player);
		}
		else if (projectileNode is MachineGunProjectile bullet)
		{
			var speed = 120.0f;
			var velocity = direction * speed;
			bullet.Initialize(_fireSequence, _ownerPeerId, true, spawnTransform.Origin, spawnTransform.Basis, velocity, def.Damage);
			bullet.RegisterCollisionException(_player);
		}
	}

	private Node GetProjectileParent()
	{
		if (!ProjectileParentPath.IsEmpty)
		{
			return GetNodeOrNull(ProjectileParentPath);
		}
		return GetTree().CurrentScene;
	}
}

public static class PlayerCharacterWeaponExtensions
{
	public static bool IsAuthority(this PlayerCharacter player)
	{
		return player != null && player.HasAuthority();
	}
}
