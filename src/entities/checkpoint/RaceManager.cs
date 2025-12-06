using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class RaceManager : Node
{
	[Signal]
	public delegate void LapCompletedEventHandler(float lapTime, int lapNumber);

	[Signal]
	public delegate void RaceStartedEventHandler();

	[Signal]
	public delegate void RaceFinishedEventHandler(float totalTime);

	[Signal]
	public delegate void CheckpointPassedEventHandler(int checkpointIndex, float splitTime);

	// Race configuration
	[Export] public int LapsPerRace { get; set; } = 1;
	[Export] public int CheckpointCount { get; set; } = 3;
	[Export] public float CountdownDuration { get; set; } = 3.0f;

	// Timing data
	private float raceStartTime = 0.0f;
	private float lapStartTime = 0.0f;
	private int currentLap = 0;
	private List<bool> checkpointsPassed = new List<bool>();
	private List<float> checkpointTimes = new List<float>();

	// Best times
	private float bestLapTime = float.PositiveInfinity;
	private List<float> bestCheckpointTimes = new List<float>();

	// Race state
	public enum RaceState { Waiting, Countdown, Racing, Finished }
	private RaceState raceState = RaceState.Racing;
	public RaceState CurrentRaceState => raceState;
	public float CountdownTimer => countdownTimer;
	public int CurrentLap => currentLap;
	public float BestLapTime => bestLapTime;
	private float countdownTimer = 0.0f;
	public float LastLapTime { get; private set; } = 0.0f;

	// Lap data for splits
	private List<float> lapTimes = new List<float>();
	private int currentCheckpointIndex = 0;

	// Reset data - store state for each checkpoint
	private Dictionary<int, CheckpointState> checkpointStates = new Dictionary<int, CheckpointState>();
	private int lastCheckpointIndex = -1;
	private Vector3 startPosition = Vector3.Zero;
	private Vector3 startRotation = Vector3.Zero;

	private class CheckpointState
	{
		public Vector3 Position { get; set; }
		public Vector3 Rotation { get; set; }
		public Vector3 Velocity { get; set; }
		public bool Passed { get; set; }
	}

	private void StoreCheckpointState(int checkpointIndex, Vector3 playerPosition, Vector3 playerRotation, Vector3 playerVelocity, bool isRaceUpdate)
	{
		if (!checkpointStates.TryGetValue(checkpointIndex, out var checkpointState))
		{
			checkpointState = new CheckpointState();
			checkpointStates[checkpointIndex] = checkpointState;
		}

		bool wasPassed = checkpointState.Passed;
		checkpointState.Position = playerPosition;
		checkpointState.Rotation = playerRotation;
		checkpointState.Velocity = playerVelocity;
		checkpointState.Passed = true;

		if (!wasPassed)
		{
			if (isRaceUpdate)
			{
				GD.Print($"First pass of checkpoint {checkpointIndex}! Stored state - pos: {playerPosition} vel: {playerVelocity}");
			}
			else
			{
				GD.Print($"Checkpoint {checkpointIndex} state stored (practice mode)");
			}
		}
	}

	public override void _Ready()
	{
		// Find checkpoints from the scene
		var checkpointsNode = GetNodeOrNull("/root/World/Checkpoints");
		if (checkpointsNode != null)
		{
			CheckpointCount = checkpointsNode.GetChildCount();
			GD.Print($"Found {CheckpointCount} checkpoints in scene");
		}

		// Initialize checkpoint tracking
		for (int i = 0; i < CheckpointCount; i++)
		{
			checkpointsPassed.Add(false);
			checkpointTimes.Add(0.0f);
			bestCheckpointTimes.Add(float.PositiveInfinity);
			// Initialize state storage for each checkpoint
			checkpointStates[i] = new CheckpointState
			{
				Position = Vector3.Zero,
				Rotation = Vector3.Zero,
				Velocity = Vector3.Zero,
				Passed = false
			};
		}
	}

	public override void _Process(double delta)
	{

		switch (raceState)
		{
			case RaceState.Countdown:
				countdownTimer -= (float)delta;
				if (countdownTimer <= 0.0f)
				{
					StartRace();
				}
				break;
			case RaceState.Racing:
				// Update race time continuously
				break;
		}
	}

	public void StartCountdown()
	{
		if (raceState != RaceState.Waiting)
			return;

		raceState = RaceState.Countdown;
		countdownTimer = CountdownDuration;
		ResetRaceData();
	}

	public void PrepareRace()
	{
		raceState = RaceState.Waiting;
		countdownTimer = CountdownDuration;
		ResetRaceData();
	}

	private void StartRace()
	{
		raceState = RaceState.Racing;
		raceStartTime = Time.GetTicksMsec() / 1000.0f;
		lapStartTime = raceStartTime;
		currentLap = 1;
		EmitSignal(SignalName.RaceStarted);
	}

	public void PassCheckpoint(int checkpointIndex, Vector3 playerPosition, Vector3 playerRotation, Vector3 playerVelocity)
	{
		if (raceState != RaceState.Racing)
		{
			StoreCheckpointState(checkpointIndex, playerPosition, playerRotation, playerVelocity, false);
			lastCheckpointIndex = checkpointIndex;
			return;
		}

		// Ensure checkpoints are passed in order
		if (checkpointIndex != currentCheckpointIndex)
		{
			GD.Print($"Wrong checkpoint! Expected {currentCheckpointIndex} but got {checkpointIndex}");
			return;
		}

		StoreCheckpointState(checkpointIndex, playerPosition, playerRotation, playerVelocity, true);
		float currentTime = Time.GetTicksMsec() / 1000.0f;
		float splitTime = currentTime - lapStartTime;

		checkpointsPassed[checkpointIndex] = true;
		checkpointTimes[checkpointIndex] = splitTime;

		// Update last checkpoint for reset purposes
		lastCheckpointIndex = checkpointIndex;

		// Update best checkpoint time
		if (splitTime < bestCheckpointTimes[checkpointIndex])
		{
			bestCheckpointTimes[checkpointIndex] = splitTime;
		}

		EmitSignal(SignalName.CheckpointPassed, checkpointIndex, splitTime);

		// Move to next checkpoint
		currentCheckpointIndex = (currentCheckpointIndex + 1) % CheckpointCount;

		// Check if lap is complete
		if (checkpointIndex == CheckpointCount - 1)
		{
			CompleteLap();
		}
	}

	private void CompleteLap()
	{
		float currentTime = Time.GetTicksMsec() / 1000.0f;
		float lapTime = currentTime - lapStartTime;

		lapTimes.Add(lapTime);
		LastLapTime = lapTime; // Store the last lap time

		// Update best lap time
		if (lapTime < bestLapTime)
		{
			bestLapTime = lapTime;
		}

		EmitSignal(SignalName.LapCompleted, lapTime, currentLap);

		// Reset lap timer immediately for the next lap
		lapStartTime = currentTime;

		// Check if race is complete
		if (currentLap >= LapsPerRace)
		{
			FinishRace();
		}
		else
		{
			// Start next lap
			currentLap++;
			ResetCheckpointData();
			currentCheckpointIndex = 0;

			// Respawn player at start position after completing a lap
			RespawnPlayerAtStart();
		}
	}

	private void FinishRace()
	{
		raceState = RaceState.Finished;
		float totalTime = Time.GetTicksMsec() / 1000.0f - raceStartTime;
		EmitSignal(SignalName.RaceFinished, totalTime);
	}

	private void ResetRaceData()
	{
		currentLap = 0;
		lapTimes.Clear();
		ResetCheckpointData();
		currentCheckpointIndex = 0;
	}

	private void ResetCheckpointData()
	{
		for (int i = 0; i < CheckpointCount; i++)
		{
			checkpointsPassed[i] = false;
			checkpointTimes[i] = 0.0f;
			// Reset checkpoint states for new lap
			checkpointStates[i].Passed = false;
		}
		lastCheckpointIndex = -1;
	}

	public float GetCurrentRaceTime()
	{
		if (raceState != RaceState.Racing)
			return 0.0f;
		return Time.GetTicksMsec() / 1000.0f - raceStartTime;
	}

	public float GetCurrentLapTime()
	{
		if (raceState != RaceState.Racing)
			return 0.0f;
		return Time.GetTicksMsec() / 1000.0f - lapStartTime;
	}

	public float GetSplitDifference(int checkpointIndex)
	{
		if (checkpointIndex >= CheckpointCount || checkpointIndex < 0)
			return 0.0f;

		float currentSplit = checkpointTimes[checkpointIndex];
		float bestSplit = bestCheckpointTimes[checkpointIndex];

		if (float.IsPositiveInfinity(bestSplit))
			return 0.0f;

		return currentSplit - bestSplit;
	}

	public string FormatTime(float timeSeconds)
	{
		if (float.IsPositiveInfinity(timeSeconds))
			return "--:--.---";

		int minutes = (int)timeSeconds / 60;
		int seconds = (int)timeSeconds % 60;
		int milliseconds = (int)((timeSeconds - (int)timeSeconds) * 1000);

		return $"{minutes:D2}:{seconds:D2}.{milliseconds:D3}";
	}

	public void SetStartPosition(Vector3 position, Vector3 rotation)
	{
		startPosition = position;
		startRotation = rotation;
		GD.Print($"Start position set to: {position}");
	}

	public Dictionary<string, object> GetResetData()
	{
		// Reset the lap timer when player resets
		ResetLapTimer();
		
		// If player has passed any checkpoints, return the last checkpoint state
		if (lastCheckpointIndex >= 0 && checkpointStates.ContainsKey(lastCheckpointIndex))
		{
			var checkpointState = checkpointStates[lastCheckpointIndex];
			if (checkpointState.Passed)
			{
				GD.Print($"Resetting to checkpoint {lastCheckpointIndex} state");
				// Notify the checkpoint that player is resetting to it
				NotifyCheckpointReset(lastCheckpointIndex);
				return new Dictionary<string, object>
				{
					{ "position", checkpointState.Position },
					{ "rotation", checkpointState.Rotation },
					{ "velocity", checkpointState.Velocity }
				};
			}
		}

		// Otherwise return start position
		if (startPosition != Vector3.Zero)
		{
			GD.Print("Resetting to start position");
			return new Dictionary<string, object>
			{
				{ "position", startPosition },
				{ "rotation", startRotation },
				{ "velocity", Vector3.Zero }
			};
		}

		// Try world spawn point as a safe fallback before current position
		var spawnPoint = GetNodeOrNull<Marker3D>("/root/World/map/CarSpawnPoint")
					?? GetTree().CurrentScene.GetNodeOrNull<Marker3D>("CarSpawnPoint");
		if (spawnPoint != null)
		{
			GD.Print("Resetting to CarSpawnPoint fallback");
			return new Dictionary<string, object>
			{
				{ "position", spawnPoint.GlobalPosition },
				{ "rotation", spawnPoint.GlobalRotation },
				{ "velocity", Vector3.Zero }
			};
		}

		// Fallback: use player's current position
		GD.Print("Warning: No valid reset position, using current position");
		var player = GetTree().CurrentScene.GetNodeOrNull<Node3D>("LocalCar");
		if (player != null)
		{
			return new Dictionary<string, object>
			{
				{ "position", player.GlobalPosition },
				{ "rotation", player.GlobalRotation },
				{ "velocity", Vector3.Zero }
			};
		}

		return new Dictionary<string, object>
		{
			{ "position", Vector3.Zero },
			{ "rotation", Vector3.Zero },
			{ "velocity", Vector3.Zero }
		};
	}

	private void NotifyCheckpointReset(int checkpointIndex)
	{
		// Notify a specific checkpoint that the player is resetting to it
		var checkpointsNode = GetNodeOrNull("/root/World/Checkpoints");
		if (checkpointsNode != null)
		{
			string checkpointName = $"Checkpoint{checkpointIndex}";
			var checkpointNode = checkpointsNode.GetNodeOrNull(checkpointName);
			if (checkpointNode != null && checkpointNode.HasMethod("on_player_reset_to_checkpoint"))
			{
				checkpointNode.Call("on_player_reset_to_checkpoint");
				GD.Print($"Notified checkpoint {checkpointIndex} of player reset");
			}
			else
			{
				GD.Print($"Could not find checkpoint node: {checkpointName}");
			}
		}
	}

	private void RespawnPlayerAtStart()
	{
		// Get the player car
		var player = GetTree().CurrentScene.GetNodeOrNull<RigidBody3D>("LocalCar");
		if (player == null)
		{
			GD.PrintErr("Could not find LocalCar to respawn!");
			return;
		}

		// Get the spawn point from the world scene
		var spawnPoint = GetNodeOrNull<Marker3D>("/root/World/map/CarSpawnPoint");
		if (spawnPoint == null)
		{
			GD.PrintErr("Could not find CarSpawnPoint!");
			return;
		}

		// Reset player position and rotation
		player.GlobalPosition = spawnPoint.GlobalPosition;
		player.GlobalRotation = spawnPoint.GlobalRotation;

		// Reset velocity if it's a RigidBody3D
		player.LinearVelocity = Vector3.Zero;
		player.AngularVelocity = Vector3.Zero;

		GD.Print($"Player respawned at CarSpawnPoint: {spawnPoint.GlobalPosition}");
	}

	public void OnFinishLineCrossed()
	{
		if (raceState == RaceState.Racing)
		{
			CompleteLap();
		}
	}
	
	public void ResetLapTimer()
	{
		// Reset the lap start time to current time when player resets
		if (raceState == RaceState.Racing)
		{
			lapStartTime = Time.GetTicksMsec() / 1000.0f;
			GD.Print("Lap timer reset due to player reset");
		}
	}
	
	public void RestartRace()
	{
		// Reset all race data
		ResetRaceData();
		
		// Reset checkpoint states
		foreach (var kvp in checkpointStates)
		{
			kvp.Value.Passed = false;
		}
		lastCheckpointIndex = -1;
		
		// Reset lap timer and last lap time
		LastLapTime = 0.0f;
		
		// Start race immediately (no countdown)
		raceState = RaceState.Racing;
		raceStartTime = Time.GetTicksMsec() / 1000.0f;
		lapStartTime = raceStartTime;
		currentLap = 1;
		
		EmitSignal(SignalName.RaceStarted);
		GD.Print("Race restarted instantly!");
	}
}
