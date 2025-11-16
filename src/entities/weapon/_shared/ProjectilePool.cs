using Godot;
using System.Collections.Generic;

public interface IPooledProjectile
{
	void ResetToPoolState();
}

public partial class ProjectilePool : Node
{
	[Export] public PackedScene? ProjectileScene { get; set; }
	[Export] public int PrewarmCount { get; set; } = 4;

	private readonly Queue<Node> _pool = new();

	public override void _Ready()
	{
		base._Ready();
		if (ProjectileScene == null || PrewarmCount <= 0)
			return;

		for (var i = 0; i < PrewarmCount; i++)
		{
			var instance = ProjectileScene.Instantiate<Node>();
			Return(instance);
		}
	}

	public T Rent<T>(Node parent = null) where T : Node
	{
		Node instance = null;
		if (_pool.Count > 0)
		{
			instance = _pool.Dequeue();
		}

		if (instance == null)
		{
			instance = ProjectileScene != null ? ProjectileScene.Instantiate<Node>() : null;
		}

		if (instance is IPooledProjectile pooled)
		{
			pooled.ResetToPoolState();
		}

		if (parent == null)
		{
			parent = GetTree().CurrentScene;
		}

		if (instance is Node3D node3D && node3D.GetParent() == null && parent != null)
		{
			parent.AddChild(node3D);
		}

		return instance as T;
	}

	public void Return(Node node)
	{
		if (node == null)
			return;

		if (node.GetParent() != null)
		{
			node.GetParent().RemoveChild(node);
		}

		if (node is IPooledProjectile pooled)
		{
			pooled.ResetToPoolState();
		}

		_pool.Enqueue(node);
	}
}
