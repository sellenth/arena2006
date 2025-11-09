using Godot;

public static partial class NetworkSerializer
{
	public const byte PacketCarInput = 1;
	public const byte PacketPlayerState = 2;
	public const byte PacketWelcome = 3;
	public const byte PacketRemovePlayer = 4;
	public const byte PacketFootInput = 5;

	public const int CarSnapshotPayloadBytes = 4 + 12 + 16 + 12 + 12;
	public const int FootSnapshotPayloadBytes = 4 + 12 + 16 + 12 + 8;

	public static byte[] SerializeCarInput(CarInputState state)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketCarInput);
		buffer.PutU32((uint)state.Tick);
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
		if (buffer.GetAvailableBytes() < 4 + 4 + 4 + 1 + 1 + 1 + 1) return null;
		var state = new CarInputState
		{
			Tick = (int)buffer.GetU32(),
			Throttle = buffer.GetFloat(),
			Steer = buffer.GetFloat(),
			Handbrake = buffer.GetU8() == 1,
			Brake = buffer.GetU8() == 1,
			Respawn = buffer.GetU8() == 1,
			Interact = buffer.GetU8() == 1
		};
		return state;
	}

	public static byte[] SerializeFootInput(FootInputState state)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketFootInput);
		buffer.PutU32((uint)state.Tick);
		buffer.PutFloat(state.MoveInput.X);
		buffer.PutFloat(state.MoveInput.Y);
		buffer.PutU8((byte)(state.Jump ? 1 : 0));
		buffer.PutU8((byte)(state.Interact ? 1 : 0));
		buffer.PutFloat(state.LookDelta.X);
		buffer.PutFloat(state.LookDelta.Y);
		return buffer.DataArray;
	}

	public static FootInputState DeserializeFootInput(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 1) return null;
		var packetType = buffer.GetU8();
		if (packetType != PacketFootInput) return null;
		if (buffer.GetAvailableBytes() < 4 + 4 + 4 + 1 + 1 + 4 + 4) return null;
		var state = new FootInputState
		{
			Tick = (int)buffer.GetU32(),
			MoveInput = new Vector2(buffer.GetFloat(), buffer.GetFloat()),
			Jump = buffer.GetU8() == 1,
			Interact = buffer.GetU8() == 1,
			LookDelta = new Vector2(buffer.GetFloat(), buffer.GetFloat())
		};
		return state;
	}

	public static byte[] SerializePlayerState(int playerId, PlayerStateSnapshot snapshot)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.PutU8(PacketPlayerState);
		buffer.PutU32((uint)playerId);
		buffer.PutU8((byte)snapshot.Mode);
		WriteCarSnapshot(buffer, snapshot.CarSnapshot);
		WriteFootSnapshot(buffer, snapshot.FootSnapshot);
		return buffer.DataArray;
	}

	public static PlayerStateData DeserializePlayerState(byte[] packet)
	{
		var buffer = new StreamPeerBuffer();
		buffer.BigEndian = false;
		buffer.DataArray = packet;
		if (buffer.GetAvailableBytes() < 1) return null;
		var packetType = buffer.GetU8();
		if (packetType != PacketPlayerState) return null;
		if (buffer.GetAvailableBytes() < 5) return null;
		var data = new PlayerStateData
		{
			PlayerId = (int)buffer.GetU32()
		};
		if (buffer.GetAvailableBytes() < 1) return null;
		var mode = (PlayerMode)buffer.GetU8();
		var snapshot = new PlayerStateSnapshot
		{
			Mode = mode,
			CarSnapshot = ReadCarSnapshot(buffer),
			FootSnapshot = ReadFootSnapshot(buffer)
		};
		data.Snapshot = snapshot;
		return data;
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

	private static void WriteFootSnapshot(StreamPeerBuffer buffer, FootSnapshot snapshot)
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

	private static FootSnapshot ReadFootSnapshot(StreamPeerBuffer buffer)
	{
		if (buffer.GetAvailableBytes() < 1) return null;
		var hasSnapshot = buffer.GetU8();
		if (hasSnapshot == 0)
			return null;
		if (buffer.GetAvailableBytes() < FootSnapshotPayloadBytes) return null;
		var snapshot = new FootSnapshot
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

	public partial class PlayerStateData : GodotObject
	{
		public int PlayerId { get; set; }
		public PlayerStateSnapshot Snapshot { get; set; }
	}
}

