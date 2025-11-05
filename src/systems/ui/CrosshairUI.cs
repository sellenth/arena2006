using Godot;

public partial class CrosshairUI : Control
{
    private CrosshairSettingsManager _settings;

    private Vector2 _viewportSize;

    public override void _Ready()
    {
        Name = "CrosshairUI";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorsPreset = (int)LayoutPreset.FullRect;
        _viewportSize = GetViewportRect().Size;

        UpdateVisibility();
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            _viewportSize = GetViewportRect().Size;
            QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        // Hide crosshair when mouse is released for UI (menus), show when captured and in Shooter mode
        bool expectingVisible = (Input.MouseMode == Input.MouseModeEnum.Captured);
        if (Visible != expectingVisible)
        {
            Visible = expectingVisible;
        }
    }

    private void UpdateVisibility()
    {
        Visible = (Input.MouseMode == Input.MouseModeEnum.Captured);
    }

    private void OnSettingsChanged()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_settings == null)
        {
            _settings = CrosshairSettingsManager.Instance; // Attempt lazy fetch if created later
        }

        var color = _settings?.Color ?? Colors.White;
        var scale = _settings?.Size ?? 1.0f;
        var shape = _settings?.Shape ?? CrosshairShape.Cross;

        Vector2 center = _viewportSize * 0.5f;

        switch (shape)
        {
            case CrosshairShape.Cross:
                DrawCross(center, color, scale);
                break;
            case CrosshairShape.Dot:
                DrawDot(center, color, scale);
                break;
            case CrosshairShape.Circle:
                DrawCircleOutline(center, color, scale);
                break;
        }
    }

    private void DrawCross(Vector2 center, Color color, float scale)
    {
        float len = 12f * scale;      // line length from center outward
        float gap = 4f * scale;       // gap around exact center
        float thickness = Mathf.Clamp(2f * scale, 1f, 4f);

        // Horizontal left
        DrawLine(new Vector2(center.X - gap, center.Y), new Vector2(center.X - len, center.Y), color, thickness);
        // Horizontal right
        DrawLine(new Vector2(center.X + gap, center.Y), new Vector2(center.X + len, center.Y), color, thickness);
        // Vertical up
        DrawLine(new Vector2(center.X, center.Y - gap), new Vector2(center.X, center.Y - len), color, thickness);
        // Vertical down
        DrawLine(new Vector2(center.X, center.Y + gap), new Vector2(center.X, center.Y + len), color, thickness);
    }

    private void DrawDot(Vector2 center, Color color, float scale)
    {
        float r = Mathf.Clamp(2f * scale, 1f, 6f);
        DrawCircle(center, r, color);
    }

    private void DrawCircleOutline(Vector2 center, Color color, float scale)
    {
        float r = 10f * scale;
        float thickness = Mathf.Clamp(2f * scale, 1f, 4f);
        int points = 48;
        // Full 360 degrees arc
        DrawArc(center, r, 0f, Mathf.Tau, points, color, thickness, true);
    }
}
