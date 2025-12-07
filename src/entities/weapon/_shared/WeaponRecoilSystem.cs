using Godot;

/// <summary>
/// Handles weapon recoil, spread calculations, and recoil recovery.
/// </summary>
public partial class WeaponRecoilSystem : Node
{
	private PlayerCharacter _player;
	private Vector2 _recoilOffsetRad = Vector2.Zero;

	public void Initialize(PlayerCharacter player)
	{
		_player = player;
	}

	public void ApplyRecoilKick(WeaponInstance instance, int shotIndex, float adsBlend)
	{
		if (instance?.Definition?.Recoil == null || _player == null)
			return;

		var kickDeg = ComputeRecoilKickDegrees(instance, shotIndex, adsBlend);
		if (kickDeg == Vector2.Zero)
			return;

		var kickRad = new Vector2(Mathf.DegToRad(kickDeg.X), Mathf.DegToRad(kickDeg.Y));
		_recoilOffsetRad += kickRad;
		_player.ApplyRecoil(kickRad);
	}

	public void RecoverRecoil(float dt, WeaponInstance instance)
	{
		if (_recoilOffsetRad == Vector2.Zero)
			return;

		var recoil = instance?.Definition?.Recoil;
		if (recoil == null)
			return;

		var recoveryRate = Mathf.DegToRad(recoil.RecoveryRate);
		if (recoveryRate <= 0f)
			return;

		var previous = _recoilOffsetRad;
		_recoilOffsetRad = _recoilOffsetRad.MoveToward(Vector2.Zero, recoveryRate * dt);
		var delta = _recoilOffsetRad - previous;
		if (delta != Vector2.Zero)
		{
			_player?.ApplyRecoil(delta);
		}
	}

	public void ApplyVisualSpread(Vector2 spreadRad)
	{
		if (spreadRad == Vector2.Zero)
			return;

		if (_player == null || !_player.IsAuthority())
			return;

		_recoilOffsetRad += spreadRad;
		_player.ApplyRecoil(spreadRad);
	}

	public Vector2 ComputeRecoilKickDegrees(WeaponInstance instance, int shotIndex, float adsBlend)
	{
		var def = instance?.Definition;
		var profile = def?.Recoil;
		if (profile == null)
			return Vector2.Zero;

		var baseKick = GetPatternKick(profile, shotIndex);
		var ads = def?.Ads;
		var adsScale = ads != null
			? Mathf.Lerp(ads.HipRecoilScale, ads.AdsRecoilScale, adsBlend)
			: 1f;
		var attachmentDelta = GetAttachmentRecoilDelta(instance);
		return (baseKick + new Vector2(attachmentDelta, attachmentDelta)) * adsScale;
	}

	public float ComputeSpreadDegrees(WeaponDefinition def, WeaponInstance instance, int shotIndex, float adsBlend)
	{
		if (def == null)
			return 0f;

		var profile = def.Recoil;
		var curveSpread = profile?.EvaluateSpread(shotIndex) ?? 0f;
		var ads = def.Ads;

		float spread;
		if (ads != null)
		{
			// ADS config exists: blend between hip and ADS spread, scaled by curve
			// When fully ADS'd with AdsSpreadDegrees=0, spread becomes 0 (perfect accuracy)
			var hipSpread = ads.HipSpreadDegrees + curveSpread;
			var adsSpread = ads.AdsSpreadDegrees;
			spread = Mathf.Lerp(hipSpread, adsSpread, adsBlend);
		}
		else
		{
			// No ADS config: just use curve spread
			spread = curveSpread;
		}

		spread += GetAttachmentSpreadDelta(instance);
		return Mathf.Max(spread, 0f);
	}

	/// <summary>
	/// Computes deterministic spread rotation based on shot index and weapon/owner IDs.
	/// Uses a seeded RNG to ensure consistent spread patterns across clients.
	/// </summary>
	public Vector2 ComputeSpreadRotation(float spreadDegrees, int shotIndex, WeaponDefinition def, long ownerPeerId)
	{
		if (spreadDegrees <= 0f || def == null)
			return Vector2.Zero;

		var seed = ComputeRecoilSeed(shotIndex, def.Id, ownerPeerId);
		var rng = new RandomNumberGenerator { Seed = seed };
		var yaw = Mathf.DegToRad(rng.RandfRange(-spreadDegrees, spreadDegrees));
		var pitch = Mathf.DegToRad(rng.RandfRange(-spreadDegrees, spreadDegrees));
		return new Vector2(pitch, yaw);
	}

	public Transform3D ApplySpreadToTransform(Transform3D transform, Vector2 spreadRad)
	{
		if (spreadRad == Vector2.Zero)
			return transform;

		var basis = transform.Basis;
		basis = basis.Rotated(basis.X, spreadRad.X);
		basis = basis.Rotated(Vector3.Up, spreadRad.Y);
		return new Transform3D(basis, transform.Origin);
	}

	private Vector2 GetPatternKick(RecoilProfile profile, int shotIndex)
	{
		if (profile?.Pattern != null && profile.Pattern.Count > 0)
		{
			var index = Mathf.Clamp(shotIndex - 1, 0, profile.Pattern.Count - 1);
			return profile.Pattern[index];
		}
		return profile?.Kick ?? Vector2.Zero;
	}

	private float GetAttachmentRecoilDelta(WeaponInstance instance)
	{
		if (instance == null)
			return 0f;

		var delta = 0f;
		foreach (var attachment in instance.Attachments.Values)
		{
			if (attachment != null)
			{
				delta += attachment.RecoilDelta;
			}
		}
		return delta;
	}

	private float GetAttachmentSpreadDelta(WeaponInstance instance)
	{
		if (instance == null)
			return 0f;

		var delta = 0f;
		foreach (var attachment in instance.Attachments.Values)
		{
			if (attachment != null)
			{
				delta += attachment.SpreadDelta;
			}
		}
		return delta;
	}

	/// <summary>
	/// Computes a deterministic seed for spread RNG using shot sequence, weapon type, and owner.
	/// This ensures all clients compute identical spread patterns for the same shot.
	/// </summary>
	private ulong ComputeRecoilSeed(int fireSequence, WeaponType weaponId, long ownerPeerId)
	{
		unchecked
		{
			var a = (ulong)(fireSequence * 73856093);
			var b = (ulong)((int)weaponId * 19349663);
			var c = (ulong)(ownerPeerId * 83492791);
			return a ^ b ^ c;
		}
	}
}
