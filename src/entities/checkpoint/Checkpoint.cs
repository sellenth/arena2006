using Godot;

public partial class Checkpoint : Area3D
{
	[Export] public int CheckpointIndex { get; set; } = 0;
	[Export] public bool IsFinishLine { get; set; } = false;
	[Export] public NodePath RaceManagerPath { get; set; } = new NodePath();

	private bool passed = false;
	private RaceManager? raceManager;
	private MeshInstance3D? visualMesh;
	private AudioStreamPlayer3D? checkpointAudio;
	private float resetGraceTimer = 0.0f;
	private float resetGraceDuration = 1.0f; // Don't play audio for 1 second after reset
	private float lastPlayTime = -10.0f;
	private const float MinPlayInterval = 0.5f; // Debounce identical plays

	public override async void _Ready()
	{
		BodyEntered += _OnBodyEntered;
		
		// Find race manager
		TryConnectRaceManager();
		
		// Find visual mesh for effects
		visualMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		
		// Wait a frame to ensure everything is initialized
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Create audio player for checkpoint sound
		SetupAudio();

		// Listen for validated checkpoint passes from RaceManager
		TryConnectRaceManager();
		if (raceManager == null)
		{
			GD.PushWarning($"[Checkpoint] Unable to locate RaceManager for checkpoint '{Name}'. Configure 'Race Manager Path' or ensure a RaceManager exists in the scene.");
		}
		
		// Set checkpoint color based on index
		if (visualMesh != null && visualMesh.MaterialOverride != null)
		{
			// Create a unique material instance for this checkpoint
			var material = (StandardMaterial3D)visualMesh.MaterialOverride.Duplicate();
			visualMesh.MaterialOverride = material;
			
			if (IsFinishLine)
			{
				material.AlbedoColor = Colors.Yellow * new Color(1, 1, 1, 0.4f);
				material.EmissionEnabled = true;
				material.Emission = Colors.Yellow * 0.5f;
			}
			else
			{
				// Different colors for different checkpoints
				var colors = new Color[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Purple };
				var checkpointColor = colors[CheckpointIndex % colors.Length];
				material.AlbedoColor = checkpointColor * new Color(1, 1, 1, 0.4f);
				material.EmissionEnabled = true;
				material.Emission = checkpointColor * 0.3f;
			}
		}
	}

	public override void _Process(double delta)
	{
		if (resetGraceTimer > 0.0f)
		{
			resetGraceTimer -= (float)delta;
		}
	}

	public void OnPlayerResetToCheckpoint()
	{
		// Called when player resets to this checkpoint position
		resetGraceTimer = resetGraceDuration;
		//GD.Print("Checkpoint ", CheckpointIndex, ": Reset grace period activated");
	}

	private void SetupAudio()
	{
		checkpointAudio = new AudioStreamPlayer3D();
		var audioStream = GD.Load<AudioStream>("res://sounds/checkpoint.mp3");
		if (audioStream != null)
		{
			checkpointAudio.Stream = audioStream;
			//GD.Print("Checkpoint ", CheckpointIndex, ": Audio stream loaded successfully");
		}
		else
		{
			//GD.Print("Checkpoint ", CheckpointIndex, ": Failed to load audio stream!");
			return;
		}
		
		checkpointAudio.Bus = AudioSettingsManager.FxBusName;
		checkpointAudio.VolumeDb = -30.0f;
		checkpointAudio.MaxDistance = 100.0f;
		checkpointAudio.PitchScale = 1.0f;
		checkpointAudio.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
		AddChild(checkpointAudio);
		//GD.Print("Checkpoint ", CheckpointIndex, ": Audio player created and added to scene");
	}

	private void PlayCheckpointSound()
	{
		//GD.Print("PlayCheckpointSound called for checkpoint ", CheckpointIndex);
		
		// Debounce
		float now = Time.GetTicksMsec() / 1000.0f;
		if (now - lastPlayTime < MinPlayInterval)
		{
			return;
		}

		// Use the 3D audio player at the checkpoint location
		if (checkpointAudio != null && checkpointAudio.Stream != null && IsInsideTree())
		{
			GD.Print("Playing 3D audio...");
			checkpointAudio.Play();
			if (checkpointAudio.Playing)
			{
				GD.Print("3D audio is playing!");
				lastPlayTime = now;
				return;
			}
			else
			{
				GD.Print("3D audio failed to play");
			}
		}
	}

	private void TryConnectRaceManager()
	{
		var manager = ResolveRaceManager();
		if (manager == null)
		{
			return;
		}

		var callable = new Callable(this, nameof(OnCheckpointPassed));
		if (!manager.IsConnected(RaceManager.SignalName.CheckpointPassed, callable))
		{
			manager.Connect(RaceManager.SignalName.CheckpointPassed, callable);
		}
	}

	private RaceManager? ResolveRaceManager()
	{
		if (raceManager != null && GodotObject.IsInstanceValid(raceManager))
		{
			return raceManager;
		}

		RaceManager? manager = null;

		if (!RaceManagerPath.IsEmpty)
		{
			manager = GetNodeOrNull<RaceManager>(RaceManagerPath);
		}

		if (manager == null)
		{
			manager = GetNodeOrNull<RaceManager>("/root/World/RaceManager");
		}

		if (manager == null)
		{
			var root = GetTree()?.Root;
			manager = root?.FindChild("RaceManager", true, false) as RaceManager;
		}

		raceManager = manager;
		return raceManager;
	}

	private void OnCheckpointPassed(int index, float splitTime)
	{
		// Only play sound for the checkpoint that was actually validated by RaceManager
		if (index == CheckpointIndex)
		{
			// Respect reset grace period
			if (resetGraceTimer <= 0.0f)
			{
				PlayCheckpointSound();
			}
			else
			{
				GD.Print("Skipping checkpoint sound - in reset grace period (", resetGraceTimer, "s remaining)");
			}
		}
	}

	private void _OnBodyEntered(Node3D body)
	{
		if (body is not RigidBody3D car)
		{
			return;
		}

		var position = car.GlobalPosition;
		var rotation = car.GlobalRotation;
		var velocity = car.LinearVelocity;

		if (!Multiplayer.HasMultiplayerPeer())
		{
			GD.PrintErr("[Checkpoint] Multiplayer peer not ready; ignoring checkpoint trigger.");
			return;
		}

		if (Multiplayer.IsServer())
		{
			if (!TryResolvePeerId(body, out var peerId))
			{
				return;
			}
			//var lobby = GetTree().Root.GetNodeOrNull<MultiplayerLobby>("/root/World/MultiplayerLobby");
			//obby?.ServerHandleCheckpoint(peerId, CheckpointIndex, position, rotation, velocity, IsFinishLine);
		}
		// Clients wait for authoritative RPC from server.
		return;
	}

	private bool TryResolvePeerId(Node body, out long peerId)
	{
		peerId = 0;
		if (!Multiplayer.HasMultiplayerPeer())
		{
			return false;
		}

		var name = body.Name.ToString();

		if (name == "LocalCar")
		{
			peerId = Multiplayer.GetUniqueId();
			return true;
		}

		const string prefix = "ServerCar_";
		if (name.StartsWith(prefix) && long.TryParse(name.Substring(prefix.Length), out var id))
		{
			peerId = id;
			return true;
		}

		return false;
	}
}
