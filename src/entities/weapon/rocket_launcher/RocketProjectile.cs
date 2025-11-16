using Godot;
using System;
using System.Collections.Generic;

    public partial class RocketProjectile : RigidBody3D, IPooledProjectile
{
    [Export] public float Speed { get; set; } = 30.0f;
    [Export] public float ExplodeRadius { get; set; } = 6.0f;
    [Export] public float Lifetime { get; set; } = 6.0f;

    [ExportGroup("Audio")]
    [Export] public AudioStream ExplosionSfx { get; set; }
    [Export] public float ExplosionVolumeDb { get; set; } = -12.0f;
    [Export] public float ExplosionMaxDistance { get; set; } = 160.0f;
    [Export] public AudioStream FlightLoopSfx { get; set; }
    [Export] public float FlightVolumeDb { get; set; } = -16.0f;
    [Export] public float FlightMaxDistance { get; set; } = 80.0f;

    [ExportGroup("Decal")]
    [Export] public Texture2D? ExplosionDecalTexture { get; set; }
    [Export] public float ExplosionDecalSize { get; set; } = 3.0f;
    [Export] public float ExplosionDecalLifetimeSec { get; set; } = 30.0f;

    [ExportGroup("Safety")]
    [Export] public float ArmDelaySec { get; set; } = 0.00f; // ignore collisions briefly after spawn

    public long OwnerPeerId { get; private set; } = 0;
    public long RocketId { get; private set; } = 0;
    public bool ServerAuthority { get; private set; } = false;
    public bool PlayAudio { get; set; } = true;
    public Action<RocketProjectile>? ReturnToPool { get; set; }
        = null; // Assigned by the rocket pool when pooling is enabled.

    private bool _exploded = false;
    private float _lifeTimer = 0f;
    private bool _armed = false;
    private GpuParticles3D _trailParticles;
    private Area3D _hitArea;
    private AudioStreamPlayer3D _flightPlayer;
    private bool _connectedRigidBodySignal = false;
    private bool _connectedHitAreaSignal = false;
    private bool _active = false;
    private readonly List<Node> _collisionExceptions = new();

    // Raised on the server when this rocket explodes (collision or lifetime)
    public event Action<long, Vector3>? OnServerExploded;

    public override void _Ready()
    {
        // Cache visuals
        _trailParticles = GetNodeOrNull<GpuParticles3D>("TrailParticles");
        _hitArea = GetNodeOrNull<Area3D>("HitDetectionArea");

        UpdateAuthoritySignals();
    }

    public override void _ExitTree()
    {
        if (_connectedRigidBodySignal)
        {
            BodyEntered -= HandleBodyEntered;
            _connectedRigidBodySignal = false;
        }
        if (_connectedHitAreaSignal && _hitArea != null)
        {
            _hitArea.BodyEntered -= HandleBodyEntered;
            _connectedHitAreaSignal = false;
        }
        if (_flightPlayer != null && IsInstanceValid(_flightPlayer))
        {
            _flightPlayer.Stop();
            _flightPlayer.QueueFree();
            _flightPlayer = null;
        }
    }

    public void Initialize(long rocketId, long ownerPeerId, bool serverAuthority, Vector3 position, Vector3 initialVelocity)
    {
        RocketId = rocketId;
        OwnerPeerId = ownerPeerId;
        ServerAuthority = serverAuthority;
        ResetForSpawn();
        UpdateAuthoritySignals();

        GlobalPosition = position;
        GravityScale = 0.0f;
        ContinuousCd = true;
        AngularDamp = 0f;
        LinearDamp = 0f;

        // Face velocity direction
        if (initialVelocity.LengthSquared() > 0.001f)
        {
            LookAt(GlobalPosition + initialVelocity.Normalized(), Vector3.Up);
        }

        // On clients (non-authority visuals), freeze physics so we can set transforms from network updates
        Freeze = !ServerAuthority;
        Sleeping = false;

        LinearVelocity = initialVelocity;
        AngularVelocity = Vector3.Zero;

        if (PlayAudio && !Multiplayer.IsServer())
        {
            StartFlightAudio();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active || _exploded) return;
        _lifeTimer += (float)delta;
        if (!_armed && _lifeTimer >= ArmDelaySec)
        {
            _armed = true;
        }
        if (_lifeTimer >= Lifetime)
        {
            if (ServerAuthority)
            {
                ServerExplode();
            }
            else
            {
                ClientExplodeFx();
            }
        }
    }

