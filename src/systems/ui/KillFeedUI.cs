using Godot;

public partial class KillFeedUI : CanvasLayer
{
	private const float EntryLifetime = 5.0f;
	private const float FadeDuration = 0.35f;
	private const int MaxEntries = 6;

	private VBoxContainer _feed;
	private NetworkController _network;

	public override void _Ready()
	{
		_feed = GetNodeOrNull<VBoxContainer>("Root/Feed");
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");

		if (_network != null)
		{
			_network.KillFeedReceived += OnKillFeedReceived;
		}
	}

	public override void _ExitTree()
	{
		if (_network != null)
		{
			_network.KillFeedReceived -= OnKillFeedReceived;
		}
	}

	public void AddEntry(string attacker, string weapon, string victim)
	{
		if (_feed == null)
			return;

		var label = new Label
		{
			Text = $"{attacker} [{weapon}] - {victim}",
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color(1f, 1f, 1f, 0f)
		};
		label.AddThemeFontSizeOverride("font_size", 16);

		_feed.AddChild(label);
		TrimEntries();

		var tween = CreateTween();
		tween.TweenProperty(label, "modulate:a", 1f, FadeDuration)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
		tween.TweenInterval(Mathf.Max(EntryLifetime - FadeDuration * 2f, 0f));
		tween.TweenProperty(label, "modulate:a", 0f, FadeDuration)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(() =>
		{
			if (IsInstanceValid(label))
			{
				label.QueueFree();
			}
		}));
	}

	private void OnKillFeedReceived(int killerId, int victimId, WeaponType weaponType)
	{
		if (_feed == null)
			return;

		var attackerName = FormatAttacker(killerId, victimId);
		var victimName = FormatVictim(victimId);
		var weaponName = weaponType != WeaponType.None ? weaponType.ToString() : "😎";

		AddEntry(attackerName, weaponName, victimName);
	}

	private static string FormatAttacker(int killerId, int victimId)
	{
		if (killerId <= 0)
			return "Guardians";

		if (killerId == victimId)
			return $"Player {killerId} (Self)";

		return $"Player {killerId}";
	}

	private static string FormatVictim(int victimId)
	{
		if (victimId <= 0)
			return "this is probably a bug";

		return $"Player {victimId}";
	}

	private void TrimEntries()
	{
		if (_feed == null)
			return;

		while (_feed.GetChildCount() > MaxEntries)
		{
			var first = _feed.GetChild(0);
			first.QueueFree();
		}
	}
}
