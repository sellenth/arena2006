using Godot;
using System;

public partial class MachineGunProjectile : Node3D
{
    [Export] public float Lifetime { get; set; } = 1.2f;
    [Export] public uint CollisionMask { get; set; } = 3;

    public long BulletId { get; private set; }
    public long OwnerPeerId { get; private set; }
    public bool ServerAuthority { get; private set; }
    public float Damage { get; private set; }
    public Action<MachineGunProjectile>? ReturnToPool { get; set; }

    private Vector3 _velocity = Vector3.Zero;
    private float _lifeTimer = 0f;
    private bool _active = false;
    private readonly Godot.Collections.Array<Rid> _excludeRids = new();

    public event Action<long, Node?, Vector3, Vector3, float>? OnServerImpact;
    public event Action<long>? OnServerLifetimeExpired;

    public override void _Ready()
    {
        Visible = false;
        SetPhysicsProcess(false);
    }

    public void Initialize(long bulletId, long ownerPeerId, bool serverAuthority, Vector3 position, Basis rotation, Vector3 velocity, float damage)
    {
        BulletId = bulletId;
        OwnerPeerId = ownerPeerId;
        ServerAuthority = serverAuthority;
        Damage = damage;
        _velocity = velocity;
        ResetForSpawn();
        GlobalTransform = new Transform3D(rotation, position);
    }

    public void ResetToPoolState()
    {
        _active = false;
        _velocity = Vector3.Zero;
        _lifeTimer = 0f;
        Visible = false;
        SetPhysicsProcess(false);
        _excludeRids.Clear();
        OnServerImpact = null;
        OnServerLifetimeExpired = null;
        ServerAuthority = false;
        Damage = 0f;
        BulletId = 0;
        OwnerPeerId = 0;
    }

    public void RegisterCollisionException(Node node)
    {
        if (node is CollisionObject3D co)
        {
            var rid = co.GetRid();
            if (!_excludeRids.Contains(rid))
            {
                _excludeRids.Add(rid);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active) return;
        float dt = (float)delta;
        Vector3 start = GlobalPosition;
        Vector3 end = start + _velocity * dt;

        if (ServerAuthority)
        {
            var space = GetWorld3D()?.DirectSpaceState;
            if (space != null)
            {
                var query = PhysicsRayQueryParameters3D.Create(start, end);
                query.CollisionMask = CollisionMask;
                query.CollideWithAreas = true;
                query.CollideWithBodies = true;
                if (_excludeRids.Count > 0)
                {
                    query.Exclude = _excludeRids;
                }
                var result = space.IntersectRay(query);
                if (result.Count > 0)
                {
                    var hitPos = (Vector3)result["position"];
                    var hitNorm = result.ContainsKey("normal") ? (Vector3)result["normal"] : Vector3.Zero;

                    Node? collider = null;
                    if (result.TryGetValue("collider", out var colliderVariant) && colliderVariant.VariantType == Variant.Type.Object)
                    {
                        var godotObj = colliderVariant.AsGodotObject();
                        collider = godotObj as Node;
                    }

                    GlobalPosition = hitPos;
                    OnServerImpact?.Invoke(BulletId, collider, hitPos, hitNorm, Damage);
                    ReleaseToPool();
                    return;
                }
            }
        }

        GlobalPosition = end;
        _lifeTimer += dt;
        if (_lifeTimer >= Lifetime)
        {
            if (ServerAuthority)
            {
                OnServerLifetimeExpired?.Invoke(BulletId);
            }
            ReleaseToPool();
        }
    }

    private void ResetForSpawn()
    {
        _lifeTimer = 0f;
        _active = true;
        Visible = true;
        SetPhysicsProcess(true);
    }

    private void ReleaseToPool()
    {
        _active = false;
        SetPhysicsProcess(false);
        Visible = false;
        _excludeRids.Clear();
        OnServerImpact = null;
        OnServerLifetimeExpired = null;
        if (ReturnToPool != null)
        {
            ReturnToPool.Invoke(this);
        }
        else
        {
            QueueFree();
        }
    }

    public void ForceRelease()
    {
        if (_active)
        {
            ReleaseToPool();
        }
    }
}
