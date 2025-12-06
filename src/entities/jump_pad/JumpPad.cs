using Godot;
using System.Collections.Generic;

public partial class JumpPad : StaticBody3D
{
    [ExportGroup("Jump Settings")]
    [Export(PropertyHint.Range, "5,80,0.1")] public float CharacterUpwardVelocity { get; set; } = 22f;
    [Export(PropertyHint.Range, "50,8000,1")] public float RigidUpwardImpulse { get; set; } = 2200f;
    [Export(PropertyHint.Range, "0,2,0.01")] public float CooldownSeconds { get; set; } = 0.2f;
    [Export(PropertyHint.Range, "0,60,0.1")] public float MinRigidUpwardVelocity { get; set; } = 18f;

    [ExportGroup("Audio")]
    [Export(PropertyHint.File, "*.mp3,*.ogg,*.wav")] public AudioStream JumpSfx { get; set; }
    [Export(PropertyHint.Range, "-40,6,0.5")] public float JumpSfxVolumeDb { get; set; } = -10f;
    [Export(PropertyHint.Range, "1,200,1")] public float JumpSfxMaxDistance { get; set; } = 32f;
    [Export(PropertyHint.Range, "0.5,2,0.01")] public float JumpSfxPitchScale { get; set; } = 1.0f;

    private Area3D _area;
    private AudioStreamPlayer3D _audioPlayer;
    private readonly Dictionary<Node3D, double> _cooldowns = new();

    public override void _Ready()
    {
        _area = GetNodeOrNull<Area3D>("DetectionArea");
        if (_area == null)
        {
            GD.PrintErr("[JumpPad] Missing DetectionArea child Area3D");
            return;
        }

        _area.BodyEntered += OnBodyEntered;
        _area.BodyExited += OnBodyExited;

        InitializeAudio();

        SetPhysicsProcess(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Decrement per-body cooldown timers
        if (_cooldowns.Count == 0) return;
        var toClear = new List<Node3D>();
        foreach (var kv in _cooldowns)
        {
            var remaining = kv.Value - delta;
            if (remaining <= 0)
                toClear.Add(kv.Key);
            else
                _cooldowns[kv.Key] = remaining;
        }
        foreach (var k in toClear)
            _cooldowns.Remove(k);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body == null) return;

        // Debounce repeated triggers while overlapping
        if (_cooldowns.ContainsKey(body)) return;

        bool applied = false;

        var launchNormal = GetLaunchNormal();

        if (body is PlayerCharacter player)
        {
            var launchVelocity = launchNormal * CharacterUpwardVelocity;
            player.ApplyLaunchVelocity(launchVelocity);
            GD.Print($"[JumpPad] Boosted PlayerCharacter '{body.Name}' to v={launchVelocity}");
            applied = true;
        }
        else if (body is CharacterBody3D character)
        {
            var launchVelocity = launchNormal * CharacterUpwardVelocity;
            character.Velocity = launchVelocity;
            GD.Print($"[JumpPad] Boosted CharacterBody3D '{body.Name}' to v={launchVelocity}");
            applied = true;
        }
        else if (body is RigidBody3D rigid)
        {
            rigid.ApplyImpulse(launchNormal * RigidUpwardImpulse);
            var lv = rigid.LinearVelocity;
            var alongNormal = lv.Dot(launchNormal);
            if (alongNormal < MinRigidUpwardVelocity)
            {
                lv += launchNormal * (MinRigidUpwardVelocity - alongNormal);
                rigid.LinearVelocity = lv;
            }
            GD.Print($"[JumpPad] Boosted RigidBody3D '{body.Name}' impulse={RigidUpwardImpulse} newV={rigid.LinearVelocity}");
            applied = true;
        }

        if (applied)
        {
            // Start cooldown to avoid re-trigger spamming while intersecting
            _cooldowns[body] = CooldownSeconds;

            // Optional: simple visual feedback pulse if a mesh exists
            var mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
            if (mesh != null)
            {
                var tween = CreateTween();
                tween.TweenProperty(mesh, "scale", new Vector3(1.1f, 1.0f, 1.1f), 0.08);
                tween.TweenProperty(mesh, "scale", Vector3.One, 0.12);
            }

            PlayJumpSound(body.GlobalPosition);
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (body == null) return;
        _cooldowns.Remove(body);
    }

    private void InitializeAudio()
    {
        _audioPlayer = new AudioStreamPlayer3D();

        if (JumpSfx == null)
        {
            JumpSfx = GD.Load<AudioStream>("res://sounds/environmental/jump-pad.mp3");
            if (JumpSfx == null)
            {
                GD.PushWarning("[JumpPad] JumpSfx not assigned and fallback load failed; audio disabled.");
                return;
            }
        }

        _audioPlayer.Stream = JumpSfx;
        _audioPlayer.Bus = AudioSettingsManager.GameAudioBusName;
        _audioPlayer.VolumeDb = JumpSfxVolumeDb;
        _audioPlayer.MaxDistance = JumpSfxMaxDistance;
        _audioPlayer.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance;
        _audioPlayer.PitchScale = JumpSfxPitchScale;
        _audioPlayer.Autoplay = false;
        AddChild(_audioPlayer);
    }

    private void PlayJumpSound(Vector3 position)
    {
        if (_audioPlayer == null || _audioPlayer.Stream == null) return;
        _audioPlayer.GlobalPosition = position;
        if (_audioPlayer.Playing) _audioPlayer.Stop();
        _audioPlayer.Play();
    }

    private Vector3 GetLaunchNormal()
    {
        var normal = GlobalTransform.Basis.Y;
        if (normal.LengthSquared() <= Mathf.Epsilon)
            return Vector3.Up;
        return normal.Normalized();
    }
}
