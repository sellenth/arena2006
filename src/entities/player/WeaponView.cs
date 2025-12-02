using Godot;
using System.Collections.Generic;

public partial class WeaponView : Node3D
{
	[Export] public NodePath InventoryPath { get; set; } = "../../../WeaponInventory";

	private WeaponInventory _inventory;
	private Node3D _currentView;
	private readonly Dictionary<string, Node3D> _viewsByKey = new();
	private float _adsBlend = 0f;
	private AdsConfig _adsConfig;

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

	public void SetAdsVisual(float blend, AdsConfig config)
	{
		_adsBlend = Mathf.Clamp(blend, 0f, 1f);
		_adsConfig = config;
		ApplyAdsPose();
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
			ApplyAdsPose();
		}
		else
		{
			GD.PushWarning($"{Name}: No child view found matching '{sceneKey}'.");
		}
	}

	private void ApplyZClipScale(Node node)
	{
		// Only apply Z-clip scaling if we have authority (local player)
		if (_inventory?.GetParent() is PlayerCharacter player && !player.HasAuthority())
		{
			return;
		}

		if (node is MeshInstance3D meshInstance)
		{
			for (int i = 0; i < meshInstance.GetSurfaceOverrideMaterialCount(); i++)
			{
				var material = meshInstance.GetSurfaceOverrideMaterial(i);
				if (material != null)
				{
					// Duplicate material to ensure we don't modify the shared resource
					var uniqueMaterial = (Material)material.Duplicate();
					meshInstance.SetSurfaceOverrideMaterial(i, uniqueMaterial);

					uniqueMaterial.Set("use_z_clip_scale", true);
					uniqueMaterial.Set("z_clip_scale", 0.1f);

					uniqueMaterial.Set("use_fov_override", true);
					uniqueMaterial.Set("fov_override", 58.6f);
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			ApplyZClipScale(child);
		}
	}

	private void ApplyAdsPose()
	{
		// Placeholder: when Hip/Ads anchors are added, lerp the viewmodel transform using _adsBlend.
		if (_currentView == null)
			return;

		ApplyScopeOverlay();
	}

	private void ApplyScopeOverlay()
	{
		var overlay = GetScopeOverlay(_currentView);
		if (overlay == null)
			return;

		var texRect = overlay.GetNodeOrNull<TextureRect>("TextureRect")
			?? overlay.FindChild("TextureRect", recursive: true, owned: false) as TextureRect;

		if (_adsConfig == null || _adsConfig.Mode != AdsMode.Scope || _adsBlend <= 0.001f)
		{
			overlay.Visible = false;
			SetMeshesVisible(_currentView, true);
			return;
		}

		if (_adsConfig.ScopeOverlay != null && texRect != null)
		{
			texRect.Texture = _adsConfig.ScopeOverlay;
		}

		var alpha = Mathf.Clamp(_adsBlend * _adsConfig.ScopeOverlayOpacity, 0f, 1f);
		var mod = overlay.Modulate;
		mod.A = alpha;
		overlay.Modulate = mod;
		overlay.Visible = alpha > 0.001f;

		// Hide the viewmodel mesh when scoped to avoid seeing the weapon in the scope.
		SetMeshesVisible(_currentView, alpha <= 0.99f);
	}

	private Control GetScopeOverlay(Node view)
	{
		if (view == null)
			return null;
		var overlay = view.GetNodeOrNull<Control>("ScopeOverlay");
		if (overlay != null)
			return overlay;
		return view.FindChild("ScopeOverlay", recursive: true, owned: false) as Control;
	}

	private void SetMeshesVisible(Node node, bool visible)
	{
		if (node == null)
			return;

		if (node is MeshInstance3D mesh)
		{
			mesh.Visible = visible;
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
				SetMeshesVisible(childNode, visible);
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
