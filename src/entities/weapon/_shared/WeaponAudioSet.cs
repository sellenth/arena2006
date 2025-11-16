using Godot;

public partial class WeaponAudioSet : Resource
{
	[Export] public AudioStream? Stream { get; set; }
	[Export] public float VolumeDb { get; set; } = -6.0f;
	[Export] public float MaxDistance { get; set; } = 64.0f;
	[Export] public float RandomPitchMin { get; set; } = 0.96f;
	[Export] public float RandomPitchMax { get; set; } = 1.06f;
	[Export] public bool Spatial { get; set; } = true;

	public AudioStreamPlayer3D Create3D(Node owner, Vector3 position)
	{
		var player = new AudioStreamPlayer3D
		{
			Stream = Stream,
			VolumeDb = VolumeDb,
			MaxDistance = MaxDistance,
			UnitSize = 2.0f,
			AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance,
			Bus = AudioSettingsManager.WeaponsBusName
		};

		player.PitchScale = (float)GD.RandRange(RandomPitchMin, RandomPitchMax);
		owner.GetTree().CurrentScene?.AddChild(player);
		player.GlobalPosition = position;
		return player;
	}
}
