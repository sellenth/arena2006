using Godot;
using System.Linq;

public partial class DebugUiController : Node
{
	private RaycastCar _car;
	private RaycastCar _fallbackCar;
	private PlayerCharacter _player;
	private NetworkController _network;
	private GameModeManager _gameModeManager;
	private Control _debugBoxContainer;

	private CheckBox _allForcesCB;
	private CheckBox _pullForcesCB;
	private CheckBox _handBreakCB;
	private CheckBox _slippingCB;
	private Label _frameRateLabel;
	private Label _timeScaleLabel;
	private Label _gameModeLabel;
	private Label _sndInfoLabel;
	private Label _weaponLabel;
	private Label _ammoLabel;
	private Label _speedLabel;
	private ProgressBar _motorRatio;
	private Label _accelLabel;
	private ProgressBar _turnRatio;
	private Label _healthLabel;
	private Label _armorLabel;
	private Label _moveStateLabel;
	private CanvasLayer _deathUI;

	public override void _Ready()
	{
		_car = GetNodeOrNull<RaycastCar>("../Car");
		_fallbackCar = _car;
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");

		if (_network != null)
		{
			_network.LocalCarChanged += OnLocalCarChanged;
			OnLocalCarChanged(_network.LocalCar);
			_player = _network.LocalPlayer;
		}

		if (_player == null)
		{
			var root = GetTree().Root;
			_player = root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;
		}

		var canvasLayer = GetNode<CanvasLayer>("CanvasLayer");
		_debugBoxContainer = GetNode<Control>("CanvasLayer/DebugUi");
		_debugBoxContainer.Visible = OS.IsDebugBuild();

		var topContainer = canvasLayer.GetNode<VBoxContainer>("DebugUi/TOP");
		_allForcesCB = topContainer.GetNode<CheckBox>("AllForcesCB");
		_pullForcesCB = topContainer.GetNode<CheckBox>("PullForcesCB");
		_handBreakCB = topContainer.GetNode<CheckBox>("HandBreakCB");
		_slippingCB = topContainer.GetNode<CheckBox>("SlippingCB");
		_frameRateLabel = topContainer.GetNode<Label>("FramerateLabel");
		_timeScaleLabel = topContainer.GetNode<Label>("TimeScaleLabel");
		_gameModeLabel = topContainer.GetNode<Label>("GameModeLabel");

		_gameModeManager = GameModeManager.Instance;
		if (_gameModeManager != null)
		{
			_gameModeManager.GameModeChanged += OnGameModeChanged;
			UpdateGameModeLabel();
		}

		_sndInfoLabel = new Label();
		_sndInfoLabel.Name = "SndInfoLabel";
		_sndInfoLabel.Visible = false;
		var gameModeIdx = _gameModeLabel.GetIndex();
		topContainer.AddChild(_sndInfoLabel);
		topContainer.MoveChild(_sndInfoLabel, gameModeIdx + 1);

		_weaponLabel = topContainer.GetNode<Label>("WeaponLabel");
		_ammoLabel = canvasLayer.GetNode<Label>("BOTTOM/AmmoLabel");
		_speedLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/SpeedLabel");
		_motorRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/MotorRatio");
		_accelLabel = canvasLayer.GetNode<Label>("RIGHT_TOP/AccelLabel");
		_turnRatio = canvasLayer.GetNode<ProgressBar>("RIGHT_TOP/TurnRatio");
		_healthLabel = canvasLayer.GetNode<Label>("BOTTOM/HealthLabel");
		_armorLabel = canvasLayer.GetNode<Label>("BOTTOM/ArmorLabel");
		_moveStateLabel = topContainer.GetNodeOrNull<Label>("MoveStateLabel");

		_deathUI = GetNodeOrNull<CanvasLayer>("DeathUI");
		if (_deathUI != null)
			_deathUI.Visible = false;
	}

	public override void _ExitTree()
	{
		if (_network != null)
			_network.LocalCarChanged -= OnLocalCarChanged;

		if (_gameModeManager != null)
			_gameModeManager.GameModeChanged -= OnGameModeChanged;
	}

