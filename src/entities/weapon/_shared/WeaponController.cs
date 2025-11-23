using Godot;
using System;

public partial class WeaponController : Node
{
	[Export] public NodePath InventoryPath { get; set; } = "../WeaponInventory";
	[Export] public NodePath ProjectileParentPath { get; set; } = "";
	[Export] public bool IgnoreGameModeWeapons { get; set; } = true;

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
	private Node _poolRoot;
	private readonly System.Collections.Generic.Dictionary<string, ProjectilePool> _pools = new();

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
		_poolRoot = new Node { Name = "ProjectilePools" };
		AddChild(_poolRoot);
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
		if (IgnoreGameModeWeapons || _gameMode == null)
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
		SpawnProjectile(instance.Definition, true, _fireSequence, ResolveProjectileTransform(instance.Definition), _ownerPeerId);
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

		var origin = _player.GlobalTransform.Origin + (viewDir * 0.3f) + (Vector3.Up * 0.9f);

		var spawn = def.ProjectileSpawn;
		return new Transform3D(basis, origin) * spawn;
	}

	private void SpawnProjectile(WeaponDefinition def, bool serverAuthority, int fireSequence, Transform3D spawnTransform, long ownerPeerId)
	{
		if (def == null || def.ProjectileScene == null || _player == null)
			return;
		_fireSequence = Math.Max(_fireSequence, fireSequence);
		var direction = -spawnTransform.Basis.Z;

		var pool = GetOrCreatePool(def);
		var parent = GetProjectileParent();
		var projectileNode = pool != null
			? pool.Rent<Node>(parent)
			: def.ProjectileScene.Instantiate<Node>();

		if (projectileNode != null && projectileNode.GetParent() == null && parent != null)
		{
			parent.AddChild(projectileNode);
		}

		if (projectileNode is Node3D node3D)
		{
			node3D.GlobalTransform = spawnTransform;
		}

		if (projectileNode is RocketProjectile rocket)
		{
			ApplyRocketConfig(def, rocket);
			var speed = rocket.Speed;
			var velocity = direction * speed;
			rocket.Initialize(fireSequence, ownerPeerId, serverAuthority, spawnTransform.Origin, velocity);
			rocket.RegisterCollisionException(_player);
			if (pool != null)
			{
				rocket.ReturnToPool = pool.Return;
			}
		}
		else if (projectileNode is MachineGunProjectile bullet)
		{
			var speed = ApplyBulletConfig(def, bullet);
			var velocity = direction * speed;
			bullet.Initialize(fireSequence, ownerPeerId, serverAuthority, spawnTransform.Origin, spawnTransform.Basis, velocity, def.Damage);
			bullet.RegisterCollisionException(_player);
			if (pool != null)
			{
				bullet.ReturnToPool = pool.Return;
			}
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

	private ProjectilePool GetOrCreatePool(WeaponDefinition def)
	{
		if (def?.ProjectileScene == null)
			return null;

		var key = def.ProjectileScene.ResourcePath;
		if (string.IsNullOrEmpty(key))
		{
			key = def.Id.ToString();
		}

		if (_pools.TryGetValue(key, out var existing) && existing != null)
			return existing;

		var pool = new ProjectilePool
		{
			Name = $"Pool_{def.Id}",
			ProjectileScene = def.ProjectileScene,
			PrewarmCount = def.ProjectilePoolPrewarm
		};

		_poolRoot?.AddChild(pool);
		_pools[key] = pool;
		return pool;
	}

	private void ApplyRocketConfig(WeaponDefinition def, RocketProjectile rocket)
	{
		if (def?.ProjectileConfig == null || rocket == null)
			return;

		rocket.Speed = def.ProjectileConfig.Speed;
		rocket.Lifetime = def.ProjectileConfig.LifetimeSec;
		rocket.ExplodeRadius = def.ProjectileConfig.ExplosionRadius;
		rocket.ArmDelaySec = def.ProjectileConfig.ArmDelaySec;
		rocket.GravityScale = def.ProjectileConfig.GravityScale;
		rocket.ExplosionDamage = def.ProjectileConfig.Damage;
		rocket.SelfDamageScale = def.ProjectileConfig.SelfDamageScale;
		rocket.KnockbackImpulse = def.ProjectileConfig.KnockbackImpulse;
		rocket.KnockbackUpBias = def.ProjectileConfig.KnockbackUpBias;
	}

	private float ApplyBulletConfig(WeaponDefinition def, MachineGunProjectile bullet)
	{
		if (def?.ProjectileConfig == null || bullet == null)
			return 120.0f;

		bullet.Lifetime = def.ProjectileConfig.LifetimeSec;
		return def.ProjectileConfig.Speed;
	}

	public int GetFireSequence() => _fireSequence;

	public void PlayRemoteFireFx(WeaponType type)
	{
		if (_player == null || _inventory == null)
			return;
		var def = _inventory.Get(type)?.Definition ?? _inventory.Equipped?.Definition;
		if (def == null)
			return;
		SpawnMuzzleFx(def);
		PlayAudio(def.FireAudio, _player.GlobalPosition);
	}

	public void SpawnRemoteProjectile(WeaponType type, long ownerPeerId, int fireSequence)
	{
		if (_inventory == null)
			return;

		var def = _inventory.Get(type)?.Definition ?? _inventory.Equipped?.Definition;
		if (def == null)
			return;

		var spawnTransform = ResolveProjectileTransform(def);
		SpawnProjectile(def, serverAuthority: false, fireSequence: fireSequence, spawnTransform: spawnTransform, ownerPeerId: ownerPeerId);
	}
}

public static class PlayerCharacterWeaponExtensions
{
	public static bool IsAuthority(this PlayerCharacter player)
	{
		return player != null && player.HasAuthority();
	}
}
