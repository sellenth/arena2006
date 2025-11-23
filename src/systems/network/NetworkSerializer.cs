using Godot;
using System.Collections.Generic;

public static partial class NetworkSerializer
{
	public const byte PacketCarInput = 1;
	public const byte PacketWelcome = 2;
	public const byte PacketRemovePlayer = 3;
	public const byte PacketPlayerInput = 4;
	public const byte PacketEntitySnapshot = 5;
	public const byte PacketEntityDespawn = 6;
	public const byte PacketScoreboard = 7;

	public const int CarSnapshotPayloadBytes = 4 + 12 + 16 + 12 + 12;
	public const int PlayerSnapshotPayloadBytes = 4 + 12 + 16 + 12 + 8;
	public const int VehicleStatePayloadBytes = 4 + 4 + 4 + 12 + 16 + 12 + 12;

	public static byte[] SerializeCarInput(CarInputState state)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketCarInput);
		buffer.PutU32((uint)state.Tick);
		buffer.PutU32((uint)state.VehicleId);
		buffer.PutFloat(state.Throttle);
		buffer.PutFloat(state.Steer);
		buffer.PutU8((byte)(state.Handbrake ? 1 : 0));
		buffer.PutU8((byte)(state.Brake ? 1 : 0));
		buffer.PutU8((byte)(state.Respawn ? 1 : 0));
		buffer.PutU8((byte)(state.Interact ? 1 : 0));
		return buffer.DataArray;
	}

	public static CarInputState DeserializeCarInput(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 1) return null;
		var packetType = buffer.GetU8();
		if (packetType != PacketCarInput) return null;
		if (buffer.GetAvailableBytes() < 4 + 4 + 4 + 4 + 1 + 1 + 1 + 1) return null;
		var state = new CarInputState
		{
			Tick = (int)buffer.GetU32(),
			VehicleId = (int)buffer.GetU32(),
			Throttle = buffer.GetFloat(),
			Steer = buffer.GetFloat(),
			Handbrake = buffer.GetU8() == 1,
			Brake = buffer.GetU8() == 1,
			Respawn = buffer.GetU8() == 1,
			Interact = buffer.GetU8() == 1
		};
		return state;
	}

	public static byte[] SerializePlayerInput(PlayerInputState state)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketPlayerInput);
		buffer.PutU32((uint)state.Tick);
		buffer.PutFloat(state.MoveInput.X);
		buffer.PutFloat(state.MoveInput.Y);
		buffer.PutU8((byte)(state.Jump ? 1 : 0));
		buffer.PutU8((byte)(state.PrimaryFire ? 1 : 0));
		buffer.PutU8((byte)(state.PrimaryFireJustPressed ? 1 : 0));
		buffer.PutU8((byte)(state.Reload ? 1 : 0));
		buffer.PutU8((byte)(state.Interact ? 1 : 0));
		buffer.PutU8((byte)(state.Sprint ? 1 : 0));
		buffer.PutFloat(state.ViewYaw);
		buffer.PutFloat(state.ViewPitch);
		return buffer.DataArray;
	}

	public static PlayerInputState DeserializePlayerInput(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 1) return null;
		var packetType = buffer.GetU8();
		if (packetType != PacketPlayerInput) return null;
		if (buffer.GetAvailableBytes() < 4 + 4 + 4 + 1 + 1 + 1 + 1 + 1 + 1 + 4 + 4) return null;
		var state = new PlayerInputState
		{
			Tick = (int)buffer.GetU32(),
			MoveInput = new Vector2(buffer.GetFloat(), buffer.GetFloat()),
			Jump = buffer.GetU8() == 1,
			PrimaryFire = buffer.GetU8() == 1,
			PrimaryFireJustPressed = buffer.GetU8() == 1,
			Reload = buffer.GetU8() == 1,
			Interact = buffer.GetU8() == 1,
			Sprint = buffer.GetU8() == 1,
			ViewYaw = buffer.GetFloat(),
			ViewPitch = buffer.GetFloat()
		};
		return state;
	}

	public static byte[] SerializeWelcome(int peerId)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketWelcome);
		buffer.PutU32((uint)peerId);
		return buffer.DataArray;
	}

	public static int DeserializeWelcome(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 5) return 0;
		var packetType = buffer.GetU8();
		if (packetType != PacketWelcome) return 0;
		return (int)buffer.GetU32();
	}

	public static byte[] SerializeRemovePlayer(int peerId)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketRemovePlayer);
		buffer.PutU32((uint)peerId);
		return buffer.DataArray;
	}

	public static int DeserializeRemovePlayer(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 5) return 0;
		var packetType = buffer.GetU8();
		if (packetType != PacketRemovePlayer) return 0;
		return (int)buffer.GetU32();
	}

	public static byte[] SerializeEntitySnapshots(System.Collections.Generic.IReadOnlyList<EntitySnapshotData> snapshots)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketEntitySnapshot);
		buffer.PutU16((ushort)(snapshots?.Count ?? 0));
		
		if (snapshots != null)
		{
			foreach (var snapshot in snapshots)
			{
				buffer.PutU32((uint)snapshot.EntityId);
				buffer.PutU16((ushort)snapshot.Data.Length);
				buffer.PutData(snapshot.Data);
			}
		}
		
		return buffer.DataArray;
	}

	public static System.Collections.Generic.List<EntitySnapshotData> DeserializeEntitySnapshots(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		
		if (buffer.GetAvailableBytes() < 3)
			return null;
		
		if (buffer.GetU8() != PacketEntitySnapshot)
			return null;
		
		var count = buffer.GetU16();
		var list = new System.Collections.Generic.List<EntitySnapshotData>();
		
		for (var i = 0; i < count; i++)
		{
			if (buffer.GetAvailableBytes() < 6)
				break;
			
			var entityId = (int)buffer.GetU32();
			var dataLength = buffer.GetU16();
			
			if (buffer.GetAvailableBytes() < dataLength)
				break;
			
			var data = buffer.GetData(dataLength);
			var error = (Error)(int)data[0];
			if (error == Error.Ok)
			{
				var bytes = data[1].AsByteArray();
				list.Add(new EntitySnapshotData
				{
					EntityId = entityId,
					Data = bytes
				});
			}
		}
		
		return list;
	}

	public static byte[] SerializeEntityDespawn(int entityId)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketEntityDespawn);
		buffer.PutU32((uint)entityId);
		return buffer.DataArray;
	}

	public static int DeserializeEntityDespawn(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		
		if (buffer.GetAvailableBytes() < 5)
			return 0;
		
		if (buffer.GetU8() != PacketEntityDespawn)
			return 0;
		
		return (int)buffer.GetU32();
	}

	public static byte[] SerializeScoreboard(IEnumerable<ScoreboardEntry> rows)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketScoreboard);

		var list = rows != null ? new List<ScoreboardEntry>(rows) : new List<ScoreboardEntry>();
		buffer.PutU16((ushort)list.Count);

		foreach (var row in list)
		{
			buffer.PutU32((uint)row.Id);
			buffer.PutU16((ushort)Mathf.Clamp(row.Kills, 0, ushort.MaxValue));
			buffer.PutU16((ushort)Mathf.Clamp(row.Deaths, 0, ushort.MaxValue));
		}

		return buffer.DataArray;
	}

	public static List<ScoreboardEntry> DeserializeScoreboard(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;

		if (buffer.GetAvailableBytes() < 3)
			return null;

		if (buffer.GetU8() != PacketScoreboard)
			return null;

		var count = buffer.GetU16();
		var entries = new List<ScoreboardEntry>();

		for (var i = 0; i < count; i++)
		{
			if (buffer.GetAvailableBytes() < 8)
				break;

			var peerId = (int)buffer.GetU32();
			var kills = (int)buffer.GetU16();
			var deaths = (int)buffer.GetU16();
			entries.Add(new ScoreboardEntry
			{
				Id = peerId,
				Kills = kills,
				Deaths = deaths
			});
		}

		return entries;
	}

	public partial class EntitySnapshotData : GodotObject
	{
		public int EntityId { get; set; }
		public byte[] Data { get; set; }
	}

	public partial class ScoreboardEntry : GodotObject
	{
		public int Id { get; set; }
		public int Kills { get; set; }
		public int Deaths { get; set; }
	}

	private static void WriteCarSnapshot(StreamPeerBuffer buffer, CarSnapshot snapshot)
	{
		if (snapshot == null)
		{
			buffer.PutU8(0);
			return;
		}

		buffer.PutU8(1);
		buffer.PutU32((uint)snapshot.Tick);
		var origin = snapshot.Transform.Origin;
		buffer.PutFloat(origin.X);
		buffer.PutFloat(origin.Y);
		buffer.PutFloat(origin.Z);
		var rotation = snapshot.Transform.Basis.GetRotationQuaternion();
		buffer.PutFloat(rotation.X);
		buffer.PutFloat(rotation.Y);
		buffer.PutFloat(rotation.Z);
		buffer.PutFloat(rotation.W);
		var lin = snapshot.LinearVelocity;
		buffer.PutFloat(lin.X);
		buffer.PutFloat(lin.Y);
		buffer.PutFloat(lin.Z);
		var ang = snapshot.AngularVelocity;
		buffer.PutFloat(ang.X);
		buffer.PutFloat(ang.Y);
		buffer.PutFloat(ang.Z);
	}

	private static CarSnapshot ReadCarSnapshot(StreamPeerBuffer buffer)
	{
		if (buffer.GetAvailableBytes() < 1) return null;
		var hasSnapshot = buffer.GetU8();
		if (hasSnapshot == 0)
			return null;
		if (buffer.GetAvailableBytes() < CarSnapshotPayloadBytes) return null;
		var snapshot = new CarSnapshot
		{
			Tick = (int)buffer.GetU32()
		};
		var origin = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		var rotation = new Quaternion(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.Transform = new Transform3D(new Basis(rotation), origin);
		snapshot.LinearVelocity = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.AngularVelocity = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		return snapshot;
	}

	private static void WritePlayerSnapshot(StreamPeerBuffer buffer, PlayerSnapshot snapshot)
	{
		if (snapshot == null)
		{
			buffer.PutU8(0);
			return;
		}

		buffer.PutU8(1);
		buffer.PutU32((uint)snapshot.Tick);
		var origin = snapshot.Transform.Origin;
		buffer.PutFloat(origin.X);
		buffer.PutFloat(origin.Y);
		buffer.PutFloat(origin.Z);
		var rotation = snapshot.Transform.Basis.GetRotationQuaternion();
		buffer.PutFloat(rotation.X);
		buffer.PutFloat(rotation.Y);
		buffer.PutFloat(rotation.Z);
		buffer.PutFloat(rotation.W);
		var vel = snapshot.Velocity;
		buffer.PutFloat(vel.X);
		buffer.PutFloat(vel.Y);
		buffer.PutFloat(vel.Z);
		buffer.PutFloat(snapshot.ViewYaw);
		buffer.PutFloat(snapshot.ViewPitch);
	}

	private static PlayerSnapshot ReadPlayerSnapshot(StreamPeerBuffer buffer)
	{
		if (buffer.GetAvailableBytes() < 1) return null;
		var hasSnapshot = buffer.GetU8();
		if (hasSnapshot == 0)
			return null;
		if (buffer.GetAvailableBytes() < PlayerSnapshotPayloadBytes) return null;
		var snapshot = new PlayerSnapshot
		{
			Tick = (int)buffer.GetU32()
		};
		var origin = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		var rotation = new Quaternion(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.Transform = new Transform3D(new Basis(rotation), origin);
		snapshot.Velocity = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.ViewYaw = buffer.GetFloat();
		snapshot.ViewPitch = buffer.GetFloat();
		return snapshot;
	}

	private static void WriteVehicleSnapshot(StreamPeerBuffer buffer, VehicleStateSnapshot snapshot)
	{
		if (snapshot == null)
		{
			buffer.PutU32(0);
			buffer.PutU32(0);
			buffer.PutU32(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			buffer.PutFloat(0);
			return;
		}

		buffer.PutU32((uint)snapshot.VehicleId);
		buffer.PutU32((uint)snapshot.Tick);
		buffer.PutU32((uint)snapshot.OccupantPeerId);
		var origin = snapshot.Transform.Origin;
		buffer.PutFloat(origin.X);
		buffer.PutFloat(origin.Y);
		buffer.PutFloat(origin.Z);
		var rotation = snapshot.Transform.Basis.GetRotationQuaternion();
		buffer.PutFloat(rotation.X);
		buffer.PutFloat(rotation.Y);
		buffer.PutFloat(rotation.Z);
		buffer.PutFloat(rotation.W);
		var lin = snapshot.LinearVelocity;
		buffer.PutFloat(lin.X);
		buffer.PutFloat(lin.Y);
		buffer.PutFloat(lin.Z);
		var ang = snapshot.AngularVelocity;
		buffer.PutFloat(ang.X);
		buffer.PutFloat(ang.Y);
		buffer.PutFloat(ang.Z);
	}

	private static VehicleStateSnapshot ReadVehicleSnapshot(StreamPeerBuffer buffer)
	{
		var snapshot = new VehicleStateSnapshot
		{
			VehicleId = (int)buffer.GetU32(),
			Tick = (int)buffer.GetU32(),
			OccupantPeerId = (int)buffer.GetU32()
		};
		var origin = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		var rotation = new Quaternion(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.Transform = new Transform3D(new Basis(rotation), origin);
		snapshot.LinearVelocity = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		snapshot.AngularVelocity = new Vector3(buffer.GetFloat(), buffer.GetFloat(), buffer.GetFloat());
		return snapshot;
	}

	public partial class PlayerStateData : GodotObject
	{
		public int PlayerId { get; set; }
		public PlayerStateSnapshot Snapshot { get; set; }
	}
}
