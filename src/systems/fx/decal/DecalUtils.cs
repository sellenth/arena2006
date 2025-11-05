using System;
using System.Collections.Generic;
using Godot;

public static class DecalUtils
{
    private static Texture2D? _cachedScorchTex;
    private const string DefaultScorchPath = "res://src/systems/fx/decal/scorch.png";
    private const int MaxDecalBufferSize = 7;
    private static readonly Queue<Decal> _decalBuffer = new();

    // TODO(perf): Decals
    // - Pool or recycle Decal nodes and/or cap the max number of active decals to reduce node churn and draw overhead.
    // - Gate debug prints behind a flag or remove them in release builds.
    // - Consider capping decal depth to reduce overdraw and stretching across edges (e.g., depth = clamp(size * 0.35, 0.2, 1.0)).
    // - Atlas/shared materials for scorch textures to avoid material state changes.
    // - Cull very distant decals (lifetime or distance-based) to keep counts bounded.

    public static void SpawnExplosionDecal(Node context, Vector3 origin, Vector3? direction,
        Texture2D? customTexture, float size, float lifetime, Node? excludeNode = null)
    {
        if (context == null || !context.IsInsideTree()) return;
        var sceneRoot = FindTopmostNode3D(context);
        var world = context.GetViewport()?.World3D;
        if (sceneRoot == null || world == null)
        {
            GD.Print("[DECAL] Failed to get scene root or world (no top Node3D or Viewport.World3D)");
            return;
        }

        void PlaceDecal()
        {
            // Prefer rocket travel direction; otherwise fall back to downward
            Vector3 dir = (direction.HasValue && direction.Value.LengthSquared() > 0.0001f)
                ? direction.Value.Normalized()
                : Vector3.Down;

            GD.Print($"[DECAL] Attempting to spawn explosion decal at {origin} with direction {dir}, size {size}");

            // First attempt: along the travel direction around the explosion center
            if (!TryPlaceDecal(sceneRoot, world, origin, dir, customTexture, size, lifetime, excludeNode))
            {
                GD.Print("[DECAL] First raycast failed, trying downward fallback");
                // Fallback attempt: downward ray in case the rocket exploded near ground or we missed the exact contact
                if (!TryPlaceDecal(sceneRoot, world, origin + Vector3.Up * 3.0f, Vector3.Down, customTexture, size, lifetime, excludeNode))
                {
                    GD.Print("[DECAL] Both raycast attempts failed - no surface found");
                }
            }
        }

        // If we're not in a safe window for direct space state access, defer until the next physics tick.
        if (!Engine.IsInPhysicsFrame())
        {
            var tree = sceneRoot.GetTree();
            if (tree == null) return;

            SceneTreeTimer timer = tree.CreateTimer(0.0, processInPhysics: true);
            timer.Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(sceneRoot) || !GodotObject.IsInstanceValid(context)) return;
                PlaceDecal();
            };
            return;
        }

        PlaceDecal();
    }

    private static Node3D? FindTopmostNode3D(Node from)
    {
        Node current = from;
        Node3D? top = null;
        while (current != null)
        {
            if (current is Node3D n3d) top = n3d;
            current = current.GetParent();
        }
        return top;
    }

    private static bool TryPlaceDecal(Node3D sceneRoot, World3D world, Vector3 fromOrigin, Vector3 direction,
        Texture2D? customTexture, float size, float lifetime, Node? excludeNode)
    {
        // Cast a reasonably long ray along the intended direction
        const float back = 5.0f;   // how far behind origin to start (opposite direction)
        const float forward = 12.0f; // how far forward to cast along direction
        var from = fromOrigin - direction * back;
        var to = fromOrigin + direction * forward;

        GD.Print($"[DECAL] Raycasting from {from} to {to}");

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        // We want bodies (world geometry); areas not needed for decals
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;

        var result = world.DirectSpaceState.IntersectRay(query);
        if (result == null || result.Count == 0)
        {
            GD.Print("[DECAL] Raycast hit nothing");
            return false;
        }

        var pos = (Vector3)result["position"];
        var normal = (Vector3)result["normal"];
        var collider = result.ContainsKey("collider") ? result["collider"] : Variant.CreateFrom("none");
        GD.Print($"[DECAL] Raycast hit at {pos}, normal {normal}, collider: {collider}");

        var decal = new Decal();
        var tex = customTexture ?? GetDefaultScorchTexture();
        decal.TextureAlbedo = tex;
        // Cap decal depth to reduce stretching/overdraw while ensuring some penetration
        float depth = Mathf.Clamp(size * 0.35f, 0.2f, 1.0f);
        decal.Size = new Vector3(size, size, depth);

        sceneRoot.AddChild(decal);
        // Place the decal slightly INTO the surface so the box overlaps the geometry.
        // Because Decal projects along -Z, orient -Z toward -normal and move center by (depth/2 - epsilon) into the surface.
        decal.GlobalPosition = pos - normal * (depth * 0.5f - 0.02f);
        decal.LookAt(decal.GlobalPosition - normal, Vector3.Up);
        TrackDecal(decal);

        GD.Print($"[DECAL] Created decal at {decal.GlobalPosition}, size {decal.Size}, texture: {tex?.ResourcePath ?? "generated"}");
        GD.Print($"[DECAL] Decal parent: {decal.GetParent()?.Name ?? "none"}, scene root: {sceneRoot.Name}");

        if (lifetime > 0.0f)
        {
            sceneRoot.GetTree().CreateTimer(lifetime).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(decal)) 
                {
                    GD.Print($"[DECAL] Removing decal at {decal.GlobalPosition} after {lifetime} seconds");
                    decal.QueueFree();
                }
            };
        }
        return true;
    }

    private static Texture2D GetDefaultScorchTexture()
    {
        if (_cachedScorchTex != null) return _cachedScorchTex;

        // Try to load configured scorch image first
        if (ResourceLoader.Exists(DefaultScorchPath))
        {
            var loaded = GD.Load<Texture2D>(DefaultScorchPath);
            if (loaded != null)
            {
                _cachedScorchTex = loaded;
                return _cachedScorchTex;
            }
        }

        int w = 256, h = 256;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float maxR = MathF.Min(cx, cy);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = (float)x - cx;
                float dy = (float)y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR; // 0 at center, ~1 at edge
                float falloff = Mathf.Clamp(1.0f - r, 0f, 1f);
                // Square the falloff for softer edge; overall alpha tuned for subtle residue
                float alpha = Mathf.Pow(falloff, 2.2f) * 0.85f;
                img.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
        }

        _cachedScorchTex = ImageTexture.CreateFromImage(img);
        return _cachedScorchTex;
    }

    private static void TrackDecal(Decal decal)
    {
        CleanupDecalBuffer();
        _decalBuffer.Enqueue(decal);

        while (_decalBuffer.Count > MaxDecalBufferSize)
        {
            var oldest = _decalBuffer.Dequeue();
            if (GodotObject.IsInstanceValid(oldest))
            {
                GD.Print($"[DECAL] Removing oldest decal to enforce buffer of {MaxDecalBufferSize}");
                oldest.QueueFree();
            }
        }
    }

    private static void CleanupDecalBuffer()
    {
        if (_decalBuffer.Count == 0) return;

        int count = _decalBuffer.Count;
        for (int i = 0; i < count; i++)
        {
            var decal = _decalBuffer.Dequeue();
            if (GodotObject.IsInstanceValid(decal))
            {
                _decalBuffer.Enqueue(decal);
            }
        }
    }
}
