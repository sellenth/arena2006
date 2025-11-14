using Godot;

public sealed class PlayerReconciliationController
{
	public float PositionLerpRate { get; set; } = 10f;
	public float VelocityLerpRate { get; set; } = 12f;
	public float AngleLerpRate { get; set; } = 12f;
	public float SnapDistance { get; set; } = 0.01f;

	private PlayerSnapshot _pendingSnapshot;

	public bool HasSnapshot => _pendingSnapshot != null;

	public void Queue(PlayerSnapshot snapshot)
	{
		_pendingSnapshot = snapshot;
	}

	public void Clear()
	{
		_pendingSnapshot = null;
	}

	public void Apply(CharacterBody3D body, PlayerLookController lookController, float delta)
	{
		if (_pendingSnapshot == null || body == null)
			return;

		var target = _pendingSnapshot;
		var posBlend = Mathf.Clamp(delta * PositionLerpRate, 0f, 1f);
		var velBlend = Mathf.Clamp(delta * VelocityLerpRate, 0f, 1f);
		var angBlend = Mathf.Clamp(delta * AngleLerpRate, 0f, 1f);

		body.GlobalTransform = body.GlobalTransform.InterpolateWith(target.Transform, posBlend);
		body.Velocity = body.Velocity.Lerp(target.Velocity, velBlend);
		if (lookController != null)
		{
			var yaw = Mathf.LerpAngle(lookController.Yaw, target.ViewYaw, angBlend);
			var pitch = Mathf.Lerp(lookController.Pitch, target.ViewPitch, angBlend);
			lookController.SetYawPitch(yaw, pitch);
		}

		var dist = body.GlobalPosition.DistanceTo(target.Transform.Origin);
		if (dist < SnapDistance)
		{
			body.GlobalTransform = target.Transform;
			body.Velocity = target.Velocity;
			lookController?.SetYawPitch(target.ViewYaw, target.ViewPitch);
			_pendingSnapshot = null;
		}
	}
}
