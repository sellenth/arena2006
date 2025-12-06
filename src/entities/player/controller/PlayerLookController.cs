using Godot;

public sealed class PlayerLookController
{
	public float MouseSensitivity { get; set; } = 0.002f;
	public float MinPitch { get; set; } = -1.5f;
	public float MaxPitch { get; set; } = 1.5f;

	public float BaseFov { get; set; } = 75f;
	public float FovSpeedScale { get; set; } = 0.8f;
	public float MaxFovBoost { get; set; } = 12f;
	public float FovLerpRate { get; set; } = 12f;

	public float TiltAngleDegrees { get; set; } = 6f;
	public float TiltResponse { get; set; } = 8f;

	public float HeadBobAmplitude { get; set; } = 0.01f;
	public float HeadBobFrequency { get; set; } = 1f;
	public float HeadBobSpeedScale { get; set; } = 0.25f;
	public float HeadBobRecoveryRate { get; set; } = 8f;

	public float Yaw { get; private set; }
	public float Pitch { get; private set; }
	public float AdsBlend { get; private set; } = 0f;

	private Node3D _root;
	private Node3D _head;
	private Camera3D _camera;
	private Vector2 _pendingLookDelta = Vector2.Zero;
	private Vector3 _cameraBaseLocalPos = Vector3.Zero;
	private Vector3 _cameraBobOffset = Vector3.Zero;
	private float _cameraTiltRad = 0f;
	private float _headBobTime = 0f;
	private float _baseFov = 75f;
	private float _currentFov;
	private float _targetFov;
	private float _adsTargetFov;
	private float _adsSensitivityScale = 1f;

	public void Initialize(Node3D root, Node3D head, Camera3D camera, float initialYaw, float initialPitch)
	{
		_root = root;
		_head = head;
		_camera = camera;
		Yaw = initialYaw;
		Pitch = Mathf.Clamp(initialPitch, MinPitch, MaxPitch);
		if (_camera != null)
		{
			_cameraBaseLocalPos = _camera.Position;
			_baseFov = _camera.Fov;
			if (BaseFov <= 0f)
				BaseFov = _baseFov;
			_currentFov = _camera.Fov;
			_targetFov = _camera.Fov;
		}
		else
		{
			_baseFov = BaseFov;
			_currentFov = BaseFov;
			_targetFov = BaseFov;
		}
		ApplyViewToNodes();
	}

	public void SetYawPitch(float yaw, float pitch)
	{
		Yaw = yaw;
		Pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
		ApplyViewToNodes();
	}

	public void QueueLookDelta(Vector2 delta)
	{
		_pendingLookDelta += delta;
	}

	public bool ApplyQueuedLook()
	{
		if (_pendingLookDelta == Vector2.Zero)
			return false;

		var delta = _pendingLookDelta;
		_pendingLookDelta = Vector2.Zero;
		var sensScale = Mathf.Lerp(1f, _adsSensitivityScale, AdsBlend);
		Yaw -= delta.X * MouseSensitivity * sensScale;
		Pitch = Mathf.Clamp(Pitch - delta.Y * MouseSensitivity * sensScale, MinPitch, MaxPitch);
		ApplyViewToNodes();
		return true;
	}

	public void ClearQueuedLook()
	{
		_pendingLookDelta = Vector2.Zero;
	}

	public void Update(float delta, Vector3 velocity, bool grounded)
	{
		UpdateCameraEffects(delta, velocity, grounded);
		ApplyViewToNodes();
	}

	public void SetAdsBlend(float blend, float targetFov, float sensitivityScale)
	{
		AdsBlend = Mathf.Clamp(blend, 0f, 1f);
		_adsTargetFov = targetFov > 0f ? targetFov : BaseFov;
		_adsSensitivityScale = Mathf.Max(0.01f, sensitivityScale);
	}

	public Vector3 GetViewDirection(Node3D fallback)
	{
		if (_camera != null)
			return -_camera.GlobalTransform.Basis.Z;

		if (_head != null)
			return -_head.GlobalTransform.Basis.Z;

		return fallback != null ? -fallback.GlobalTransform.Basis.Z : Vector3.Forward;
	}

	private void UpdateCameraEffects(float delta, Vector3 velocity, bool grounded)
	{
		if (_camera == null)
			return;

		var planarVelocity = new Vector3(velocity.X, 0f, velocity.Z);
		var planarSpeed = planarVelocity.Length();
		var adsFov = _adsTargetFov > 0f ? _adsTargetFov : BaseFov;
		var baseFov = Mathf.Lerp(BaseFov, adsFov, AdsBlend);
		var speedBoost = Mathf.Clamp(planarSpeed * FovSpeedScale, 0f, MaxFovBoost) * (1f - AdsBlend);
		var targetFov = baseFov + speedBoost;
		var lerp = 1f - Mathf.Exp(-FovLerpRate * delta);
		_currentFov = Mathf.Lerp(_currentFov, targetFov, lerp);
		_targetFov = targetFov;
		_camera.Fov = _currentFov;

		var referenceRight = _head?.GlobalTransform.Basis.X ?? Vector3.Right;
		var strafe = 0f;

		if (!referenceRight.IsZeroApprox())
			strafe = planarVelocity.Dot(referenceRight.Normalized());

		var normalizedStrafe = Mathf.Clamp(-strafe * 0.1f, -1f, 1f);
		var tiltTarget = Mathf.DegToRad(normalizedStrafe * TiltAngleDegrees);
		_cameraTiltRad = Mathf.Lerp(_cameraTiltRad, tiltTarget, 1f - Mathf.Exp(-TiltResponse * delta)) * (planarSpeed / 60f);

		//UpdateHeadBob(delta, planarSpeed, grounded);
	}

	private void UpdateHeadBob(float delta, float planarSpeed, bool grounded)
	{
		if (grounded && planarSpeed > 0.1f)
		{
			_headBobTime += delta * (HeadBobFrequency + planarSpeed * HeadBobSpeedScale);
			var phase = _headBobTime * Mathf.Tau;
			var x = Mathf.Sin(phase) * HeadBobAmplitude;
			var y = Mathf.Cos(phase * 0.5f) * HeadBobAmplitude * 0.5f;
			_cameraBobOffset = new Vector3(x, y, 0f);
		}
		else
		{
			var recover = 1f - Mathf.Exp(-HeadBobRecoveryRate * delta);
			_headBobTime = Mathf.Lerp(_headBobTime, 0f, recover);
			_cameraBobOffset = _cameraBobOffset.Lerp(Vector3.Zero, recover);
		}
	}

	private void ApplyViewToNodes()
	{
		if (_root != null)
		{
			var rootRot = _root.Rotation;
			rootRot.Y = Yaw;
			_root.Rotation = rootRot;
		}

		if (_camera != null)
		{
			var camRot = _camera.Rotation;
			camRot.X = Pitch;
			camRot.Z = _cameraTiltRad;
			_camera.Rotation = camRot;
			_camera.Position = _cameraBaseLocalPos + _cameraBobOffset;
			_camera.Fov = _currentFov;
		}
	}
}
