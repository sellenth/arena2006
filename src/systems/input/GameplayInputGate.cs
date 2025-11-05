using Godot;

public static class GameplayInputGate
{
	private static int _blockCount = 0;
	private static readonly string[] _gameplayActions =
	{
		"fire",
		"reload",
		"mode_switch",
		"handbrake",
		"ui_up",
		"ui_down",
		"ui_left",
		"ui_right",
		"startrace",
		"reset",
		"sprint",
		"crouch"
	};

	public static bool IsBlocked => _blockCount > 0;

	public static void PushBlock()
	{
		_blockCount++;
		ReleaseBufferedActions();
	}

	public static void PopBlock()
	{
		if (_blockCount > 0)
		{
			_blockCount--;
		}
	}

	public static void Clear()
	{
		_blockCount = 0;
	}

	public static void ReleaseBufferedActions()
	{
		foreach (var action in _gameplayActions)
		{
			if (InputMap.HasAction(action))
			{
				Input.ActionRelease(action);
			}
		}
	}
}
