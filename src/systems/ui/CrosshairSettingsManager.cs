using Godot;

public enum CrosshairShape
{
    Cross = 0,
    Dot = 1,
    Circle = 2,
}

[GlobalClass]
public partial class CrosshairSettingsManager : Node
{
    public static CrosshairSettingsManager? Instance { get; private set; }

    [Signal]
    public delegate void ChangedEventHandler();

    private const string ConfigPath = "user://crosshair_settings.cfg";
    private const string Section = "crosshair";

    private CrosshairShape _shape = CrosshairShape.Cross;
    private float _size = 1.0f; // scale multiplier
    private Color _color = Colors.White;

    public CrosshairShape Shape => _shape;
    public float Size => _size;
    public Color Color => _color;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            GD.PushWarning("CrosshairSettingsManager: Duplicate instance detected; freeing the new one.");
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
        Load();
    }

    public void SetShape(CrosshairShape shape, bool save = true)
    {
        _shape = shape;
        if (save) Save();
        EmitSignal(SignalName.Changed);
    }

    public void SetSize(float size, bool save = true)
    {
        _size = Mathf.Clamp(size, 0.25f, 5.0f);
        if (save) Save();
        EmitSignal(SignalName.Changed);
    }

    public void SetColor(Color color, bool save = true)
    {
        _color = color;
        if (save) Save();
        EmitSignal(SignalName.Changed);
    }

    public void Load()
    {
        var cfg = new ConfigFile();
        var err = cfg.Load(ConfigPath);
        if (err != Error.Ok)
        {
            // First run: apply defaults
            Save();
            return;
        }

        // Shape (stored as int or string)
        if (cfg.HasSectionKey(Section, "shape"))
        {
            Variant v = cfg.GetValue(Section, "shape");
            if (v.VariantType == Variant.Type.Int)
            {
                _shape = (CrosshairShape)(int)v;
            }
            else
            {
                var s = v.ToString();
                if (System.Enum.TryParse<CrosshairShape>(s, out var parsed))
                {
                    _shape = parsed;
                }
            }
        }

        // Size
        if (cfg.HasSectionKey(Section, "size"))
        {
            Variant v = cfg.GetValue(Section, "size");
            if (v.VariantType == Variant.Type.Float)
            {
                _size = Mathf.Clamp(v.AsSingle(), 0.25f, 5.0f);
            }
            else
            {
                var s = v.ToString();
                if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                {
                    _size = Mathf.Clamp(f, 0.25f, 5.0f);
                }
            }
        }

        // Color (stored as HTML hex)
        if (cfg.HasSectionKey(Section, "color"))
        {
            var s = cfg.GetValue(Section, "color").ToString();
            try
            {
                _color = new Color(s);
            }
            catch
            {
                _color = Colors.White;
            }
        }

        EmitSignal(SignalName.Changed);
    }

    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "shape", (int)_shape);
        cfg.SetValue(Section, "size", _size);
        cfg.SetValue(Section, "color", _color.ToHtml(true));
        var err = cfg.Save(ConfigPath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"CrosshairSettingsManager: Failed to save settings ({err}).");
        }
    }
}
