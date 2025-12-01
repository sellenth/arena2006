using Godot;
using System.Collections.Generic;

public partial class WeaponView : Node3D
{
	[Export] public NodePath InventoryPath { get; set; } = "../../../WeaponInventory";

	private WeaponInventory _inventory;
	private Node3D _currentView;
	private readonly Dictionary<string, Node3D> _viewsByKey = new();

	public override void _Ready()
	{
		_inventory = GetNodeOrNull<WeaponInventory>(InventoryPath);
		CacheChildViews();
		HideAllViews();

		if (_inventory == null)
		{
			GD.PushWarning($"{Name}: WeaponInventory not found at '{InventoryPath}'.");
			return;
		}

		_inventory.EquippedChanged -= OnEquippedChanged;
		_inventory.EquippedChanged += OnEquippedChanged;

		RefreshView(_inventory.Equipped?.Definition);
	}

	private void OnEquippedChanged(WeaponType type)
	{
		var definition = _inventory?.Get(type)?.Definition ?? _inventory?.Equipped?.Definition;
		RefreshView(definition);
	}

	private void RefreshView(WeaponDefinition definition)
	{
		HideAllViews();
		if (definition?.ViewScene == null)
			return;

		var sceneKey = definition.ViewScene.ResourcePath;
		if (string.IsNullOrEmpty(sceneKey))
			sceneKey = definition.Id.ToString();

		if (!_viewsByKey.TryGetValue(sceneKey, out var view) || view == null || !IsInstanceValid(view))
		{
			CacheChildViews();
			_viewsByKey.TryGetValue(sceneKey, out view);
		}

		_currentView = view;
		if (_currentView != null)
		{
			_currentView.Visible = true;
			ApplyZClipScale(_currentView);
		}
		else
		{
			GD.PushWarning($"{Name}: No child view found matching '{sceneKey}'.");
		}
	}

	private void ApplyZClipScale(Node node)
	{
		if (node is MeshInstance3D meshInstance)
		{
			for (int i = 0; i < meshInstance.GetSurfaceOverrideMaterialCount(); i++)
			{
				var material = meshInstance.GetSurfaceOverrideMaterial(i);
				if (material != null)
				{
					material.Set("use_z_clip_scale", true);
					material.Set("z_clip_scale", 0.1f);
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			ApplyZClipScale(child);
		}
	}

	private void CacheChildViews()
	{
		_viewsByKey.Clear();
		foreach (var child in GetChildren())
		{
			if (child is Node3D node)
			{
				var key = !string.IsNullOrEmpty(node.SceneFilePath) ? node.SceneFilePath : node.Name.ToString();
				GD.Print(key);
				if (!string.IsNullOrEmpty(key))
				{
					_viewsByKey[key] = node;
				}
			}
		}
	}

	private void HideAllViews()
	{
		foreach (var child in GetChildren())
		{
			if (child is Node3D node)
			{
				node.Visible = false;
			}
		}
		_currentView = null;
	}
}