    private void HandleBodyEntered(Node body)
    {
        if (_exploded) return;
        if (!_armed) return; // still within arm delay window
        // Ignore collisions with other rockets to reduce chain popping
        if (body is RocketProjectile) return;
        // Optionally ignore immediate collision with owner (could add owner tagging via groups)
        ServerExplode();
    }

    private void ServerExplode()
    {
        if (_exploded) return;
        _exploded = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        SetPhysicsProcess(false);

        OnServerExploded?.Invoke(RocketId, GlobalPosition);
        // Server doesn't need local SFX; the clients handle audio/FX when told to destroy
        ReleaseToPool();
    }

    public void ClientExplodeFx()
    {
        if (_exploded) return;
        _exploded = true;
        SetPhysicsProcess(false);
        if (_trailParticles != null) _trailParticles.Emitting = false;
        if (_flightPlayer != null && IsInstanceValid(_flightPlayer))
        {
            _flightPlayer.Stop();
            _flightPlayer.QueueFree();
            _flightPlayer = null;
        }
        CreateExplosionEffect();
        PlayExplosionSfx();
        // Try to place an explosion residue decal on the impacted world surface (client-side visual only)
        DecalUtils.SpawnExplosionDecal(
            this,
            GlobalPosition,
            LinearVelocity,
            ExplosionDecalTexture,
            ExplosionDecalSize,
            ExplosionDecalLifetimeSec,
            this
        );
        ReleaseToPool();
    }

    public void ApplyNetworkUpdate(Vector3 pos, Vector3 vel)
    {
        if (ServerAuthority || !_active) return; // server sim owns physics or pooled
        GlobalPosition = pos;
        LinearVelocity = vel;
        if (vel.LengthSquared() > 0.001f)
        {
            LookAt(GlobalPosition + vel.Normalized(), Vector3.Up);
        }
    }

    private void StartFlightAudio()
    {
        if (FlightLoopSfx == null)
        {
            FlightLoopSfx = GD.Load<AudioStream>("res://sounds/rocket_loop.mp3");
        }
        if (FlightLoopSfx == null) return;

        _flightPlayer = new AudioStreamPlayer3D();
        _flightPlayer.Stream = FlightLoopSfx;
        _flightPlayer.Bus = AudioSettingsManager.WeaponsBusName;
        _flightPlayer.VolumeDb = FlightVolumeDb;
        _flightPlayer.MaxDistance = FlightMaxDistance;
        _flightPlayer.UnitSize = 4.0f;
        _flightPlayer.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance;
        AddChild(_flightPlayer);
        _flightPlayer.GlobalPosition = GlobalPosition;
        _flightPlayer.Play();
    }

    private void PlayExplosionSfx()
    {
        if (ExplosionSfx == null)
        {
            ExplosionSfx = GD.Load<AudioStream>("res://sounds/explosion.mp3");
        }
        if (ExplosionSfx == null) return;

        var player = new AudioStreamPlayer3D();
        player.Stream = ExplosionSfx;
        player.Bus = AudioSettingsManager.WeaponsBusName;
        player.VolumeDb = ExplosionVolumeDb;
        player.MaxDistance = ExplosionMaxDistance;
        player.UnitSize = 3.0f;
        player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance;
        GetTree().CurrentScene.AddChild(player);
        player.GlobalPosition = GlobalPosition;
        player.Finished += () => { if (IsInstanceValid(player)) player.QueueFree(); };
        player.Play();
    }

