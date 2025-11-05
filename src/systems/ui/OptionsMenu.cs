using Godot;

public partial class OptionsMenu : CanvasLayer
{
	[Signal]
	public delegate void ClosedEventHandler();

	private bool _holdsInputBlock;
	private Label _descriptionLabel;
	private TabContainer _tabContainer;
	private HSlider _musicSlider;
	private HSlider _fxSlider;
	private HSlider _gameAudioSlider;
	private HSlider _weaponsSlider;
	private bool _syncingAudioSliders;

	// Crosshair tab controls
	private OptionButton _crosshairShapeOption;
	private HSlider _crosshairSizeSlider;
	private ColorPickerButton _crosshairColorPicker;
	private bool _syncingCrosshair;

	// Multiplayer tab controls
	private LineEdit _playerNameEdit;
	private ColorPickerButton _underglowPicker;
	private Label _hatValueLabel;
	private Button _hatPrevButton;
	private Button _hatNextButton;
	private bool _syncingMultiplayer;
	private bool _multiplayerDirty;
	private bool _customizationSignalConnected;

	private const int AudioTabIndex = 0;
	private const int CrosshairTabIndex = 1;
	private const int ControlsTabIndex = 2;
	private const int MultiplayerTabIndex = 3;
	private const string DefaultDescription = "Select a category";

	public override void _Ready()
	{
		ProcessMode = Node.ProcessModeEnum.Always;

		var closeButton = GetNodeOrNull<Button>("Center/PanelContainer/Content/CloseButton");
		_descriptionLabel = GetNode<Label>("Center/PanelContainer/Content/DescriptionLabel");
		_tabContainer = GetNode<TabContainer>("Center/PanelContainer/Content/Tabs");
		_musicSlider = GetNode<HSlider>("Center/PanelContainer/Content/Tabs/Audio/Music/MusicSlider");
		_fxSlider = GetNode<HSlider>("Center/PanelContainer/Content/Tabs/Audio/Fx/FxSlider");
		_gameAudioSlider = GetNode<HSlider>("Center/PanelContainer/Content/Tabs/Audio/GameAudio/GameAudioSlider");
		_weaponsSlider = GetNode<HSlider>("Center/PanelContainer/Content/Tabs/Audio/Weapons/WeaponsSlider");

		// Crosshair controls
		_crosshairShapeOption = GetNodeOrNull<OptionButton>("Center/PanelContainer/Content/Tabs/HUD/Shape/ShapeOption");
		_crosshairSizeSlider = GetNodeOrNull<HSlider>("Center/PanelContainer/Content/Tabs/HUD/Size/SizeSlider");
		_crosshairColorPicker = GetNodeOrNull<ColorPickerButton>("Center/PanelContainer/Content/Tabs/HUD/Color/ColorPicker");

		_musicSlider.ValueChanged += value => OnAudioSliderChanged(AudioChannel.Music, value);
		_fxSlider.ValueChanged += value => OnAudioSliderChanged(AudioChannel.Fx, value);
		_gameAudioSlider.ValueChanged += value => OnAudioSliderChanged(AudioChannel.GameAudio, value);
		_weaponsSlider.ValueChanged += value => OnAudioSliderChanged(AudioChannel.Weapons, value);

		// Initialize crosshair controls
		if (_crosshairShapeOption != null)
		{
			_crosshairShapeOption.Clear();
			_crosshairShapeOption.AddItem("Cross", (int)CrosshairShape.Cross);
			_crosshairShapeOption.AddItem("Dot", (int)CrosshairShape.Dot);
			_crosshairShapeOption.AddItem("Circle", (int)CrosshairShape.Circle);
			_crosshairShapeOption.ItemSelected += OnCrosshairShapeSelected;
		}
		if (_crosshairSizeSlider != null)
		{
			_crosshairSizeSlider.ValueChanged += OnCrosshairSizeChanged;
		}
		if (_crosshairColorPicker != null)
		{
			_crosshairColorPicker.ColorChanged += OnCrosshairColorChanged;
		}

		_playerNameEdit = GetNodeOrNull<LineEdit>("Center/PanelContainer/Content/Tabs/Multiplayer/NameRow/NameLineEdit");
		_underglowPicker = GetNodeOrNull<ColorPickerButton>("Center/PanelContainer/Content/Tabs/Multiplayer/UnderglowRow/UnderglowPicker");
		_hatValueLabel = GetNodeOrNull<Label>("Center/PanelContainer/Content/Tabs/Multiplayer/HatRow/HatSelector/HatValueLabel");
		_hatPrevButton = GetNodeOrNull<Button>("Center/PanelContainer/Content/Tabs/Multiplayer/HatRow/HatSelector/HatPrevButton");
		_hatNextButton = GetNodeOrNull<Button>("Center/PanelContainer/Content/Tabs/Multiplayer/HatRow/HatSelector/HatNextButton");

		if (_playerNameEdit != null)
		{
			_playerNameEdit.TextChanged += OnPlayerNameChanged;
		}
		if (_underglowPicker != null)
		{
			_underglowPicker.ColorChanged += OnUnderglowColorChanged;
		}

		var customization = PlayerCustomizationSettings.Instance;
		if (customization != null && !_customizationSignalConnected)
		{
			customization.Connect(PlayerCustomizationSettings.SignalName.Changed, new Callable(this, nameof(OnCustomizationSettingsChanged)));
			_customizationSignalConnected = true;
		}

		if (closeButton != null)
		{
			closeButton.Pressed += OnClosePressed;
		}

		_tabContainer.TabChanged += OnTabChanged;
		_tabContainer.CurrentTab = AudioTabIndex;
		SyncMultiplayerFromSettings();
		ApplyTabState(AudioTabIndex, focusAudio: false);
		Visible = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible)
		{
			return;
		}

