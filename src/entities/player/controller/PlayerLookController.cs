using Godot;

public sealed class PlayerLookController
{
	public float MouseSensitivity { get; set; } = 0.002f;
	public float MinPitch { get; set; } = -1.2f;
	public float MaxPitch { get; set; } = 1.2f;

        public float BaseFov { get; set; } = 75f;
        public float FovSpeedScale { get; set; } = 0.8f;
        public float MaxFovBoost { get; set; } = 12f;
        public float FovLerpRate { get; set; } = 12f;

        public float SlideFovBoost { get; set; } = 6f;
        public float SlideTiltDegrees { get; set; } = 6f;
        public float SlideCameraDrop { get; set; } = 0.1f;

        public float WallRunFovBoost { get; set; } = 10f;
        public float WallRunTiltDegrees { get; set; } = 14f;
        public float WallRunCameraShift { get; set; } = 0.08f;

        public float StateEffectResponse { get; set; } = 10f;

	public float TiltAngleDegrees { get; set; } = 6f;
	public float TiltResponse { get; set; } = 8f;

	public float HeadBobAmplitude { get; set; } = 0.01f;
	public float HeadBobFrequency { get; set; } = 1f;
	public float HeadBobSpeedScale { get; set; } = 0.25f;
	public float HeadBobRecoveryRate { get; set; } = 8f;

	public float Yaw { get; private set; }
	public float Pitch { get; private set; }

        private Node3D _head;
        private Camera3D _camera;
        private Vector2 _pendingLookDelta = Vector2.Zero;
        private Vector3 _cameraBaseLocalPos = Vector3.Zero;
        private Vector3 _cameraBobOffset = Vector3.Zero;
        private Vector3 _stateCameraOffset = Vector3.Zero;
        private float _cameraTiltRad = 0f;
        private float _stateTiltRad = 0f;
        private float _headBobTime = 0f;
        private float _baseFov = 75f;
        private float _currentFov;
        private float _targetFov;

	public void Initialize(Node3D head, Camera3D camera, float initialYaw, float initialPitch)
	{
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
		Yaw -= delta.X * MouseSensitivity;
		Pitch = Mathf.Clamp(Pitch - delta.Y * MouseSensitivity, MinPitch, MaxPitch);
		ApplyViewToNodes();
		return true;
	}

	public void ClearQueuedLook()
	{
		_pendingLookDelta = Vector2.Zero;
	}

        public void Update(
                float delta,
                Vector3 velocity,
                bool grounded,
                PlayerMovementStateKind movementState = PlayerMovementStateKind.Airborne,
                Vector3 wallNormal = default)
        {
                UpdateCameraEffects(delta, velocity, grounded, movementState, wallNormal);
                ApplyViewToNodes();
        }

	public Vector3 GetViewDirection(Node3D fallback)
	{
		if (_camera != null)
			return -_camera.GlobalTransform.Basis.Z;

		if (_head != null)
			return -_head.GlobalTransform.Basis.Z;

		return fallback != null ? -fallback.GlobalTransform.Basis.Z : Vector3.Forward;
	}

        private void UpdateCameraEffects(
                float delta,
                Vector3 velocity,
                bool grounded,
                PlayerMovementStateKind movementState,
                Vector3 wallNormal)
        {
                if (_camera == null)
                        return;

                var planarVelocity = new Vector3(velocity.X, 0f, velocity.Z);
                var planarSpeed = planarVelocity.Length();
                var stateOffset = Vector3.Zero;
                var movementTilt = 0f;
                var targetFov = BaseFov + Mathf.Clamp(planarSpeed * FovSpeedScale, 0f, MaxFovBoost);

                switch (movementState)
                {
                        case PlayerMovementStateKind.Sliding:
                                targetFov += SlideFovBoost;
                                stateOffset = Vector3.Down * SlideCameraDrop;
                                movementTilt = Mathf.DegToRad(SlideTiltDegrees) * Mathf.Clamp(planarSpeed / 12f, 0f, 1f);
                                break;
                        case PlayerMovementStateKind.WallRunning when !wallNormal.IsZeroApprox():
                                targetFov += WallRunFovBoost;
                                stateOffset = -wallNormal.Normalized() * WallRunCameraShift;
                                var wallSide = wallNormal.Cross(Vector3.Up).Normalized();
                                var alongWall = planarVelocity.Normalized();
                                var tiltDir = wallSide.Dot(alongWall);
                                movementTilt = Mathf.DegToRad(WallRunTiltDegrees) * Mathf.Clamp(tiltDir, -1f, 1f);
                                break;
                }
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
                var tiltLerp = 1f - Mathf.Exp(-TiltResponse * delta);
                var speedScale = Mathf.Clamp(planarSpeed / 60f, 0f, 1f);
                _cameraTiltRad = Mathf.Lerp(_cameraTiltRad, tiltTarget * speedScale, tiltLerp);

                var stateLerp = 1f - Mathf.Exp(-StateEffectResponse * delta);
                _stateTiltRad = Mathf.Lerp(_stateTiltRad, movementTilt, stateLerp);
                _stateCameraOffset = _stateCameraOffset.Lerp(stateOffset, stateLerp);

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
		if (_head != null)
		{
			var headRot = _head.Rotation;
			headRot.Y = Yaw;
			_head.Rotation = headRot;
		}

                if (_camera != null)
                {
                        var camRot = _camera.Rotation;
                        camRot.X = Pitch;
                        camRot.Z = _cameraTiltRad + _stateTiltRad;
                        _camera.Rotation = camRot;
                        _camera.Position = _cameraBaseLocalPos + _cameraBobOffset + _stateCameraOffset;
                        _camera.Fov = _currentFov;
                }
        }
}
