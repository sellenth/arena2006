using Godot;
using System;

/// <summary>
/// Handles weapon firing logic, projectile spawning, and projectile pooling.
/// </summary>
public partial class WeaponFireSystem : Node
{
	[Export] public NodePath ProjectileParentPath { get; set; } = "";

	private PlayerCharacter _player;
	private WeaponFxSystem _fxSystem;
	private WeaponRecoilSystem _recoilSystem;
	private long _ownerPeerId = 0;
	private Node _poolRoot;
	private readonly System.Collections.Generic.Dictionary<string, ProjectilePool> _pools = new();

	public void Initialize(PlayerCharacter player, WeaponFxSystem fxSystem, WeaponRecoilSystem recoilSystem, long ownerPeerId)
	{
		_player = player;
		_fxSystem = fxSystem;
		_recoilSystem = recoilSystem;
		_ownerPeerId = ownerPeerId;

		_poolRoot = new Node { Name = "ProjectilePools" };
		AddChild(_poolRoot);
	}

	public void SpawnProjectile(WeaponDefinition def, bool serverAuthority, int fireSequence, Transform3D spawnTransform, long ownerPeerId)
	{
		if (def == null || def.ProjectileScene == null || _player == null)
			return;

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

	public Transform3D BuildFiringTransform(WeaponDefinition def, WeaponInstance instance, int fireSequence, int burstIndex, long ownerPeerId, float adsBlend, out Vector2 spreadRad)
	{
		var baseTransform = ResolveProjectileTransform(def);
		// Use burstIndex for spread curve (resets between bursts), fireSequence for RNG seed (deterministic)
		var spreadDeg = _recoilSystem.ComputeSpreadDegrees(def, instance, burstIndex, adsBlend);
		spreadRad = _recoilSystem.ComputeSpreadRotation(spreadDeg, fireSequence, def, ownerPeerId);
		GD.Print($"[Spread] burstIdx={burstIndex}, adsBlend={adsBlend:F2}, spreadDeg={spreadDeg:F3}, spreadRad=({spreadRad.X:F4}, {spreadRad.Y:F4})");
		return _recoilSystem.ApplySpreadToTransform(baseTransform, spreadRad);
	}

	public Transform3D ResolveProjectileTransform(WeaponDefinition def)
	{
		if (_player == null)
			return Transform3D.Identity;

		// Always use player's view direction for aiming
		var viewDir = _player.GetViewDirection().Normalized();
		if (viewDir.IsZeroApprox())
		{
			viewDir = -_player.GlobalTransform.Basis.Z;
		}

		var basis = Basis.LookingAt(viewDir, Vector3.Up);

		// Try to get muzzle position from weapon view (but ignore its rotation)
		if (_fxSystem != null && _fxSystem.TryGetMuzzleTransform(out var muzzleTransform, out _))
		{
			// Use muzzle position, but player's aim direction
			GD.Print($"[Fire] viewDir={viewDir}, muzzlePos={muzzleTransform.Origin}, projDir={-basis.Z}");
			return new Transform3D(basis, muzzleTransform.Origin);
		}

		// Fallback: offset from player position
		const float ForwardOffset = 0.3f;
		const float VerticalOffset = 0.9f;
		var origin = _player.GlobalTransform.Origin + (viewDir * ForwardOffset) + (Vector3.Up * VerticalOffset);

		var spawn = def.ProjectileSpawn;
		return new Transform3D(basis, origin) * spawn;
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
}
