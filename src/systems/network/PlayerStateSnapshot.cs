using Godot;

public partial class PlayerStateSnapshot : RefCounted
{
	public int Tick { get; set; }
	public PlayerMode Mode { get; set; } = PlayerMode.Foot;
	public int VehicleId { get; set; }
	public int LastProcessedInputTick { get; set; }
	public CarSnapshot CarSnapshot { get; set; }
	public PlayerSnapshot PlayerSnapshot { get; set; }
}
