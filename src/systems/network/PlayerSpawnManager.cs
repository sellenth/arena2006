using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerSpawnManager : GodotObject
{
	private Node3D _playerSpawns = null;
	private bool _respawnPointsCached = false;
	private bool _hasFallbackSpawn = false;
	private Transform3D _fallbackSpawnTransform = Transform3D.Identity;
	private readonly RandomNumberGenerator _spawnRng = new RandomNumberGenerator();
	private PackedScene _playerCharacterScene;

	public PlayerSpawnManager()
	{
		_spawnRng.Randomize();
	}

	public void SetPlayerCharacterScene(PackedScene scene)
	{
		_playerCharacterScene = scene;
	}

	public void CacheSpawnPoints(SceneTree tree, Node3D fallbackParent)
	{
		if (_respawnPointsCached)
			return;

		_respawnPointsCached = true;
		var manager = RespawnManager.Instance;
		if (tree == null)
			return;

		foreach (var groupName in new[] { "RespawnPoints", "respawn_points", "SpawnPoints" })
		{
			var nodes = tree.GetNodesInGroup(groupName);
			foreach (Node node in nodes)
			{
				if (node is Node3D node3D)
				{
					manager.RegisterSpawnPoint(node3D, 1.0f, 0.5f, 0.35f);
					if (!_hasFallbackSpawn)
					{
						_fallbackSpawnTransform = node3D.GlobalTransform;
						_hasFallbackSpawn = true;
					}
				}
			}
		}

		var root = tree.Root;
		var carSpawn = root.FindChild("CarSpawnPoint", true, false) as Marker3D;
		carSpawn ??= root.GetNodeOrNull<Marker3D>("/root/GameRoot/Level/CarSpawnPoint");
		if (carSpawn != null)
		{
			manager.RegisterSpawnPoint(carSpawn, 1.15f, 0.4f, 0.2f);
			_fallbackSpawnTransform = carSpawn.GlobalTransform;
			_hasFallbackSpawn = true;
		}

		_playerSpawns = root.GetNode<Node3D>("/root/GameRoot/Level/PlayerSpawns");
	}

	public Transform3D GetSpawnTransform(int peerId, IEnumerable<Vector3> occupiedPositions, Node3D contextNode)
	{
		var manager = RespawnManager.Instance;
		var otherPositions = occupiedPositions.ToList();

		var query = RespawnManager.SpawnQuery.Create(contextNode);
		query.OpponentPositions = otherPositions;
		query.AllyPositions = System.Array.Empty<Vector3>();
		if (_hasFallbackSpawn)
			query.FallbackTransform = _fallbackSpawnTransform;

		if (manager.TryGetBestSpawnTransform(query, out var transform))
			return ApplySpawnJitter(transform);

		var fallback = _hasFallbackSpawn ? _fallbackSpawnTransform : Transform3D.Identity;
		return ApplySpawnJitter(fallback);
	}

	private Transform3D ApplySpawnJitter(Transform3D transform)
	{
		if (NetworkConfig.PlayerSpawnJitterRadius <= 0.0f)
			return transform;

		var offset = new Vector3(
			_spawnRng.RandfRange(-1.0f, 1.0f),
			0.0f,
			_spawnRng.RandfRange(-1.0f, 1.0f));

		if (offset.LengthSquared() < 0.0001f)
			offset = new Vector3(1.0f, 0.0f, 0.0f);

		offset = offset.Normalized() * _spawnRng.RandfRange(0.0f, NetworkConfig.PlayerSpawnJitterRadius);
		transform.Origin += offset;
		return transform;
	}

	public PlayerCharacter SpawnPlayerForPeer(int peerId, Node3D parent, RaycastCar ownerCar, int entityId)
	{
		if (parent == null || _playerCharacterScene == null)
			return null;

		var player = _playerCharacterScene.Instantiate<PlayerCharacter>();
		if (player == null)
			return null;

		player.AutoRegisterWithNetwork = false;
		player.Name = $"ServerPlayer_{peerId}";
		player.SetCameraActive(false);
		player.ConfigureAuthority(true);
		player.SetWorldActive(false);
		player.SetNetworkId(entityId);
		player.SetReplicatedMode(PlayerMode.Foot, 0);
		parent.AddChild(player);
		player.RegisterAsAuthority();

		var transform = _playerSpawns.GlobalTransform;
		transform = ApplySpawnJitter(transform);
		RespawnManager.Instance.TeleportEntity(player, transform);
		return player;
	}
}