		if (@event.IsActionPressed("pause") || @event.IsActionPressed("ui_cancel"))
		{
			Close();
			GetViewport().SetInputAsHandled();
			return;
		}

	}

	public void Open()
	{
		if (!_holdsInputBlock)
		{
			GameplayInputGate.PushBlock();
			_holdsInputBlock = true;
		}

		Visible = true;
		_tabContainer.CurrentTab = AudioTabIndex;
		ApplyTabState(AudioTabIndex, focusAudio: true);
		_tabContainer.GrabFocus();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public void Close()
	{
		if (!Visible)
		{
			return;
		}

		Visible = false;
		if (_holdsInputBlock)
		{
			GameplayInputGate.PopBlock();
			_holdsInputBlock = false;
		}
		_tabContainer.CurrentTab = AudioTabIndex;
		ApplyTabState(AudioTabIndex, focusAudio: false);
		PersistCustomizationChanges();
		EmitSignal(SignalName.Closed);
	}

	private void OnClosePressed()
	{
		Close();
	}

	private void SetDescription(string text)
	{
		_descriptionLabel.Text = text;
	}

	private void OnTabChanged(long tabIndex)
	{
		ApplyTabState((int)tabIndex, focusAudio: true);
	}

	private void GrabAudioFocus()
	{
		if (!Visible)
		{
			return;
		}
		_musicSlider.GrabFocus();
	}

	private void ApplyTabState(int tabIndex, bool focusAudio)
	{
		UpdateDescriptionForTab(tabIndex);
		if (tabIndex == AudioTabIndex)
		{
			SyncAudioSlidersFromSettings();
			if (focusAudio)
			{
				CallDeferred(nameof(GrabAudioFocus));
			}
		}
		else if (tabIndex == CrosshairTabIndex)
		{
			SyncCrosshairFromSettings();
		}
		else if (tabIndex == MultiplayerTabIndex)
		{
			SyncMultiplayerFromSettings();
		}
	}

	private void UpdateDescriptionForTab(int tabIndex)
	{
		switch (tabIndex)
		{
			case AudioTabIndex:
				SetDescription("Adjust audio levels");
				break;
			case CrosshairTabIndex:
				SetDescription("Customize the crosshair (shape, size, color)");
				break;
			case ControlsTabIndex:
				SetDescription("Controls menu not yet available.");
				break;
			case MultiplayerTabIndex:
				SetDescription("Customize your multiplayer identity and appearance.");
				break;
			default:
				SetDescription(DefaultDescription);
				break;
		}
	}

	private void SyncAudioSlidersFromSettings()
	{
		var manager = AudioSettingsManager.Instance;
		if (manager == null)
		{
			GD.PushWarning("OptionsMenu: AudioSettingsManager not found when syncing sliders.");
			return;
		}

		_syncingAudioSliders = true;
		_musicSlider.Value = manager.GetChannelVolume(AudioChannel.Music) * 100.0f;
		_fxSlider.Value = manager.GetChannelVolume(AudioChannel.Fx) * 100.0f;
		_gameAudioSlider.Value = manager.GetChannelVolume(AudioChannel.GameAudio) * 100.0f;
		_weaponsSlider.Value = manager.GetChannelVolume(AudioChannel.Weapons) * 100.0f;
		_syncingAudioSliders = false;
	}

	private void OnAudioSliderChanged(AudioChannel channel, double sliderValue)
	{
		if (_syncingAudioSliders)
		{
			return;
		}

		var manager = AudioSettingsManager.Instance;
		if (manager == null)
		{
			GD.PushWarning("OptionsMenu: AudioSettingsManager not available when applying slider change.");
			return;
		}

		float normalized = Mathf.Clamp((float)(sliderValue / 100.0), 0.0f, 1.0f);
		manager.SetChannelVolume(channel, normalized);
	}

	private void SyncCrosshairFromSettings()
	{
		var ch = CrosshairSettingsManager.Instance;
		if (ch == null)
			return;

		_syncingCrosshair = true;
		if (_crosshairShapeOption != null)
		{
			_crosshairShapeOption.Selected = (int)ch.Shape;
		}
		if (_crosshairSizeSlider != null)
		{
			_crosshairSizeSlider.Value = ch.Size;
		}
		if (_crosshairColorPicker != null)
		{
			_crosshairColorPicker.Color = ch.Color;
		}
		_syncingCrosshair = false;
	}

	private void OnCrosshairShapeSelected(long idx)
	{
		if (_syncingCrosshair) return;
		var ch = CrosshairSettingsManager.Instance;
		if (ch == null) return;
		ch.SetShape((CrosshairShape)(int)idx);
	}

	private void OnCrosshairSizeChanged(double value)
	{
		if (_syncingCrosshair) return;
		var ch = CrosshairSettingsManager.Instance;
		if (ch == null) return;
		ch.SetSize((float)value);
	}

	private void OnCrosshairColorChanged(Color color)
	{
		if (_syncingCrosshair) return;
		var ch = CrosshairSettingsManager.Instance;
		if (ch == null) return;
		ch.SetColor(color);
	}

	private void SyncMultiplayerFromSettings()
	{
		var customization = PlayerCustomizationSettings.Instance;
		if (customization == null)
		{
			return;
		}

		_syncingMultiplayer = true;

		if (_playerNameEdit != null && _playerNameEdit.Text != customization.PlayerName)
		{
			_playerNameEdit.Text = customization.PlayerName;
		}
		if (_underglowPicker != null && !_underglowPicker.Color.IsEqualApprox(customization.UnderglowColor))
		{
			_underglowPicker.Color = customization.UnderglowColor;
		}
		if (_hatValueLabel != null)
		{
			_hatValueLabel.Text = GetHatDisplayName(customization.HatId);
		}
		if (_hatPrevButton != null)
		{
			_hatPrevButton.Disabled = true;
		}
		if (_hatNextButton != null)
		{
			_hatNextButton.Disabled = true;
		}

		_syncingMultiplayer = false;
		_multiplayerDirty = false;
	}

	private void OnPlayerNameChanged(string newName)
	{
		if (_syncingMultiplayer)
		{
			return;
		}

		var customization = PlayerCustomizationSettings.Instance;
		if (customization == null)
		{
			return;
		}

		customization.SetPlayerName(newName, save: false);
		_multiplayerDirty = true;
	}

	private void OnUnderglowColorChanged(Color color)
	{
		if (_syncingMultiplayer)
		{
			return;
		}

		var customization = PlayerCustomizationSettings.Instance;
		if (customization == null)
		{
			return;
		}

		customization.SetUnderglowColor(color, save: false);
		_multiplayerDirty = true;
	}

	private void OnCustomizationSettingsChanged(string name, string hatId, Color underglowColor)
	{
		if (_syncingMultiplayer)
		{
			return;
		}
		SyncMultiplayerFromSettings();
	}

	private void PersistCustomizationChanges()
	{
		var customization = PlayerCustomizationSettings.Instance;
		if (customization == null)
		{
			return;
		}

		bool needsUpload = _multiplayerDirty || customization.IsDirty;
		if (!needsUpload)
		{
			return;
		}

		customization.Save();
		_multiplayerDirty = false;

		//TODO: UploadLocalCustomization();
	}

	private static string GetHatDisplayName(string hatId)
	{
		if (string.IsNullOrEmpty(hatId) || hatId == PlayerCustomizationSettings.DefaultHatId)
		{
			return "(None)";
		}
		return hatId;
	}
}
