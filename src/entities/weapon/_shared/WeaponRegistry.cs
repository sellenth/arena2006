using Godot;
using System.Collections.Generic;

public static class WeaponRegistry
{
	private static readonly Dictionary<WeaponType, WeaponDefinition> _definitions = new();
	private static bool _initialized;

	private static readonly string[] WeaponPaths =
	{
		"res://src/entities/weapon/ak/weapon_definition.tres",
		"res://src/entities/weapon/uzi/weapon_definition.tres",
		"res://src/entities/weapon/sniper/weapon_definition.tres",
		"res://src/entities/weapon/rocket_launcher/weapon_definition.tres",
	};

	public static void EnsureInitialized()
	{
		if (_initialized)
			return;

		_initialized = true;
		foreach (var path in WeaponPaths)
		{
			var def = GD.Load<WeaponDefinition>(path);
			if (def != null)
			{
				_definitions[def.Id] = def;
				GD.Print($"[WeaponRegistry] Loaded {def.Id} from {path}");
			}
			else
			{
				GD.PushWarning($"[WeaponRegistry] Failed to load weapon from {path}");
			}
		}
	}

	public static WeaponDefinition Get(WeaponType type)
	{
		EnsureInitialized();
		_definitions.TryGetValue(type, out var def);
		return def;
	}

	public static bool TryGet(WeaponType type, out WeaponDefinition def)
	{
		EnsureInitialized();
		return _definitions.TryGetValue(type, out def);
	}

	public static IEnumerable<WeaponDefinition> GetAll()
	{
		EnsureInitialized();
		return _definitions.Values;
	}
}

