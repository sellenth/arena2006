using Godot;

public sealed class TeamDefinition
{
	public static readonly TeamDefinition None = new(-1, "None", Colors.Gray);
	public static readonly TeamDefinition Team1 = new(0, "Red Team", new Color(1f, 0f, 0f));
	public static readonly TeamDefinition Team2 = new(1, "Green Team", new Color(0f, 1f, 0f));
	public static readonly TeamDefinition Team3 = new(2, "Blue Team", new Color(0.2f, 0.4f, 0.9f));
	public static readonly TeamDefinition Team4 = new(3, "Yellow Team", new Color(0.9f, 0.8f, 0.2f));

	private static readonly TeamDefinition[] DefaultTeams = { Team1, Team2, Team3, Team4 };

	public int TeamId { get; }
	public string Name { get; }
	public Color Color { get; }

	public TeamDefinition(int teamId, string name, Color color)
	{
		TeamId = teamId;
		Name = name ?? string.Empty;
		Color = color;
	}

	public static TeamDefinition GetDefault(int teamId)
	{
		if (teamId < 0)
			return None;
		if (teamId < DefaultTeams.Length)
			return DefaultTeams[teamId];
		return new TeamDefinition(teamId, $"Team {teamId + 1}", GetDefaultColor(teamId));
	}

	private static Color GetDefaultColor(int index)
	{
		float hue = (index * 0.618033988749895f) % 1.0f;
		return Color.FromHsv(hue, 0.7f, 0.9f);
	}

	public bool IsValid => TeamId >= 0;

	public override bool Equals(object obj)
	{
		return obj is TeamDefinition other && TeamId == other.TeamId;
	}

	public override int GetHashCode()
	{
		return TeamId.GetHashCode();
	}

	public static bool operator ==(TeamDefinition left, TeamDefinition right)
	{
		if (left is null) return right is null;
		return left.Equals(right);
	}

	public static bool operator !=(TeamDefinition left, TeamDefinition right)
	{
		return !(left == right);
	}
}

