using Godot;

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
		var snd = _gameModeManager?.ActiveMode as SearchAndDestroyMode;

		var isServer = _network != null && _network.IsServer;
		var isSndMode = (isServer && snd != null) || matchStateClient?.CurrentModeId == SearchAndDestroyMode.ModeId;
		if (!isSndMode)
		{
			_sndInfoLabel.Visible = false;
			return;
		}
		_sndInfoLabel.Visible = true;

		int attackersScore, defendersScore;
		string attackersName, defendersName;
		int roundsToWin;
		bool teamsSwapped;
		string bombStatus;

		if (isServer && snd != null)
		{
			var attackersTeam = snd.GetAttackersTeamId();
			var defendersTeam = snd.GetDefendersTeamId();
			attackersName = _gameModeManager.TeamManager.GetTeamDefinition(attackersTeam)?.Name ?? "Attackers";
			defendersName = _gameModeManager.TeamManager.GetTeamDefinition(defendersTeam)?.Name ?? "Defenders";
			attackersScore = snd.AttackersScore;
			defendersScore = snd.DefendersScore;
			roundsToWin = snd.RoundsToWin;
			teamsSwapped = snd.TeamsSwapped;
			bombStatus = GetBombSiteStatusServer(snd);
		}
		else if (matchStateClient != null)
		{
			attackersName = "Reds";
			defendersName = "Greens";
			attackersScore = matchStateClient.GetTeamScore(0);
			defendersScore = matchStateClient.GetTeamScore(1);
			roundsToWin = 7;
			teamsSwapped = false;
			bombStatus = GetBombSiteStatusClient();
		}
		else
		{
			_sndInfoLabel.Visible = false;
			return;
		}

		string info;
		if (isServer && snd != null)
		{
			info = $"S&D: {attackersName} {attackersScore} - {defendersName} {defendersScore} | First to {roundsToWin}\n";
			info += $"Round: {snd.CurrentRound} | ATK={attackersName} DEF={defendersName}" + (teamsSwapped ? " (swapped)" : "") + "\n";
			info += $"Bomb: {bombStatus}";
		}
		else
		{
			info = $"S&D: {attackersName} {attackersScore} - {defendersName} {defendersScore} | First to {roundsToWin}\n";
			info += $"Round: {matchStateClient?.RoundNumber ?? 0} | (client view)\n";
			info += $"Bomb: {bombStatus}";
		}

		_sndInfoLabel.Text = info;
	}

	private string GetBombSiteStatusServer(SearchAndDestroyMode snd)
	{
		if (snd.IsBombPlanted)
		{
			var activeBomb = PlantedBomb.ActiveBomb;
			var siteName = activeBomb?.SiteName ?? "?";
			var timeLeft = snd.BombTimeRemaining;
			return $"PLANTED at {siteName} ({timeLeft:F1}s)";
		}

		foreach (var site in BombSite.AllSites)
		{
			if (site.IsPlanting)
			{
				var progress = site.CurrentPlantProgress / site.PlantTime * 100f;
				return $"PLANTING at {site.SiteName} ({progress:F0}%)";
			}
		}

		return "Not planted";
	}

	private string GetBombSiteStatusClient()
	{
		var activeBomb = PlantedBomb.ActiveBomb;
		if (activeBomb != null)
		{
			return $"PLANTED at {activeBomb.SiteName} ({activeBomb.FuseRemaining:F1}s)";
		}

		foreach (var site in BombSite.AllSites)
		{
			if (site.IsPlanting)
			{
				var progress = site.CurrentPlantProgress / site.PlantTime * 100f;
				return $"PLANTING at {site.SiteName} ({progress:F0}%)";
			}
			if (site.HasBomb)
			{
				return $"PLANTED at {site.SiteName}";
			}
		}

		return "Not planted";
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
