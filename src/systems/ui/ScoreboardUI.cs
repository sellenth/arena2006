using Godot;
using System;
using System.Collections.Generic;

public partial class ScoreboardUI : CanvasLayer
{
	private const float ColumnFontSize = 18f;
	private const float RowFontSize = 16f;

	private Control _panel;
	private VBoxContainer _rows;
	private Label _emptyLabel;
	private NetworkController _network;

	public override void _Ready()
	{
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");
		if (_network != null)
		{
			_network.ScoreboardUpdated += OnScoreboardUpdated;
			ApplyScoreboard(_network.GetScoreboardSnapshot());
		}
		else
		{
			ApplyScoreboard(new Godot.Collections.Array());
		}

		RefreshHeaders();
	}

	public override void _ExitTree()
	{
		if (_network != null)
		{
			_network.ScoreboardUpdated -= OnScoreboardUpdated;
		}
	}

	public override void _Process(double delta)
	{
		Visible = Input.IsActionPressed("scoreboard");
	}

	private void OnScoreboardUpdated(Godot.Collections.Array scoreboard)
	{
		ApplyScoreboard(scoreboard);
	}

	private void BindNodes()
	{
		if (_panel != null && _rows != null)
			return;

		_panel = GetNodeOrNull<Control>("Root/Panel");
		_rows = _panel?.GetNodeOrNull<VBoxContainer>("VBox/Rows");
		_emptyLabel = _panel?.GetNodeOrNull<Label>("VBox/Empty");
	}

	public void ApplyScoreboard(Godot.Collections.Array scoreboard)
	{
		BindNodes();
		if (_rows == null || _emptyLabel == null)
			return;

		foreach (Node child in _rows.GetChildren())
			child.QueueFree();

		if (scoreboard == null || scoreboard.Count == 0)
		{
			_emptyLabel.Visible = true;
			return;
		}

		var rows = ParseRows(scoreboard);
		_emptyLabel.Visible = rows.Count == 0;
		if (rows.Count == 0)
			return;

		var localId = _network?.ClientPeerId ?? 0;

		foreach (var row in rows)
		{
			var isLocal = localId != 0 && row.Id == localId;
			_rows.AddChild(BuildRow(row, isLocal));
		}
	}

	private List<ScoreRow> ParseRows(Godot.Collections.Array scoreboard)
	{
		var rows = new List<ScoreRow>();
		foreach (var entry in scoreboard)
		{
			if (entry.VariantType != Variant.Type.Dictionary)
				continue;

			var dict = entry.AsGodotDictionary();
			var id = GetInt(dict, "id");
			var kills = GetInt(dict, "kills");
			var deaths = GetInt(dict, "deaths");
			var name = $"Player {id}";
			rows.Add(new ScoreRow(id, name, kills, deaths));
		}

		rows.Sort((a, b) =>
		{
			var killCompare = b.Kills.CompareTo(a.Kills);
			if (killCompare != 0) return killCompare;
			var deathCompare = a.Deaths.CompareTo(b.Deaths);
			if (deathCompare != 0) return deathCompare;
			return a.Id.CompareTo(b.Id);
		});

		return rows;
	}

	private Control BuildRow(ScoreRow row, bool highlight)
	{
		var container = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		container.AddChild(CreateCell(row.Name, 120f, highlight, HorizontalAlignment.Left, RowFontSize));
		container.AddChild(CreateCell(row.Kills.ToString(), 48f, highlight, HorizontalAlignment.Center, RowFontSize));
		container.AddChild(CreateCell(row.Deaths.ToString(), 56f, highlight, HorizontalAlignment.Center, RowFontSize));

		return container;
	}

	private Label CreateCell(string text, float minWidth, bool highlight, HorizontalAlignment alignment, float fontSize)
	{
		var label = new Label
		{
			Text = text,
			HorizontalAlignment = alignment,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(minWidth, 0f)
		};
		label.AddThemeFontSizeOverride("font_size", (int)fontSize);
		label.Modulate = highlight ? new Color(1.0f, 0.92f, 0.55f) : Colors.White;
		return label;
	}

	public void RefreshHeaders()
	{
		BindNodes();
		if (_panel == null)
			return;

		var headerPlayer = _panel.GetNodeOrNull<Label>("VBox/Header/Player");
		var headerKills = _panel.GetNodeOrNull<Label>("VBox/Header/Kills");
		var headerDeaths = _panel.GetNodeOrNull<Label>("VBox/Header/Deaths");
		if (headerPlayer != null) headerPlayer.AddThemeFontSizeOverride("font_size", (int)ColumnFontSize);
		if (headerKills != null) headerKills.AddThemeFontSizeOverride("font_size", (int)ColumnFontSize);
		if (headerDeaths != null) headerDeaths.AddThemeFontSizeOverride("font_size", (int)ColumnFontSize);
	}

	private static int GetInt(Godot.Collections.Dictionary dict, string key)
	{
		if (!dict.ContainsKey(key))
			return 0;

		var value = (Variant)dict[key];
		switch (value.VariantType)
		{
			case Variant.Type.Int:
				return (int)(long)value;
			case Variant.Type.Float:
				return Mathf.RoundToInt((float)(double)value);
			case Variant.Type.String:
				return int.TryParse((string)value, out var parsed) ? parsed : 0;
			default:
				return 0;
		}
	}

	private readonly record struct ScoreRow(int Id, string Name, int Kills, int Deaths);
}
