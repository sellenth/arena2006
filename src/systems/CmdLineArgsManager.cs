using Godot;
using System.Collections.Generic;

public enum NetworkRole
{
	None,
	Server,
	Client
}

public static class CmdLineArgsManager
{
	private static NetworkRole? _cachedRole = null;
	private static string _cachedClientIp = null;
	private static List<string> _allArgs = null;

	private static void EnsureArgsLoaded()
	{
		if (_allArgs != null)
			return;

		_allArgs = new List<string>();
		var systemArgs = OS.GetCmdlineArgs();
		var userArgs = OS.GetCmdlineUserArgs();
		
		_allArgs.AddRange(systemArgs);
		_allArgs.AddRange(userArgs);
	}

	public static NetworkRole GetNetworkRole()
	{
		if (_cachedRole.HasValue)
			return _cachedRole.Value;

		EnsureArgsLoaded();
		var parsedRole = NetworkRole.Client;

		foreach (var arg in _allArgs)
		{
			var splitArgs = arg.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
			foreach (var splitArg in splitArgs)
			{
				if (splitArg == "--server")
				{
					_cachedRole = NetworkRole.Server;
					return NetworkRole.Server;
				}
				else if (splitArg == "--client")
				{
					parsedRole = NetworkRole.Client;
				}
			}
		}

		_cachedRole = parsedRole;
		return parsedRole;
	}

	public static string GetClientIp(string defaultValue = "127.0.0.1")
	{
		if (_cachedClientIp != null)
			return _cachedClientIp;

		EnsureArgsLoaded();

		for (int i = 0; i < _allArgs.Count; i++)
		{
			var arg = _allArgs[i];
			
			if (arg.StartsWith("--ip="))
			{
				_cachedClientIp = arg.Substring(5);
				return _cachedClientIp;
			}
			else if (arg == "--ip" && i + 1 < _allArgs.Count)
			{
				_cachedClientIp = _allArgs[i + 1];
				return _cachedClientIp;
			}
			
			var splitArgs = arg.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
			for (int j = 0; j < splitArgs.Length; j++)
			{
				if (splitArgs[j].StartsWith("--ip="))
				{
					_cachedClientIp = splitArgs[j].Substring(5);
					return _cachedClientIp;
				}
				else if (splitArgs[j] == "--ip" && j + 1 < splitArgs.Length)
				{
					_cachedClientIp = splitArgs[j + 1];
					return _cachedClientIp;
				}
			}
		}

		_cachedClientIp = defaultValue;
		return defaultValue;
	}

	public static bool HasFlag(string flagName)
	{
		EnsureArgsLoaded();
		
		foreach (var arg in _allArgs)
		{
			if (arg == flagName)
				return true;
			
			var splitArgs = arg.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
			foreach (var splitArg in splitArgs)
			{
				if (splitArg == flagName)
					return true;
			}
		}
		
		return false;
	}

	public static string GetValue(string flagName, string defaultValue = null)
	{
		EnsureArgsLoaded();
		
		for (int i = 0; i < _allArgs.Count; i++)
		{
			var arg = _allArgs[i];
			
			if (arg.StartsWith($"{flagName}="))
			{
				return arg.Substring(flagName.Length + 1);
			}
			else if (arg == flagName && i + 1 < _allArgs.Count)
			{
				return _allArgs[i + 1];
			}
			
			var splitArgs = arg.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
			for (int j = 0; j < splitArgs.Length; j++)
			{
				if (splitArgs[j].StartsWith($"{flagName}="))
				{
					return splitArgs[j].Substring(flagName.Length + 1);
				}
				else if (splitArgs[j] == flagName && j + 1 < splitArgs.Length)
				{
					return splitArgs[j + 1];
				}
			}
		}
		
		return defaultValue;
	}
}

