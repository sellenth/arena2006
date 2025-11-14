using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RespawnManager : RefCounted
{
	public static RespawnManager Instance { get; } = new RespawnManager();

	private readonly List<SpawnPoint> _spawnPoints = new();
	private readonly System.Collections.Generic.Dictionary<ulong, int> _nodeToIndex = new System.Collections.Generic.Dictionary<ulong, int>();
	private readonly RandomNumberGenerator _rng = new();

	private RespawnManager()
	{
		_rng.Randomize();
	}

	private struct SpawnPoint
	{
		public Transform3D Transform;
		public string Name;
		public float Weight;
		public float CoverScore;
		public float ExposureScore;
		public bool Enabled;
		public ulong NodeInstanceId;

		public Vector3 Position => Transform.Origin;
		public Vector3 Forward => -Transform.Basis.Z.Normalized();

		public SpawnPoint(Transform3D transform, string name, float weight, float cover, float exposure, ulong id, bool enabled = true)
		{
			Transform = transform;
			Name = string.IsNullOrEmpty(name) ? "SpawnPoint" : name;
			Weight = weight;
			CoverScore = cover;
			ExposureScore = exposure;
			Enabled = enabled;
			NodeInstanceId = id;
		}
	}

	public enum VelocityRetention { Reset, Preserve, Override }

	public enum GroupFormation { Stack, Line, Arc, Grid, Custom }

	public struct RespawnRequest
	{
		public Transform3D Transform { get; set; }
		public VelocityRetention LinearVelocityMode { get; set; }
		public VelocityRetention AngularVelocityMode { get; set; }
		public Vector3? LinearVelocityOverride { get; set; }
		public Vector3? AngularVelocityOverride { get; set; }

		public static RespawnRequest Create(Transform3D transform)
		{
			return new RespawnRequest
			{
				Transform = transform,
				LinearVelocityMode = VelocityRetention.Reset,
				AngularVelocityMode = VelocityRetention.Reset
			};
		}
	}

	public sealed class GroupRespawnOptions
	{
		public GroupFormation Formation { get; init; } = GroupFormation.Stack;
		public float Spacing { get; init; } = 4.0f;
		public VelocityRetention LinearVelocityMode { get; init; } = VelocityRetention.Reset;
		public VelocityRetention AngularVelocityMode { get; init; } = VelocityRetention.Reset;
		public IList<Vector3> CustomOffsets { get; init; }
		public IList<Vector3> CustomLinearVelocities { get; init; }
		public IList<Vector3> CustomAngularVelocities { get; init; }
		public IList<Basis> CustomRotations { get; init; }
		public bool AlignToAnchorForward { get; init; } = true;
	}

	public struct SpawnQuery
	{
		public Node3D ContextNode { get; set; }
		public Transform3D? FallbackTransform { get; set; }
		public IReadOnlyList<Vector3> AllyPositions { get; set; }
		public IReadOnlyList<Vector3> OpponentPositions { get; set; }
		public IReadOnlyList<Vector3> DangerPositions { get; set; }
		public Vector3? PreferredPosition { get; set; }
		public Vector3? PreferredForward { get; set; }
		public float DistanceWeight { get; set; }
		public float MinOpponentDistance { get; set; }
		public float CloseEnemyPenalty { get; set; }
		public float AllyCohesionWeight { get; set; }
		public float DesiredAllyDistance { get; set; }
		public float CohesionFalloff { get; set; }
		public float DangerWeight { get; set; }
		public float DangerRadius { get; set; }
		public float PreferenceWeight { get; set; }
		public bool RequireLineOfSightBreak { get; set; }
		public float LineOfSightPenalty { get; set; }
		public float LineOfSightBonus { get; set; }
		public uint LineOfSightCollisionMask { get; set; }
		public float LineOfSightHeightOffset { get; set; }
		public float RandomVariance { get; set; }
		public float CoverWeight { get; set; }
		public float ExposureWeight { get; set; }

		public static SpawnQuery Create(Node3D context)
		{
			return new SpawnQuery
			{
				ContextNode = context,
				FallbackTransform = null,
				AllyPositions = Array.Empty<Vector3>(),
				OpponentPositions = Array.Empty<Vector3>(),
				DangerPositions = Array.Empty<Vector3>(),
				PreferredPosition = null,
				PreferredForward = null,
				DistanceWeight = 1.25f,
				MinOpponentDistance = 14.0f,
				CloseEnemyPenalty = 2.5f,
				AllyCohesionWeight = 0.5f,
				DesiredAllyDistance = 12.0f,
				CohesionFalloff = 5.0f,
				DangerWeight = 1.5f,
				DangerRadius = 10.0f,
				PreferenceWeight = 2.0f,
				RequireLineOfSightBreak = true,
				LineOfSightPenalty = 0.35f,
				LineOfSightBonus = 0.85f,
				LineOfSightCollisionMask = uint.MaxValue,
				LineOfSightHeightOffset = 1.5f,
				RandomVariance = 0.2f,
				CoverWeight = 0.8f,
				ExposureWeight = 0.45f
			};
		}
	}

	public void ClearSpawnPoints()
	{
		_spawnPoints.Clear();
		_nodeToIndex.Clear();
	}

	public void EnsureSpawnPoints(Node context)
	{
		if (context == null)
			return;

		var tree = context.GetTree();
		if (tree == null)
			return;

		foreach (var groupName in new[] { "RespawnPoints", "respawn_points", "SpawnPoints" })
		{
			var nodes = tree.GetNodesInGroup(groupName);
			foreach (Node node in nodes)
			{
				if (node is Node3D node3D)
					RegisterSpawnPoint(node3D, 1.0f, 0.5f, 0.35f);
			}
		}

		var root = tree.Root;
		if (root == null)
			return;

		var carSpawn = root.FindChild("CarSpawnPoint", true, false) as Marker3D;
		carSpawn ??= root.GetNodeOrNull<Marker3D>("/root/GameRoot/CarSpawnPoint");
		if (carSpawn != null)
			RegisterSpawnPoint(carSpawn, 1.15f, 0.4f, 0.2f);
	}

	public bool RegisterSpawnPoint(Node3D node, float weight = 1.0f, float cover = 0.5f, float exposure = 0.5f)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
			return false;

		var transform = node.GlobalTransform;
		var id = node.GetInstanceId();
		if (_nodeToIndex.TryGetValue(id, out var index))
		{
			var updated = _spawnPoints[index];
			updated.Transform = transform;
			updated.Enabled = true;
			_spawnPoints[index] = updated;
			return true;
		}

		var spawn = new SpawnPoint(transform, node.Name, weight, cover, exposure, id);
		_nodeToIndex[id] = _spawnPoints.Count;
		_spawnPoints.Add(spawn);
		return true;
	}

	public bool RegisterSpawnPoint(Transform3D transform, string name = null, float weight = 1.0f, float cover = 0.5f, float exposure = 0.5f)
	{
		var spawn = new SpawnPoint(transform, name, weight, cover, exposure, 0);
		_spawnPoints.Add(spawn);
		return true;
	}

	public bool TryGetBestSpawnTransform(SpawnQuery query, out Transform3D transform)
	{
		RefreshTrackedSpawnPoints();

		var candidates = _spawnPoints.Where(p => p.Enabled).ToList();
		if (candidates.Count == 0)
		{
			if (query.FallbackTransform.HasValue)
			{
				transform = query.FallbackTransform.Value;
				return false;
			}

			transform = Transform3D.Identity;
			return false;
		}

		var greatestScore = float.NegativeInfinity;
		SpawnPoint best = candidates[0];

		foreach (var spawn in candidates)
		{
			var score = ScoreSpawnPoint(spawn, query);
			if (score > greatestScore)
			{
				greatestScore = score;
				best = spawn;
			}
		}

		transform = best.Transform;
		return true;
	}

	public bool RespawnEntity(RigidBody3D body, RespawnRequest request)
	{
		if (body == null)
			return false;

		var target = request.Transform;

		body.GlobalTransform = target;
		body.LinearVelocity = ResolveVelocity(body.LinearVelocity, request.LinearVelocityMode, request.LinearVelocityOverride);
		body.AngularVelocity = ResolveVelocity(body.AngularVelocity, request.AngularVelocityMode, request.AngularVelocityOverride);
		body.Sleeping = false;
		return true;
	}

	public bool TeleportEntity(CharacterBody3D character, Transform3D transform)
	{
		if (character == null)
			return false;

		character.GlobalTransform = transform;
		character.Velocity = Vector3.Zero;
		return true;
	}

	public bool TeleportEntity(CharacterBody3D character, Vector3 position)
	{
		if (character == null)
			return false;

		var transform = new Transform3D(character.GlobalTransform.Basis, position);
		return TeleportEntity(character, transform);
	}

	public bool TeleportEntity(CharacterBody3D character, Vector3 position, Basis rotation)
	{
		if (character == null)
			return false;

		var transform = new Transform3D(rotation, position);
		return TeleportEntity(character, transform);
	}

	public bool RespawnEntityAtBestPoint(RigidBody3D body, Node3D context)
	{
		if (body == null)
			return false;

		context ??= body;
		EnsureSpawnPoints(context);

		var query = SpawnQuery.Create(context);
		query.FallbackTransform = body.GlobalTransform;
		if (!TryGetBestSpawnTransform(query, out var transform))
			return false;

		var request = RespawnRequest.Create(transform);
		return RespawnEntity(body, request);
	}

	public bool RespawnAtPosition(RigidBody3D body, Vector3 position)
	{
		if (body == null)
			return false;

		var transform = new Transform3D(body.GlobalTransform.Basis, position);
		return RespawnEntity(body, RespawnRequest.Create(transform));
	}

	public bool RespawnAtPosition(RigidBody3D body, Vector3 position, Basis rotation)
	{
		if (body == null)
			return false;

		var transform = new Transform3D(rotation, position);
		return RespawnEntity(body, RespawnRequest.Create(transform));
	}

	public bool RespawnWithVelocity(RigidBody3D body, Transform3D transform, Vector3? linearVelocity, Vector3? angularVelocity)
	{
		if (body == null)
			return false;

		var request = RespawnRequest.Create(transform);
		request.LinearVelocityMode = linearVelocity.HasValue ? VelocityRetention.Override : VelocityRetention.Preserve;
		request.AngularVelocityMode = angularVelocity.HasValue ? VelocityRetention.Override : VelocityRetention.Preserve;
		request.LinearVelocityOverride = linearVelocity;
		request.AngularVelocityOverride = angularVelocity;
		return RespawnEntity(body, request);
	}

	public void RespawnGroup(IReadOnlyList<RigidBody3D> bodies, Transform3D anchor, GroupRespawnOptions options = null)
	{
		if (bodies == null || bodies.Count == 0)
			return;

		options ??= new GroupRespawnOptions();

		if (options.CustomRotations != null && options.CustomRotations.Count > 0 &&
			(options.CustomOffsets == null || options.CustomOffsets.Count == 0) &&
			options.Formation != GroupFormation.Custom)
		{
			GD.PrintErr("RespawnManager: Custom rotations require matching offsets or a custom formation.");
		}

		var offsets = ResolveOffsets(options, bodies.Count);

		for (var i = 0; i < bodies.Count; i++)
		{
			var body = bodies[i];
			if (body == null)
				continue;

			var offset = offsets[Mathf.Min(i, offsets.Count - 1)];
			var worldOffset = anchor.Basis * offset;
			var basis = anchor.Basis;

			if (options.CustomRotations != null && options.CustomRotations.Count > 0)
			{
				var index = Mathf.Min(i, options.CustomRotations.Count - 1);
				basis = options.CustomRotations[index];
			}
			else if (!options.AlignToAnchorForward)
			{
				basis = Basis.Identity;
			}

			var transform = new Transform3D(basis, anchor.Origin + worldOffset);
			var request = RespawnRequest.Create(transform);
			request.LinearVelocityMode = options.LinearVelocityMode;
			request.AngularVelocityMode = options.AngularVelocityMode;
			request.LinearVelocityOverride = ExtractOptional(options.CustomLinearVelocities, i);
			request.AngularVelocityOverride = ExtractOptional(options.CustomAngularVelocities, i);
			RespawnEntity(body, request);
		}
	}

	private IList<Vector3> ResolveOffsets(GroupRespawnOptions options, int count)
	{
		if (options.CustomOffsets != null && options.CustomOffsets.Count > 0)
			return options.CustomOffsets;

		var offsets = new List<Vector3>(count);
		switch (options.Formation)
		{
			case GroupFormation.Line:
				var start = -0.5f * (count - 1) * options.Spacing;
				for (var i = 0; i < count; i++)
					offsets.Add(new Vector3(start + i * options.Spacing, 0, 0));
				break;
			case GroupFormation.Arc:
				var radius = Math.Max(2.0f, options.Spacing * Math.Max(1, count / 2.0f));
				var angleStep = Mathf.DegToRad(60.0f / Math.Max(1, count - 1));
				var startAngle = -angleStep * (count - 1) * 0.5f;
				for (var i = 0; i < count; i++)
				{
					var angle = startAngle + i * angleStep;
					offsets.Add(new Vector3(Mathf.Sin(angle) * (float)radius, 0, Mathf.Cos(angle) * (float)radius));
				}
				break;
			case GroupFormation.Grid:
				var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
				var rows = Mathf.CeilToInt((float)count / columns);
				for (var index = 0; index < count; index++)
				{
					var row = index / columns;
					var column = index % columns;
					var offsetX = (column - (columns - 1) * 0.5f) * options.Spacing;
					var offsetZ = (row - (rows - 1) * 0.5f) * options.Spacing;
					offsets.Add(new Vector3(offsetX, 0, offsetZ));
				}
				break;
			default:
				for (var i = 0; i < count; i++)
					offsets.Add(Vector3.Zero);
				break;
		}

		return offsets;
	}

	private float ScoreSpawnPoint(SpawnPoint spawn, SpawnQuery query)
	{
		var score = spawn.Weight;

		if (query.OpponentPositions != null && query.OpponentPositions.Count > 0)
		{
			var minDistance = float.MaxValue;
			var sumDistance = 0.0f;
			foreach (var pos in query.OpponentPositions)
			{
				var dist = spawn.Position.DistanceTo(pos);
				minDistance = Mathf.Min(minDistance, dist);
				sumDistance += dist;
			}

			var avgDistance = sumDistance / query.OpponentPositions.Count;
			if (minDistance < query.MinOpponentDistance)
			{
				score -= (query.MinOpponentDistance - minDistance) * query.CloseEnemyPenalty;
			}

			score += Mathf.Clamp(avgDistance, 0, query.MinOpponentDistance * 3.0f) * query.DistanceWeight * 0.35f;
			score += Mathf.Clamp(minDistance, 0, query.MinOpponentDistance * 2.5f) * query.DistanceWeight;
		}
		else
		{
			score += query.DistanceWeight;
		}

		if (query.AllyPositions != null && query.AllyPositions.Count > 0)
		{
			var minAllyDistance = float.MaxValue;
			foreach (var ally in query.AllyPositions)
			{
				var dist = spawn.Position.DistanceTo(ally);
				minAllyDistance = Mathf.Min(minAllyDistance, dist);
			}

			var cohesion = Mathf.Exp(-Mathf.Abs(minAllyDistance - query.DesiredAllyDistance) / Mathf.Max(0.01f, query.CohesionFalloff));
			score += cohesion * query.AllyCohesionWeight;
		}

		if (query.DangerPositions != null && query.DangerPositions.Count > 0)
		{
			foreach (var danger in query.DangerPositions)
			{
				var dist = spawn.Position.DistanceTo(danger);
				if (dist < query.DangerRadius)
				{
					var dangerFactor = 1.0f - dist / query.DangerRadius;
					score -= dangerFactor * query.DangerWeight;
				}
			}
		}

		if (query.PreferredPosition.HasValue)
		{
			var dist = spawn.Position.DistanceTo(query.PreferredPosition.Value);
			score += query.PreferenceWeight / (1.0f + dist);
		}

		if (query.PreferredForward.HasValue)
		{
			var alignment = Mathf.Max(0.0f, spawn.Forward.Dot(query.PreferredForward.Value.Normalized()));
			score += alignment * 0.5f * query.PreferenceWeight;
		}

		if (query.RequireLineOfSightBreak && query.OpponentPositions != null && query.OpponentPositions.Count > 0)
		{
			var hasLos = false;
			foreach (var pos in query.OpponentPositions)
			{
				if (!HasSafeLineOfSight(spawn.Position, pos, query))
					continue;

				hasLos = true;
				break;
			}

			if (hasLos)
				score *= query.LineOfSightPenalty;
			else
				score += query.LineOfSightBonus;
		}

		score += spawn.CoverScore * query.CoverWeight;
		score -= spawn.ExposureScore * query.ExposureWeight;
		score += (float)_rng.RandfRange(-query.RandomVariance, query.RandomVariance);

		return score;
	}

	private void RefreshTrackedSpawnPoints()
	{
		if (_nodeToIndex.Count == 0)
			return;

		foreach (var pair in _nodeToIndex.ToArray())
		{
			if (GodotObject.InstanceFromId(pair.Key) is not Node3D node || !GodotObject.IsInstanceValid(node))
			{
				var disabled = _spawnPoints[pair.Value];
				disabled.Enabled = false;
				_spawnPoints[pair.Value] = disabled;
				continue;
			}

			var spawn = _spawnPoints[pair.Value];
			spawn.Transform = node.GlobalTransform;
			spawn.Enabled = true;
			_spawnPoints[pair.Value] = spawn;
		}
	}

	private static Vector3 ResolveVelocity(Vector3 current, VelocityRetention mode, Vector3? overrideValue)
	{
		return mode switch
		{
			VelocityRetention.Preserve => current,
			VelocityRetention.Override when overrideValue.HasValue => overrideValue.Value,
			_ => Vector3.Zero
		};
	}

	private bool HasSafeLineOfSight(Vector3 origin, Vector3 target, SpawnQuery query)
	{
		if (query.ContextNode == null)
			return true;

		var world = query.ContextNode.GetWorld3D();
		if (world == null)
			return true;

		var state = world.DirectSpaceState;
		if (state == null)
			return true;

		var start = origin + Vector3.Up * query.LineOfSightHeightOffset;
		var end = target + Vector3.Up * query.LineOfSightHeightOffset;
		var parameters = PhysicsRayQueryParameters3D.Create(start, end);
		parameters.CollisionMask = query.LineOfSightCollisionMask;
		var result = state.IntersectRay(parameters);
		return result.Count == 0;
	}

	private static Vector3? ExtractOptional(IList<Vector3> list, int index)
	{
		if (list == null || list.Count == 0)
			return null;
		return list[index < list.Count ? index : list.Count - 1];
	}
}
