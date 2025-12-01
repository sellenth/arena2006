using Godot;

public partial class HitMarkerUI : Control
{
    [Export] public float EntryHoldSeconds { get; set; } = 0.12f;
    [Export] public float FadeOutSeconds { get; set; } = 0.18f;
    [Export] public float MachineGunReferenceDamage { get; set; } = 12.0f;
    [Export] public float RocketReferenceDamage { get; set; } = 60.0f;
    [Export] public float MinLineLength { get; set; } = 10.0f;
    [Export] public float MaxLineLength { get; set; } = 20.0f;
    [Export] public float MinGap { get; set; } = 4.0f;
    [Export] public float MaxGap { get; set; } = 5.0f;
    [Export] public float MinLineWidth { get; set; } = 2.0f;
    [Export] public float MaxLineWidth { get; set; } = 5.0f;
    [Export] public float MinPitch { get; set; } = 0.65f;
    [Export] public float MaxPitch { get; set; } = 1.45f;
    [Export] public float BaseVolumeDb { get; set; } = -4.0f;
    [Export(PropertyHint.File, "*.ogg,*.wav,*.mp3")] public string AudioPath { get; set; } = "res://src/entities/hitmarker/impactGeneric_light_002.ogg";
    [Export] public Color HitColor { get; set; } = new(1f, 1f, 1f, 0.95f);
    [Export] public Color KillColor { get; set; } = new(1f, 0.3f, 0.2f, 1f);

    private float _timer = 0f;
    private float _duration = 0f;
    private float _entryHold = 0f;
    private float _damageStrength = 0f;
    private bool _wasKill = false;
    private float _lineLength = 26f;
    private float _lineGap = 10f;
    private float _lineWidth = 2.5f;
    private Color _currentColor = Colors.Transparent;
    private AudioStreamPlayer? _audioPlayer;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetProcess(false);

        _audioPlayer = GetNodeOrNull<AudioStreamPlayer>("HitAudio");
        if (_audioPlayer == null)
        {
            _audioPlayer = new AudioStreamPlayer
            {
                Name = "HitAudio",
                Bus = ResolveFxBusName(),
                Autoplay = false,
                VolumeDb = BaseVolumeDb
            };
            AddChild(_audioPlayer);
        }
        else
        {
            _audioPlayer.Bus = ResolveFxBusName();
        }

        LoadAudioStream();
    }

    public void RefreshAudio()
    {
        LoadAudioStream();
    }

    public void ShowHitFeedback(float damage, WeaponType weaponType, bool wasKill)
    {
        if (damage < 0f) damage = 0f;
        _wasKill = wasKill;
        _damageStrength = Mathf.Clamp(NormalizeDamage(damage, weaponType), 0f, 1f);
        _entryHold = Mathf.Max(0.02f, EntryHoldSeconds);
        _duration = _entryHold + Mathf.Max(0.03f, FadeOutSeconds);
        _timer = _duration;

        float strengthEase = EaseOutCubic(_damageStrength);
        _lineLength = Mathf.Lerp(MinLineLength, MaxLineLength, strengthEase);
        _lineGap = Mathf.Lerp(MinGap, MaxGap, 1f - strengthEase);
        _lineWidth = Mathf.Lerp(MinLineWidth, MaxLineWidth, strengthEase);

        _currentColor = wasKill ? KillColor : HitColor;
        _currentColor.A = wasKill ? KillColor.A : HitColor.A;

        Visible = true;
        SetProcess(true);
        QueueRedraw();

        PlayHitSound(strengthEase, wasKill);
    }

    public override void _Process(double delta)
    {
        if (_timer <= 0f)
        {
            _timer = 0f;
            Visible = false;
            SetProcess(false);
            return;
        }

        _timer -= (float)delta;

        float progress = 1f - Mathf.Clamp(_timer / _duration, 0f, 1f);
        float fadeStart = _entryHold / _duration;
        float fadeProgress = 0f;
        if (progress > fadeStart)
        {
            float fadeT = Mathf.Clamp((progress - fadeStart) / Mathf.Max(0.0001f, 1f - fadeStart), 0f, 1f);
            fadeProgress = fadeT;
        }

        float alpha = Mathf.Lerp(_currentColor.A, 0f, EaseInCubic(fadeProgress));
        var baseColor = _wasKill ? KillColor : HitColor;
        baseColor.A = alpha;
        _currentColor = baseColor;

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Visible) return;
        if (_currentColor.A <= 0.001f) return;

        Vector2 size = GetRect().Size;
        Vector2 center = size * 0.5f;

        float animStretch = 1f + (_damageStrength * 0.18f);
        float length = _lineLength * animStretch;
        float gap = _lineGap * (1f + 0.35f * (1f - _damageStrength));

        DrawLine(center + new Vector2(-gap - length, 0f), center + new Vector2(-gap, 0f), _currentColor, _lineWidth, true);
        DrawLine(center + new Vector2(gap, 0f), center + new Vector2(gap + length, 0f), _currentColor, _lineWidth, true);
        DrawLine(center + new Vector2(0f, -gap - length), center + new Vector2(0f, -gap), _currentColor, _lineWidth, true);
        DrawLine(center + new Vector2(0f, gap), center + new Vector2(0f, gap + length), _currentColor, _lineWidth, true);
    }

    private void PlayHitSound(float strengthEase, bool wasKill)
    {
        if (_audioPlayer == null)
        {
            return;
        }

        if (_audioPlayer.Stream == null)
        {
            LoadAudioStream();
            if (_audioPlayer.Stream == null)
            {
                return;
            }
        }

        float pitch = Mathf.Lerp(MaxPitch, MinPitch, strengthEase);
        if (wasKill)
        {
            pitch *= 0.9f;
        }
        float volumeBoost = strengthEase * 4.0f;
        if (wasKill) volumeBoost += 2.5f;

        _audioPlayer.Stop();
        _audioPlayer.PitchScale = Mathf.Clamp(pitch, 0.3f, 3.0f);
        _audioPlayer.VolumeDb = BaseVolumeDb + volumeBoost;
        _audioPlayer.Play();
    }

    private float NormalizeDamage(float damage, WeaponType weaponType)
    {
        float reference = weaponType switch
        {
            WeaponType.RocketLauncher => RocketReferenceDamage,
            WeaponType.MachineGun => MachineGunReferenceDamage,
            _ => MachineGunReferenceDamage
        };
        if (reference <= 0.0001f) reference = 1.0f;
        return Mathf.Clamp(damage / reference, 0f, 1f);
    }

    private void LoadAudioStream()
    {
        if (_audioPlayer == null) return;
        if (string.IsNullOrEmpty(AudioPath)) return;

        var stream = ResourceLoader.Exists(AudioPath)
            ? ResourceLoader.Load<AudioStream>(AudioPath)
            : null;

        if (stream == null)
        {
            GD.PushWarning($"HitMarkerUI: Unable to load audio stream at '{AudioPath}'");
            return;
        }

        _audioPlayer.Stream = stream;
        _audioPlayer.Bus = ResolveFxBusName();
        _audioPlayer.VolumeDb = BaseVolumeDb;
    }

    private string ResolveFxBusName()
    {
        var mgr = AudioSettingsManager.Instance;
        return mgr != null ? mgr.GetBusName(AudioChannel.Fx) : AudioSettingsManager.FxBusName;
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private static float EaseInCubic(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * t;
    }
}
