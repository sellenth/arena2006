using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;

public enum AudioChannel
{
	Music,
	Fx,
	GameAudio,
	Weapons
}

[GlobalClass]
public partial class AudioSettingsManager : Node
{
	public const string MasterBusName = "Master";
	public const string MusicBusName = "Music";
	public const string FxBusName = "FX";
	public const string GameAudioBusName = "GameAudio";
	public const string WeaponsBusName = "Weapons";

	public static AudioSettingsManager? Instance { get; private set; }

	private const string ConfigPath = "user://audio_settings.cfg";
	private const string ConfigSection = "audio";

	private readonly Dictionary<AudioChannel, string> _busNames = new()
	{
		{ AudioChannel.Music, MusicBusName },
		{ AudioChannel.Fx, FxBusName },
		{ AudioChannel.GameAudio, GameAudioBusName },
		{ AudioChannel.Weapons, WeaponsBusName },
	};

	private readonly Dictionary<AudioChannel, float> _volumes = new()
	{
		{ AudioChannel.Music, 1.0f },
		{ AudioChannel.Fx, 1.0f },
		{ AudioChannel.GameAudio, 1.0f },
		{ AudioChannel.Weapons, 1.0f },
	};

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this)
		{
			GD.PushWarning("AudioSettingsManager: Duplicate instance detected; freeing the new one.");
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
		InitializeBuses();
		Load();
	}

	public float GetChannelVolume(AudioChannel channel)
	{
		return _volumes.TryGetValue(channel, out var volume) ? volume : 1.0f;
	}

	public string GetBusName(AudioChannel channel)
	{
		return _busNames.TryGetValue(channel, out var name) ? name : MasterBusName;
	}

	public void SetChannelVolume(AudioChannel channel, float volume, bool save = true)
	{
		volume = Mathf.Clamp(volume, 0.0f, 1.0f);
		_volumes[channel] = volume;
		ApplyVolume(channel, volume);
		if (save)
		{
			Save();
		}
	}

	private void InitializeBuses()
	{
		EnsureBusExists(MusicBusName);
		EnsureBusExists(FxBusName);
		EnsureBusExists(GameAudioBusName);
		EnsureBusExists(WeaponsBusName);
	}

	private void EnsureBusExists(string busName)
	{
		var index = AudioServer.GetBusIndex(busName);
		if (index != -1)
		{
			return;
		}

		AudioServer.AddBus();
		index = AudioServer.BusCount - 1;
		AudioServer.SetBusName(index, busName);
		AudioServer.SetBusSend(index, MasterBusName);
	}

	private void ApplyVolume(AudioChannel channel, float volume)
	{
		if (!_busNames.TryGetValue(channel, out var busName))
		{
			return;
		}

		var busIndex = AudioServer.GetBusIndex(busName);
		if (busIndex == -1)
		{
			GD.PushWarning($"AudioSettingsManager: Bus '{busName}' not found when applying volume.");
			return;
		}

		var db = volume <= 0.0001f ? -80.0f : Mathf.LinearToDb(volume);
		AudioServer.SetBusVolumeDb(busIndex, db);
	}

	private void Load()
	{
		var config = new ConfigFile();
		var error = config.Load(ConfigPath);
		if (error != Error.Ok)
		{
			foreach (var kvp in _busNames)
			{
				ApplyVolume(kvp.Key, _volumes[kvp.Key]);
			}
			return;
		}

		foreach (var kvp in _busNames)
		{
			float volume = _volumes[kvp.Key];
			Variant stored = config.GetValue(ConfigSection, kvp.Value, volume);

			// Try to get as float first
			if (stored.VariantType == Variant.Type.Float)
			{
				volume = Mathf.Clamp(stored.AsSingle(), 0.0f, 1.0f);
			}
			// Try to get as bool (for on/off settings)
			else if (stored.VariantType == Variant.Type.Bool)
			{
				volume = stored.AsBool() ? 1.0f : 0.0f;
			}
			// Fallback to string parsing for compatibility
			else
			{
				string storedText = stored.ToString();
				if (float.TryParse(storedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFloat))
				{
					volume = Mathf.Clamp(parsedFloat, 0.0f, 1.0f);
				}
				else if (bool.TryParse(storedText, out var parsedBool))
				{
					volume = parsedBool ? 1.0f : 0.0f;
				}
			}

			_volumes[kvp.Key] = volume;
			ApplyVolume(kvp.Key, volume);
		}
	}

	public void Save()
	{
		var config = new ConfigFile();
		foreach (var kvp in _busNames)
		{
			config.SetValue(ConfigSection, kvp.Value, _volumes[kvp.Key]);
		}

		var error = config.Save(ConfigPath);
		if (error != Error.Ok)
		{
			GD.PushWarning($"AudioSettingsManager: Failed to save audio settings ({error}).");
		}
	}
}
