using Godot;
using System;

public partial class ScoreUI : Control
{
    private const float RowFontSize = 16f;

    private PanelContainer? _panel;
    private VBoxContainer? _rowsContainer;
    private Label? _titleLabel;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        BindNodes();
        ClearRows();
        ShowEmptyState();
    }

    public override void _Process(double delta)
    {
    }

    public override void _ExitTree()
    {
    }

    private void BindNodes()
    {
        if (_panel != null && _rowsContainer != null)
        {
            return;
        }

        _panel = GetNodeOrNull<PanelContainer>("ScoreboardPanel");
        if (_panel == null)
        {
            GD.PrintErr("[ScoreUI] Missing ScoreboardPanel child; please check race_ui.tscn layout.");
            return;
        }

        _titleLabel = _panel.GetNodeOrNull<Label>("Stack/TitleLabel");
        _rowsContainer = _panel.GetNodeOrNull<VBoxContainer>("Stack/Rows");

        if (_rowsContainer == null)
        {
            GD.PrintErr("[ScoreUI] Missing Rows container under ScoreboardPanel/Stack.");
        }
    }

    private void BindLobby()
    {
        BindNodes();
        var world = GetTree()?.Root?.GetNodeOrNull<Node>("/root/World");
        if (world == null)
        {
            return;
        }

    }

    private void OnScoreboardUpdated(Godot.Collections.Array scoreboard)
    {
        ApplyScoreboard(scoreboard);
    }

    private void ApplyScoreboard(Godot.Collections.Array scoreboard)
    {
        BindNodes();
        if (_rowsContainer == null)
        {
            return;
        }

        ClearRows();

        if (scoreboard == null || scoreboard.Count == 0)
        {
            ShowEmptyState();
            return;
        }

        for (int i = 0; i < scoreboard.Count; i++)
        {
            Variant rowVariant = scoreboard[i];
            if (rowVariant.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var row = rowVariant.AsGodotDictionary();

            long peerId = row.ContainsKey("id") ? (long)row["id"] : 0L;
            string name = row.ContainsKey("name") ? (string)row["name"] : $"p{peerId}";
            bool finished = row.ContainsKey("finished") && (bool)row["finished"];
            int rank = row.ContainsKey("rank") ? (int)row["rank"] : 0;
            int elims = row.ContainsKey("elims") ? (int)row["elims"] : 0;
            int points = row.ContainsKey("points") ? (int)row["points"] : 0;
            float finishTime = row.ContainsKey("finish_time") ? (float)(double)row["finish_time"] : 0f;
            bool isReady = row.ContainsKey("is_ready") && (bool)row["is_ready"];
            bool isHost = row.ContainsKey("is_host") && (bool)row["is_host"];

            string rankText = finished && rank > 0 ? rank.ToString() : "--";
            string elimText = elims.ToString();
            string pointsText = points.ToString();
            string timeText = finished && finishTime > 0f ? FormatTime(finishTime) : string.Empty;
            
            // Ready status indicator
            string readyText = "";
            if (isHost)
            {
                readyText = "HOST";
            }
            else
            {
                readyText = isReady ? "✓" : "✗";
            }

            bool isLocal = Multiplayer.HasMultiplayerPeer() && peerId == Multiplayer.GetUniqueId();

            var rowContainer = new HBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
            };
            SetSeparation(rowContainer, 12);

            rowContainer.AddChild(CreateValueLabel(rankText, 28, HorizontalAlignment.Left, isLocal));
            rowContainer.AddChild(CreateValueLabel(name, 100, HorizontalAlignment.Left, isLocal));
            rowContainer.AddChild(CreateReadyStatusLabel(readyText, isHost, isReady, isLocal));
            rowContainer.AddChild(CreateValueLabel(elimText, 40, HorizontalAlignment.Center, isLocal));
            rowContainer.AddChild(CreateValueLabel(pointsText, 40, HorizontalAlignment.Center, isLocal));
            rowContainer.AddChild(CreateValueLabel(timeText, 76, HorizontalAlignment.Right, isLocal));

            _rowsContainer.AddChild(rowContainer);
        }

        if (_rowsContainer.GetChildCount() == 0)
        {
            ShowEmptyState();
        }
    }

    private void ClearRows()
    {
        BindNodes();
        if (_rowsContainer == null)
        {
            return;
        }

        foreach (Node child in _rowsContainer.GetChildren())
        {
            child.QueueFree();
        }
    }

    private void ShowEmptyState(string message = "Waiting for race...")
    {
        BindNodes();
        if (_rowsContainer == null)
        {
            return;
        }

        var placeholder = new Label
        {
            Text = message,
            Modulate = new Color(0.75f, 0.75f, 0.75f),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        placeholder.AddThemeFontSizeOverride("font_size", (int)RowFontSize);
        _rowsContainer.AddChild(placeholder);
    }

    private Label CreateValueLabel(string text, float minWidth, HorizontalAlignment alignment, bool highlight)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(minWidth, 0f),
        };
        label.AddThemeFontSizeOverride("font_size", (int)RowFontSize);
        label.Modulate = highlight ? new Color(1.0f, 0.9f, 0.45f, 1f) : Colors.White;
        return label;
    }
    
    private Label CreateReadyStatusLabel(string text, bool isHost, bool isReady, bool highlight)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(60f, 0f),
        };
        label.AddThemeFontSizeOverride("font_size", (int)RowFontSize);
        
        // Color coding for ready status
        Color modulate;
        if (isHost)
        {
            modulate = highlight ? Colors.Orange : new Color(1.0f, 0.8f, 0.4f); // Orange for host
        }
        else if (isReady)
        {
            modulate = highlight ? Colors.LightGreen : Colors.Green; // Green for ready
        }
        else
        {
            modulate = highlight ? new Color(1.0f, 0.8f, 0.8f) : Colors.Red; // Red for not ready
        }
        
        label.Modulate = modulate;
        return label;
    }

    private static void SetSeparation(BoxContainer container, int pixels)
    {
        container.AddThemeConstantOverride("separation", pixels);
    }



    private static string FormatTime(float seconds)
    {
        if (seconds <= 0f || float.IsNaN(seconds))
        {
            return string.Empty;
        }

        int minutes = (int)(seconds / 60f);
        float remainder = seconds - minutes * 60f;
        return $"{minutes:00}:{remainder:00.00}";
    }
}
