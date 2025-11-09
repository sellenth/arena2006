using Godot;

public partial class PlayerStateSnapshot : RefCounted
{
	public int Tick { get; set; }
	public PlayerMode Mode { get; set; } = PlayerMode.Vehicle;
	public CarSnapshot CarSnapshot { get; set; }
	public FootSnapshot FootSnapshot { get; set; }
}
