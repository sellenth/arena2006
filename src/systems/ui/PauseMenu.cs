using System;
using Godot;

public partial class PauseMenu : CanvasLayer
{
	private Button[] _menuButtons = Array.Empty<Button>();
	private int _selectedIndex = 0;
	private OptionsMenu? _optionsMenu;
	private bool _optionsMenuConnected;
	private bool _holdsInputBlock;

	public override void _Ready()
	{
		ProcessMode = Node.ProcessModeEnum.Always;

		var optionsButton = GetNodeOrNull<Button>("Center/PanelContainer/Content/Menu/OptionsButton");
		var exitButton = GetNodeOrNull<Button>("Center/PanelContainer/Content/Menu/ExitButton");
		var resumeButton = GetNodeOrNull<Button>("Center/PanelContainer/Content/Menu/ResumeButton");

		_menuButtons = new[] { optionsButton, exitButton, resumeButton };
		for (int i = 0; i < _menuButtons.Length; i++)
		{
			if (_menuButtons[i] == null)
			{
				GD.PushError($"PauseMenu: Menu button at index {i} is missing.");
				continue;
			}

			_menuButtons[i].FocusMode = Control.FocusModeEnum.All;
		}

		if (optionsButton != null)
		{
			optionsButton.Pressed += OnOptionsPressed;
		}
		if (exitButton != null)
		{
			exitButton.Pressed += OnExitPressed;
		}
		if (resumeButton != null)
		{
			resumeButton.Pressed += OnResumePressed;
		}

		Visible = false;

		FindOptionsMenu();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("pause"))
		{
			if (IsOptionsMenuOpen())
			{
				return;
			}

			if (Visible)
			{
				ResumeGame();
			}
			else
			{
				OpenMenu();
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if (!Visible)
		{
			return;
		}

		if (@event.IsActionPressed("ui_down"))
		{
			MoveSelection(1);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event.IsActionPressed("ui_up"))
		{
			MoveSelection(-1);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event.IsActionPressed("ui_accept"))
		{
			ActivateSelection();
			GetViewport().SetInputAsHandled();
		}
	}

	private void OpenMenu()
	{
		if (!_holdsInputBlock)
		{
			GameplayInputGate.PushBlock();
			_holdsInputBlock = true;
		}

		_selectedIndex = 0;
		Visible = true;
		FocusCurrentButton();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void ResumeGame()
	{
		Visible = false;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		if (_holdsInputBlock)
		{
			GameplayInputGate.PopBlock();
			_holdsInputBlock = false;
		}
	}

	private void FindOptionsMenu()
	{
		if (_optionsMenu != null)
		{
			return;
		}

		var parent = GetParent();
		if (parent != null)
		{
			_optionsMenu = parent.GetNodeOrNull<OptionsMenu>("OptionsMenu");
		}

		if (_optionsMenu == null)
		{
			var scene = GetTree().CurrentScene;
			_optionsMenu = scene?.GetNodeOrNull<OptionsMenu>("OptionsMenu");
		}

		if (_optionsMenu != null && !_optionsMenuConnected)
		{
			_optionsMenu.Connect(OptionsMenu.SignalName.Closed, new Callable(this, nameof(OnOptionsMenuClosed)));
			_optionsMenuConnected = true;
		}
	}

	private bool IsOptionsMenuOpen()
	{
		return _optionsMenu != null && _optionsMenu.Visible;
	}

	private void MoveSelection(int direction)
	{
		if (_menuButtons.Length == 0)
		{
			return;
		}

		_selectedIndex = (_selectedIndex + direction) % _menuButtons.Length;
		if (_selectedIndex < 0)
		{
			_selectedIndex += _menuButtons.Length;
		}

		FocusCurrentButton();
	}

	private void FocusCurrentButton()
	{
		if (_menuButtons.Length == 0)
		{
			return;
		}

		var button = _menuButtons[_selectedIndex];
		button?.GrabFocus();
	}

	private void ActivateSelection()
	{
		if (_menuButtons.Length == 0)
		{
			return;
		}

		var button = _menuButtons[_selectedIndex];
		button?.EmitSignal(Button.SignalName.Pressed);
	}

	private void OnOptionsPressed()
	{
		FindOptionsMenu();
		if (_optionsMenu == null)
		{
			GD.PushError("PauseMenu: Options menu scene is missing.");
			return;
		}

		Visible = false;
		_optionsMenu.Open();
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}

	private void OnResumePressed()
	{
		ResumeGame();
	}

	private void OnOptionsMenuClosed()
	{
		OpenMenu();
		_selectedIndex = 0;
		FocusCurrentButton();
	}
}
