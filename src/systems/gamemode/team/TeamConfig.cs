using Godot;

public enum TeamStructure
{
	FreeForAll = 0,
	TwoTeams = 1,
	MultiTeam = 2
}

public readonly struct TeamConfig
{
	public static readonly TeamConfig FreeForAll = new(TeamStructure.FreeForAll, 0, false, false);
	public static readonly TeamConfig TwoTeam = new(TeamStructure.TwoTeams, 2, true, true);

	public TeamStructure Structure { get; }
	public int TeamCount { get; }
	public bool AllowTeamSwitching { get; }
	public bool AutoBalance { get; }

	public TeamConfig(TeamStructure structure, int teamCount, bool allowTeamSwitching, bool autoBalance)
	{
		Structure = structure;
		TeamCount = structure == TeamStructure.FreeForAll ? 0 : System.Math.Max(teamCount, 2);
		AllowTeamSwitching = allowTeamSwitching;
		AutoBalance = autoBalance;
	}

	public static TeamConfig CreateMultiTeam(int teamCount, bool allowSwitching = true, bool autoBalance = true)
	{
		return new TeamConfig(TeamStructure.MultiTeam, teamCount, allowSwitching, autoBalance);
	}

	public bool IsFreeForAll => Structure == TeamStructure.FreeForAll;
	public bool IsTeamBased => Structure != TeamStructure.FreeForAll;
}