	public override void _Process(double delta)
	{
		var isFootMode = _network != null && _network.CurrentClientMode == PlayerMode.Foot;

		if (isFootMode && _player != null)
		{
			var speed = _player.Velocity.Length();
			_speedLabel.Text = $"Speed: {speed:F2} m/s";
			_healthLabel.Text = $"Health: {_player.Health}/{_player.MaxHealth}";
			_armorLabel.Text = $"Armor: {_player.Armor}/{_player.MaxArmor}";
			if (_moveStateLabel != null)
			{
				_moveStateLabel.Text = $"Move: {_player.GetMovementStateName()}";
			}
		}
		else if (_car != null)
		{
			_handBreakCB.ButtonPressed = _car.HandBreak;
			_slippingCB.ButtonPressed = _car.IsSlipping;

			var speed = _car.LinearVelocity.Length();
			_speedLabel.Text = $"Speed: {speed:F2} m/s";

			_motorRatio.Value = Mathf.Abs(_car.MotorInput);

			_turnRatio.Value = -_car.SteerInput;

			var accelForce = _car.Acceleration * _car.MotorInput;
			_accelLabel.Text = $"AccelForce: {accelForce:F1}";

			_healthLabel.Text = $"Health: {_car.Health}/{_car.MaxHealth}";
			_armorLabel.Text = $"Armor: {_car.Armor}/{_car.MaxArmor}";
		}

		_frameRateLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}";
		_timeScaleLabel.Text = $"Time Scale: {Engine.TimeScale:F2}";

		UpdateDeathUI();

		var weaponInventory = _player?.GetNodeOrNull<WeaponInventory>("WeaponInventory");
		var weaponType = weaponInventory?.EquippedType ?? WeaponType.None;
		var mag = weaponInventory?.Equipped?.Magazine ?? 0;
		var reserve = weaponInventory?.Equipped?.Reserve ?? 0;
		var magSize = weaponInventory?.Equipped?.Definition?.MagazineSize ?? 0;
		_weaponLabel.Text = $"Weapon: {weaponType.ToString()}";
		if (_ammoLabel != null)
		{
			_ammoLabel.Text = $"Ammo: {mag}/{magSize} | Total: {mag + reserve}";
		}

