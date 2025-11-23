using Godot;

public static class NetworkConfig
{
	public const int DefaultPort = 45000;
	public const int PeerTimeoutMsec = 5000;
	public const int PlayerEntityIdOffset = 3000;
	public const int VehicleEntityIdOffset = 2000;
	public const float PlayerSpawnJitterRadius = 5.0f;
	public const int MaxPredictionHistory = 256;
	public const float PlayerSnapDistance = 2.5f;
	public const float PlayerSmallCorrectionBlend = 0.25f;
}

