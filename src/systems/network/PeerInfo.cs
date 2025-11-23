using Godot;

public partial class PeerInfo : GodotObject
{
	public int Id { get; set; }
	public PacketPeerUdp Peer { get; set; }
	public CarInputState CarInputState { get; set; } = new CarInputState();
	public PlayerInputState PlayerInputState { get; set; } = new PlayerInputState();
	public CarSnapshot LastCarSnapshot { get; set; }
	public PlayerSnapshot LastPlayerSnapshot { get; set; }
	public long LastSeenMsec { get; set; }
	public PlayerCharacter PlayerCharacter { get; set; }
	public PlayerMode Mode { get; set; } = PlayerMode.Foot;
	public Transform3D PlayerRestTransform { get; set; } = Transform3D.Identity;
	public int ControlledVehicleId { get; set; }
	public int Kills { get; set; }
	public int Deaths { get; set; }

	public PeerInfo(int peerId, PacketPeerUdp peerRef)
	{
		Id = peerId;
		Peer = peerRef;
		LastSeenMsec = (long)Time.GetTicksMsec();
	}

	public void Touch()
	{
		LastSeenMsec = (long)Time.GetTicksMsec();
	}
}