    private void CreateExplosionEffect()
    {
        // Minimal particle puff
        var particles = new GpuParticles3D();
        particles.Amount = 60;
        particles.Lifetime = 0.8f;
        particles.OneShot = true;
        particles.Emitting = true;
        var mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0, 1, 0);
        mat.InitialVelocityMin = 5.0f;
        mat.InitialVelocityMax = 12.0f;
        mat.AngularVelocityMin = -180.0f; mat.AngularVelocityMax = 180.0f;
        mat.LinearAccelMin = -10.0f; mat.LinearAccelMax = -10.0f;
        mat.ScaleMin = 0.5f; mat.ScaleMax = 1.8f;
        mat.Color = new Color(1.0f, 0.5f, 0.1f);
        particles.ProcessMaterial = mat;
        var mesh = new SphereMesh(); mesh.RadialSegments = 4; mesh.Rings = 2; mesh.Radius = 0.1f;
        particles.DrawPass1 = mesh;
        GetTree().CurrentScene.AddChild(particles);
        particles.GlobalPosition = GlobalPosition;
        particles.Finished += () => { if (IsInstanceValid(particles)) particles.QueueFree(); };
    }

    public void ResetToPoolState()
    {
        _active = false;
        _exploded = false;
        _lifeTimer = 0f;
        _armed = false;
        SetPhysicsProcess(false);
        Freeze = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        SetContactMonitorSafe(false);
        if (_trailParticles != null) _trailParticles.Emitting = false;
        SetHitAreaMonitoring(false);
        ResetCollisionExceptions();
        if (_flightPlayer != null && IsInstanceValid(_flightPlayer))
        {
            _flightPlayer.Stop();
            _flightPlayer.QueueFree();
            _flightPlayer = null;
        }
        Visible = false;
        OnServerExploded = null;
        ServerAuthority = false;
        UpdateAuthoritySignals();
    }

    private void ResetForSpawn()
    {
        _active = true;
        _exploded = false;
        _lifeTimer = 0f;
        _armed = false;
        ResetCollisionExceptions();
        SetPhysicsProcess(true);
        Visible = true;

        if (_trailParticles != null)
        {
            _trailParticles.Emitting = true;
        }

        SetHitAreaMonitoring(true);

        if (_flightPlayer != null && IsInstanceValid(_flightPlayer))
        {
            _flightPlayer.Stop();
            _flightPlayer.QueueFree();
            _flightPlayer = null;
        }

        if (ServerAuthority)
        {
            SetContactMonitorSafe(true);
            MaxContactsReported = 16;
        }
        else
        {
            SetContactMonitorSafe(false);
        }
    }

    private void SetContactMonitorSafe(bool enabled)
    {
        // Godot locks contact monitor toggles inside body entered/exited callbacks; defer when disabling mid-physics.
        if (!enabled && IsInsideTree() && Engine.IsInPhysicsFrame())
        {
            CallDeferred(nameof(ApplyContactMonitor), enabled);
            return;
        }

        ContactMonitor = enabled;
    }

    private void ApplyContactMonitor(bool enabled)
    {
        ContactMonitor = enabled;
    }

    private void SetHitAreaMonitoring(bool enabled)
    {
        if (_hitArea == null)
        {
            return;
        }

        if (!_hitArea.IsInsideTree())
        {
            _hitArea.Monitoring = enabled;
            return;
        }

        if (!enabled)
        {
            _hitArea.SetDeferred("monitoring", false);
        }
        else if (Engine.IsInPhysicsFrame())
        {
            _hitArea.SetDeferred("monitoring", true);
        }
        else
        {
            _hitArea.Monitoring = true;
        }
    }

    private void UpdateAuthoritySignals()
    {
        if (ServerAuthority)
        {
            if (!_connectedRigidBodySignal)
            {
                BodyEntered += HandleBodyEntered;
                _connectedRigidBodySignal = true;
            }
            if (_hitArea != null)
            {
                if (!_connectedHitAreaSignal)
                {
                    _hitArea.BodyEntered += HandleBodyEntered;
                    _connectedHitAreaSignal = true;
                }
                SetHitAreaMonitoring(true);
            }
        }
        else
        {
            if (_connectedRigidBodySignal)
            {
                BodyEntered -= HandleBodyEntered;
                _connectedRigidBodySignal = false;
            }
            if (_connectedHitAreaSignal && _hitArea != null)
            {
                _hitArea.BodyEntered -= HandleBodyEntered;
                _connectedHitAreaSignal = false;
            }
            SetHitAreaMonitoring(false);
        }
    }

    private void ReleaseToPool()
    {
        _active = false;
        if (ReturnToPool != null)
        {
            ReturnToPool.Invoke(this);
        }
        else
        {
            QueueFree();
        }
    }

    public void RegisterCollisionException(Node node)
    {
        if (node == null) return;
        AddCollisionExceptionWith(node);
        _collisionExceptions.Add(node);
    }

    private void ResetCollisionExceptions()
    {
        foreach (var node in _collisionExceptions)
        {
            if (IsInstanceValid(node))
            {
                RemoveCollisionExceptionWith(node);
            }
        }
        _collisionExceptions.Clear();
    }
}