		UpdateGameModeLabel();
		UpdateSndInfoLabel();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_debug_ui"))
		{
			_debugBoxContainer.Visible = !_debugBoxContainer.Visible;
		}
	}

	private void OnLocalCarChanged(RaycastCar car)
	{
		_car = car ?? _fallbackCar;
	}

	private void UpdateDeathUI()
	{
		if (_deathUI == null)
			return;

		if (_player == null && _network != null)
			_player = _network.LocalPlayer;

		var shouldShow = _player != null && _player.IsDead;
		_deathUI.Visible = shouldShow;
	}

	private void OnGameModeChanged(string modeId, string displayName)
	{
		UpdateGameModeLabel();
	}

	private void UpdateGameModeLabel()
	{
		if (_gameModeLabel == null)
			return;

		if (_gameModeManager != null && _gameModeManager.ActiveMode != null)
		{
			var modeName = _gameModeManager.ActiveMode.DisplayName;
			var phaseName = GetCurrentPhaseName();
			_gameModeLabel.Text = $"Game Mode: {modeName} [{phaseName}]";
		}
		else
		{
			_gameModeLabel.Text = "Game Mode: None";
		}
	}

	private void UpdateSndInfoLabel()
	{
		if (_sndInfoLabel == null)
			return;

		if (_gameModeManager == null)
			_gameModeManager = GameModeManager.Instance;

		var matchStateClient = MatchStateClient.Instance;
		var isServer = _network != null && _network.IsServer;
		
		// Check if we are in S&D mode
		string modeId = isServer ? _gameModeManager?.ActiveMode?.Id : matchStateClient?.CurrentModeId;
		if (modeId != "search_and_destroy")
		{
			_sndInfoLabel.Visible = false;
			return;
		}
		
		_sndInfoLabel.Visible = true;

		int attackersScore = 0;
		int defendersScore = 0;
		string attackersName = "Attackers";
		string defendersName = "Defenders";
		int roundsToWin = 0;
		int currentRound = 0;
		bool teamsSwapped = false;
		ObjectiveState objState = new ObjectiveState();

		if (isServer)
		{
			// Server source
			if (_gameModeManager?.ActiveMode is SearchAndDestroyMode snd)
			{
				var attackersTeam = snd.GetAttackersTeamId();
				var defendersTeam = snd.GetDefendersTeamId();
				attackersName = _gameModeManager.TeamManager.GetTeamDefinition(attackersTeam)?.Name ?? "Attackers";
				defendersName = _gameModeManager.TeamManager.GetTeamDefinition(defendersTeam)?.Name ?? "Defenders";
				attackersScore = snd.AttackersScore;
				defendersScore = snd.DefendersScore;
				roundsToWin = snd.RoundsToWin;
				teamsSwapped = snd.TeamsSwapped;
				currentRound = snd.CurrentRound;
				objState = snd.GetObjectiveState();
			}
		}
		else if (matchStateClient != null)
		{
			// Client source
			attackersScore = matchStateClient.GetTeamScore(0);
			defendersScore = matchStateClient.GetTeamScore(1);
			roundsToWin = matchStateClient.RoundsToWin;
			currentRound = matchStateClient.RoundNumber;
			objState = matchStateClient.Objective;
		}

		string bombStatus = FormatBombStatus(objState);

		string info = $"S&D: {attackersName} {attackersScore} - {defendersName} {defendersScore} | First to {roundsToWin}\n";
		info += $"Round: {currentRound} | ATK={attackersName} DEF={defendersName}" + (teamsSwapped ? " (swapped)" : "") + "\n";
		info += $"Bomb: {bombStatus}";

		_sndInfoLabel.Text = info;
	}

	private string FormatBombStatus(ObjectiveState state)
	{
		if (state.Status == 0)
			return "Not planted";

		var siteName = GetSiteNameForIndex(state.SiteIndex);
		switch (state.Status)
		{
			case 1:
				return $"PLANTED at {siteName} ({state.TimeRemaining:F1}s)";
			case 2:
				return $"DEFUSED at {siteName}";
			case 3:
				return $"EXPLODED at {siteName}";
			default:
				return "Unknown";
		}
	}

	private string GetSiteNameForIndex(int siteIndex)
	{
		var site = BombSite.AllSites.FirstOrDefault(s => s.SiteIndex == siteIndex);
		if (site != null)
		{
			return site.SiteName;
		}

		if (siteIndex >= 0)
			return $"Site {siteIndex}";

		return "Unknown site";
	}

	private string GetCurrentPhaseName()
	{
		var matchStateClient = MatchStateClient.Instance;
		if (_network != null && _network.IsClient && matchStateClient != null)
		{
			var phase = matchStateClient.GameModePhase;
			var remaining = matchStateClient.PhaseTimeRemaining;

			if (remaining <= 0f || float.IsPositiveInfinity(remaining))
			{
				return phase.ToString();
			}
			else
			{
				var mins = (int)(remaining / 60f);
				var secs = (int)(remaining % 60f);
				return $"{phase} {mins}:{secs:D2}";
			}
		}

		if (_gameModeManager?.ActivePhase == null)
			return "No Phase";

		var serverPhase = _gameModeManager.ActivePhase;
		var phaseType = serverPhase.PhaseType;
		var serverRemaining = serverPhase.RemainingSeconds;

		if (float.IsPositiveInfinity(serverRemaining))
		{
			return phaseType.ToString();
		}
		else
		{
			var mins = (int)(serverRemaining / 60f);
			var secs = (int)(serverRemaining % 60f);
			return $"{phaseType} {mins}:{secs:D2}";
		}
	}
}
