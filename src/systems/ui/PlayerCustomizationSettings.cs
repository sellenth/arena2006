using System.Text;
using Godot;

public readonly struct PlayerCustomizationState
{
	public readonly string Name;
	public readonly string HatId;
	public readonly Color UnderglowColor;

	public PlayerCustomizationState(string name, string hatId, Color underglowColor)
	{
		Name = name;
		HatId = hatId;
		UnderglowColor = underglowColor;
	}
}

[GlobalClass]
public partial class PlayerCustomizationSettings : Node
{
	public static PlayerCustomizationSettings? Instance { get; private set; }

	[Signal]
	public delegate void ChangedEventHandler(string name, string hatId, Color underglowColor);

	public const string DefaultName = "net_player";
	public const string DefaultHatId = "none";
	public static readonly Color DefaultUnderglowColor = Colors.White;

	private const string ConfigPath = "user://player_customization.cfg";
	private const string Section = "player";
	private const int MaxNameLength = 20;

	private bool _isLoaded;
	private bool _dirty;

	public string PlayerName { get; private set; } = DefaultName;
	public string HatId { get; private set; } = DefaultHatId;
	public Color UnderglowColor { get; private set; } = DefaultUnderglowColor;

	public bool IsDirty => _dirty;

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this)
		{
			GD.PushWarning("PlayerCustomizationSettings: Duplicate instance detected; freeing the new one.");
			QueueFree();
			return;
		}
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public override void _Ready()
	{
		EnsureLoaded();
	}

	public PlayerCustomizationState GetState()
	{
		EnsureLoaded();
		return new PlayerCustomizationState(PlayerName, HatId, UnderglowColor);
	}

	public void SetPlayerName(string name, bool save = true)
	{
		EnsureLoaded();
		var sanitized = SanitizePlayerName(name);
		if (PlayerName == sanitized)
		{
			return;
		}
		PlayerName = sanitized;
		_dirty = true;
		EmitChange();
		if (save)
		{
			Save();
		}
	}

	public void SetHat(string hatId, bool save = true)
	{
		EnsureLoaded();
		var normalized = NormalizeHatId(hatId);
		if (HatId == normalized)
		{
			return;
		}
		HatId = normalized;
		_dirty = true;
		EmitChange();
		if (save)
		{
			Save();
		}
	}

	public void SetUnderglowColor(Color color, bool save = true)
	{
		EnsureLoaded();
		var sanitized = ClampUnderglowColor(color);
		if (UnderglowColor.IsEqualApprox(sanitized))
		{
			return;
		}
		UnderglowColor = sanitized;
		_dirty = true;
		EmitChange();
		if (save)
		{
			Save();
		}
	}

	public void Load()
	{
		var cfg = new ConfigFile();
		var err = cfg.Load(ConfigPath);
		if (err != Error.Ok)
		{
			PlayerName = DefaultName;
			HatId = DefaultHatId;
			UnderglowColor = DefaultUnderglowColor;
			_isLoaded = true;
			_dirty = true;
			EmitChange();
			if (err == Error.FileCantOpen || err == Error.FileNotFound)
			{
				Save();
			}
			else
			{
				GD.PushWarning($"PlayerCustomizationSettings: Failed to load settings ({err}).");
			}
			return;
		}

		string name = DefaultName;
		if (cfg.HasSectionKey(Section, "name"))
		{
			name = cfg.GetValue(Section, "name").ToString();
		}

		string hat = DefaultHatId;
		if (cfg.HasSectionKey(Section, "hat"))
		{
			hat = cfg.GetValue(Section, "hat").ToString();
		}

		Color underglow = DefaultUnderglowColor;
		if (cfg.HasSectionKey(Section, "underglow"))
		{
			var value = cfg.GetValue(Section, "underglow");
			switch (value.VariantType)
			{
				case Variant.Type.Color:
					underglow = (Color)value;
					break;
				case Variant.Type.String:
					try
					{
						underglow = new Color(value.ToString());
					}
					catch
					{
						underglow = DefaultUnderglowColor;
					}
					break;
			}
		}

		PlayerName = SanitizePlayerName(name);
		HatId = NormalizeHatId(hat);
		UnderglowColor = ClampUnderglowColor(underglow);

		_isLoaded = true;
		_dirty = false;
		EmitChange();
	}

	public void Save()
	{
		EnsureLoaded();
		var cfg = new ConfigFile();
		cfg.SetValue(Section, "name", PlayerName);
		cfg.SetValue(Section, "hat", HatId);
		cfg.SetValue(Section, "underglow", UnderglowColor.ToHtml(true));
		var err = cfg.Save(ConfigPath);
		if (err != Error.Ok)
		{
			GD.PushWarning($"PlayerCustomizationSettings: Failed to save settings ({err}).");
			return;
		}
		_dirty = false;
	}

	private void EnsureLoaded()
	{
		if (_isLoaded)
		{
			return;
		}
		Load();
	}

	private void EmitChange()
	{
		if (!IsInsideTree())
		{
			return;
		}
		EmitSignal(SignalName.Changed, PlayerName, HatId, UnderglowColor);
	}

	public static string SanitizePlayerName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return DefaultName;
		}

		var trimmed = name.Trim();
		var builder = new StringBuilder(MaxNameLength);
		foreach (var ch in trimmed)
		{
			if (char.IsControl(ch))
			{
				continue;
			}
			builder.Append(ch);
			if (builder.Length >= MaxNameLength)
			{
				break;
			}
		}

		return builder.Length > 0 ? builder.ToString() : DefaultName;
	}

	public static string NormalizeHatId(string hatId)
	{
		if (string.IsNullOrWhiteSpace(hatId))
		{
			return DefaultHatId;
		}
		var normalized = hatId.Trim().ToLowerInvariant();
		return normalized.Length == 0 ? DefaultHatId : normalized;
	}

	public static Color ClampUnderglowColor(Color color)
	{
		var clamped = new Color(
			Mathf.Clamp(color.R, 0f, 1f),
			Mathf.Clamp(color.G, 0f, 1f),
			Mathf.Clamp(color.B, 0f, 1f),
			1f
		);
		return clamped;
	}
}
