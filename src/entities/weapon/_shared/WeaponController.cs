using Godot;
using System;

public partial class WeaponController : Node
{
	[Export] public NodePath InventoryPath { get; set; } = "../WeaponInventory";
	[Export] public NodePath ProjectileParentPath { get; set; } = "";
	[Export] public bool IgnoreGameModeWeapons { get; set; } = false;

	private WeaponInventory _inventory;
	private PlayerCharacter _player;
	private GameModeManager _gameMode;
	private NetworkController _network;

	private WeaponState _state = WeaponState.Idle;
	private float _cooldownTimer = 0f;
	private float _reloadTimer = 0f;
	private int _fireSequence = 0;
	private PlayerInputState _lastInput = new PlayerInputState();
	private Vector2 _recoilOffsetRad = Vector2.Zero;
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
			PlayAudio(instance.Definition.DryFireAudio, _player?.GlobalPosition ?? Vector3.Zero);
			return;
		}

		if (!instance.ConsumeRound())
		{
			PlayAudio(instance.Definition.DryFireAudio, _player?.GlobalPosition ?? Vector3.Zero);
			return;
		}

		_fireSequence++;
		var shotIndex = _fireSequence;
		var adsBlend = GetAdsBlend();
		var spawnTransform = BuildFiringTransform(instance.Definition, instance, shotIndex, _ownerPeerId, adsBlend, out var spreadRad);
		SpawnProjectile(instance.Definition, true, shotIndex, spawnTransform, _ownerPeerId);
		ApplyVisualSpread(spreadRad);
		ApplyRecoilKick(instance, shotIndex, adsBlend);
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
			rocket.WeaponType = def.Id;
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
			bullet.Initialize(fireSequence, ownerPeerId, serverAuthority, spawnTransform.Origin, spawnTransform.Basis, velocity, def.Damage, def.Id);
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

	public AdsConfig GetEquippedAdsConfig()
	{
		return _inventory?.Equipped?.Definition?.Ads;
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

		var instance = _inventory.Get(type) ?? _inventory.Equipped;
		var def = instance?.Definition ?? _inventory.Equipped?.Definition;
		if (def == null)
			return;

		var adsBlend = GetAdsBlend();
		var spawnTransform = BuildFiringTransform(def, instance, fireSequence, ownerPeerId, adsBlend, out _);
		SpawnProjectile(def, serverAuthority: false, fireSequence: fireSequence, spawnTransform: spawnTransform, ownerPeerId: ownerPeerId);
	}

	private float GetAdsBlend()
	{
		return _player?.GetAdsBlend() ?? 0f;
	}

	private void ApplyRecoilKick(WeaponInstance instance, int shotIndex, float adsBlend)
	{
		if (instance?.Definition?.Recoil == null || _player == null)
			return;

		var kickDeg = ComputeRecoilKickDegrees(instance, shotIndex, adsBlend);
		if (kickDeg == Vector2.Zero)
			return;

		var kickRad = new Vector2(Mathf.DegToRad(kickDeg.X), Mathf.DegToRad(kickDeg.Y));
		_recoilOffsetRad += kickRad;
		_player.ApplyRecoil(kickRad);
	}

	private Vector2 ComputeRecoilKickDegrees(WeaponInstance instance, int shotIndex, float adsBlend)
	{
		var def = instance?.Definition;
		var profile = def?.Recoil;
		if (profile == null)
			return Vector2.Zero;

		var baseKick = GetPatternKick(profile, shotIndex);
		var ads = def?.Ads;
		var adsScale = ads != null
			? Mathf.Lerp(ads.HipRecoilScale, ads.AdsRecoilScale, adsBlend)
			: 1f;
		var attachmentDelta = GetAttachmentRecoilDelta(instance);
		return (baseKick + new Vector2(attachmentDelta, attachmentDelta)) * adsScale;
	}

	private Vector2 GetPatternKick(RecoilProfile profile, int shotIndex)
	{
		if (profile?.Pattern != null && profile.Pattern.Count > 0)
		{
			var index = Mathf.Clamp(shotIndex - 1, 0, profile.Pattern.Count - 1);
			return profile.Pattern[index];
		}
		return profile?.Kick ?? Vector2.Zero;
	}

	private float GetAttachmentRecoilDelta(WeaponInstance instance)
	{
		if (instance == null)
			return 0f;

		var delta = 0f;
		foreach (var attachment in instance.Attachments.Values)
		{
			if (attachment != null)
			{
				delta += attachment.RecoilDelta;
			}
		}
		return delta;
	}

	private float GetAttachmentSpreadDelta(WeaponInstance instance)
	{
		if (instance == null)
			return 0f;

		var delta = 0f;
		foreach (var attachment in instance.Attachments.Values)
		{
			if (attachment != null)
			{
				delta += attachment.SpreadDelta;
			}
		}
		return delta;
	}

	private float ComputeSpreadDegrees(WeaponDefinition def, WeaponInstance instance, int shotIndex, float adsBlend)
	{
		if (def == null)
			return 0f;

		var profile = def.Recoil;
		var baseSpread = profile?.EvaluateSpread(shotIndex) ?? 0f;
		var ads = def.Ads;
		var hip = ads?.HipSpreadDegrees ?? baseSpread;
		var adsSpread = ads?.AdsSpreadDegrees ?? baseSpread;
		var spread = Mathf.Max(baseSpread, Mathf.Lerp(hip, adsSpread, adsBlend));
		spread += GetAttachmentSpreadDelta(instance);
		return Mathf.Max(spread, 0f);
	}

	private Vector2 ComputeSpreadRotation(float spreadDegrees, int shotIndex, WeaponDefinition def, long ownerPeerId)
	{
		if (spreadDegrees <= 0f || def == null)
			return Vector2.Zero;

		var seed = ComputeRecoilSeed(shotIndex, def.Id, ownerPeerId);
		var rng = new RandomNumberGenerator { Seed = seed };
		var yaw = Mathf.DegToRad(rng.RandfRange(-spreadDegrees, spreadDegrees));
		var pitch = Mathf.DegToRad(rng.RandfRange(-spreadDegrees, spreadDegrees));
		return new Vector2(pitch, yaw);
	}

	private Transform3D BuildFiringTransform(WeaponDefinition def, WeaponInstance instance, int fireSequence, long ownerPeerId, float adsBlend, out Vector2 spreadRad)
	{
		var baseTransform = ResolveProjectileTransform(def);
		var spreadDeg = ComputeSpreadDegrees(def, instance, fireSequence, adsBlend);
		spreadRad = ComputeSpreadRotation(spreadDeg, fireSequence, def, ownerPeerId);
		return ApplySpreadToTransform(baseTransform, spreadRad);
	}

	private Transform3D ApplySpreadToTransform(Transform3D transform, Vector2 spreadRad)
	{
		if (spreadRad == Vector2.Zero)
			return transform;

		var basis = transform.Basis;
		basis = basis.Rotated(basis.X, spreadRad.X);
		basis = basis.Rotated(Vector3.Up, spreadRad.Y);
		return new Transform3D(basis, transform.Origin);
	}

	private ulong ComputeRecoilSeed(int fireSequence, WeaponType weaponId, long ownerPeerId)
	{
		unchecked
		{
			var a = (ulong)(fireSequence * 73856093);
			var b = (ulong)((int)weaponId * 19349663);
			var c = (ulong)(ownerPeerId * 83492791);
			return a ^ b ^ c;
		}
	}

	private void RecoverRecoil(float dt)
	{
		if (_recoilOffsetRad == Vector2.Zero)
			return;

		var recoil = _inventory?.Equipped?.Definition?.Recoil;
		if (recoil == null)
			return;

		var recoveryRate = Mathf.DegToRad(recoil.RecoveryRate);
		if (recoveryRate <= 0f)
			return;

		var previous = _recoilOffsetRad;
		_recoilOffsetRad = _recoilOffsetRad.MoveToward(Vector2.Zero, recoveryRate * dt);
		var delta = _recoilOffsetRad - previous;
		if (delta != Vector2.Zero)
		{
			_player?.ApplyRecoil(delta);
		}
	}

	private void ApplyVisualSpread(Vector2 spreadRad)
	{
		if (spreadRad == Vector2.Zero)
			return;

		if (_player == null || !_player.IsAuthority())
			return;

		_recoilOffsetRad += spreadRad;
		_player.ApplyRecoil(spreadRad);
	}
}

public static class PlayerCharacterWeaponExtensions
{
	public static bool IsAuthority(this PlayerCharacter player)
	{
		return player != null && player.HasAuthority();
	}
}
