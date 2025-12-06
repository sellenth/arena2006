using Godot;

/// <summary>
/// Standalone COD-style damage vignette. Drop into any scene and set the player path or rely on auto-detect.
/// </summary>
public partial class DamageUI : CanvasLayer
{
	[Export] public NodePath PlayerPath { get; set; }
	[Export] public bool AutoFindLocalPlayer { get; set; } = true;
	[Export] public Texture2D LowTexture { get; set; }
	[Export] public Texture2D MediumTexture { get; set; }
	[Export] public Texture2D CriticalTexture { get; set; }
	[Export] public float FlashDuration { get; set; } = 1.25f;
	[Export] public float MediumThreshold { get; set; } = 0.65f;
	[Export] public float CriticalThreshold { get; set; } = 0.35f;

	private PlayerCharacter _player;
	private NetworkController _network;
	private TextureRect _lowRect;
	private TextureRect _mediumRect;
	private TextureRect _criticalRect;
	private float _flashTimer = 0f;
	private float _flashIntensity = 0f;
	private int _lastHealth = -1;
	private int _lastArmor = -1;

	public PlayerCharacter CurrentPlayer => _player;

	public override void _Ready()
	{
		_network = GetNodeOrNull<NetworkController>("/root/NetworkController");
		_lowRect = GetNodeOrNull<TextureRect>("DamageLow");
		_mediumRect = GetNodeOrNull<TextureRect>("DamageMedium");
		_criticalRect = GetNodeOrNull<TextureRect>("DamageCritical");

		ApplyTextures();
		HideOverlay();
		ResolvePlayer();
	}

	public override void _Process(double delta)
	{
		ResolvePlayer();

		if (_player == null)
		{
			HideOverlay();
			return;
		}

		var currentHealth = _player.Health;
		var currentArmor = _player.Armor;

		if (_lastHealth < 0 || _lastArmor < 0)
		{
			_lastHealth = currentHealth;
			_lastArmor = currentArmor;
		}

		var lastTotal = _lastHealth + _lastArmor;
		var currentTotal = currentHealth + currentArmor;

		if (currentTotal < lastTotal)
		{
			var damageTaken = lastTotal - currentTotal;
			var maxTotal = Mathf.Max(1, _player.MaxHealth + _player.MaxArmor);
			var healthRatio = (float)currentTotal / maxTotal;
			var damageRatio = (float)damageTaken / Mathf.Max(1, _player.MaxHealth);
			TriggerOverlay(damageRatio, healthRatio);
		}

		_lastHealth = currentHealth;
		_lastArmor = currentArmor;

		if (_flashTimer > 0f)
		{
			_flashTimer = Mathf.Max(0f, _flashTimer - (float)delta);
			var t = _flashTimer / FlashDuration;
			ApplyOverlayAlpha(_flashIntensity * t);
		}
		else
		{
			ApplyOverlayAlpha(0f);
		}
	}

	public void SetPlayer(PlayerCharacter player)
	{
		_player = player;
		if (_player != null)
		{
			_lastHealth = _player.Health;
			_lastArmor = _player.Armor;
		}
	}

	public void SetTextures(Texture2D low, Texture2D medium, Texture2D critical)
	{
		LowTexture = low;
		MediumTexture = medium;
		CriticalTexture = critical;
		ApplyTextures();
	}

	private void ApplyTextures()
	{
		if (_lowRect != null && LowTexture != null)
			_lowRect.Texture = LowTexture;
		if (_mediumRect != null && MediumTexture != null)
			_mediumRect.Texture = MediumTexture;
		if (_criticalRect != null && CriticalTexture != null)
			_criticalRect.Texture = CriticalTexture;
	}

	private void ResolvePlayer()
	{
		if (_player != null)
			return;

		if (PlayerPath != null && !PlayerPath.IsEmpty)
		{
			_player = GetNodeOrNull<PlayerCharacter>(PlayerPath);
			if (_player != null)
				return;
		}

		if (AutoFindLocalPlayer && _network != null)
		{
			_player = _network.LocalPlayer;
			if (_player != null)
				return;
		}

		var root = GetTree()?.Root;
		if (root != null)
		{
			_player = root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;
		}
	}

	private void TriggerOverlay(float damageRatio, float healthRatio)
	{
		_flashTimer = FlashDuration;
		_flashIntensity = Mathf.Clamp(Mathf.Max(damageRatio * 1.4f, 1f - healthRatio), 0.25f, 0.95f);

		SetRectVisible(_lowRect, false);
		SetRectVisible(_mediumRect, false);
		SetRectVisible(_criticalRect, false);

		TextureRect target = _lowRect ?? _mediumRect ?? _criticalRect;
		if (_criticalRect != null && (healthRatio <= CriticalThreshold || damageRatio >= CriticalThreshold))
		{
			target = _criticalRect;
		}
		else if (_mediumRect != null && (healthRatio <= MediumThreshold || damageRatio >= 0.2f))
		{
			target = _mediumRect;
		}

		if (target != null)
			target.Visible = true;

		ApplyOverlayAlpha(_flashIntensity);
	}

	private void ApplyOverlayAlpha(float alpha)
	{
		if (alpha <= 0f)
		{
			HideOverlay();
			return;
		}

		Visible = true;
		SetTextureAlpha(_lowRect, _lowRect?.Visible == true ? alpha : 0f);
		SetTextureAlpha(_mediumRect, _mediumRect?.Visible == true ? alpha : 0f);
		SetTextureAlpha(_criticalRect, _criticalRect?.Visible == true ? alpha : 0f);
	}

	private void HideOverlay()
	{
		Visible = false;
		_flashTimer = 0f;
		_flashIntensity = 0f;
		SetRectVisible(_lowRect, false);
		SetRectVisible(_mediumRect, false);
		SetRectVisible(_criticalRect, false);
	}

	private void SetRectVisible(TextureRect rect, bool visible)
	{
		if (rect == null)
			return;

		rect.Visible = visible;
		if (!visible)
			SetTextureAlpha(rect, 0f);
	}

	private void SetTextureAlpha(TextureRect rect, float alpha)
	{
		if (rect == null)
			return;

		var modulate = rect.Modulate;
		modulate.A = Mathf.Clamp(alpha, 0f, 1f);
		rect.Modulate = modulate;
	}
}
