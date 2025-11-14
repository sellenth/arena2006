using Godot;
using Godot.Collections;

public partial class RaycastCar : RigidBody3D, IReplicatedEntity
{
	public enum NetworkRegistrationMode
	{
		None,
		LocalPlayer,
		AuthoritativeVehicle
	}

	[Export] public Array<RaycastWheel> Wheels { get; set; }
	[Export] public float Acceleration { get; set; } = 600.0f;
	[Export] public float MaxSpeed { get; set; } = 20.0f;
	[Export] public Curve AccelCurve { get; set; }
	[Export] public float TireTurnSpeed { get; set; } = 2.0f;
	[Export] public float TireMaxTurnDegrees { get; set; } = 25;
	[Export] public float SteerAccelerationRate { get; set; } = 3.0f;
	[Export] public float SteerDecelerationRate { get; set; } = 5.0f;

	[Export] public Array<GpuParticles3D> SkidMarks { get; set; }
	[Export] public bool ShowDebug { get; set; } = false;
	[Export] public NetworkRegistrationMode RegistrationMode { get; set; } = NetworkRegistrationMode.LocalPlayer;
	[Export] public bool AutoRespawnOnReady { get; set; } = true;
	[Export] public NodePath CameraPath { get; set; } = "CameraPivot/Camera";
	
	[Export] public int NetworkId { get; set; } = 0;

	public int TotalWheels { get; private set; }

	public float MotorInput { get; set; } = 0.0f;
	public float SteerInput => _smoothedSteerInput;
	public bool HandBreak { get; set; } = false;
	public bool IsSlipping { get; set; } = false;

	private CarInputState _inputState = new CarInputState();
	private float _smoothedSteerInput = 0.0f;
	private bool _brakePressed = false;
	private CarSnapshot _pendingSnapshot;

	private const float SnapBlend = 0.35f;
	private const float SnapPosEps = 0.05f;
	private const float SnapVelEps = 0.1f;
	private static readonly float SnapAngEps = Mathf.DegToRad(2.0f);

	private bool _isNetworked = false;
	private bool _hasLoggedSpawn = false;
	private Vector3 _initialSpawnPosition = Vector3.Zero;
	private Camera3D _camera;
	private bool _simulateLocally = true;
	private bool _cameraActive = false;
	
	private ReplicatedTransform3D _transformProperty;
	private ReplicatedVector3 _linearVelocityProperty;
	private ReplicatedVector3 _angularVelocityProperty;

	public override void _Ready()
	{
		TotalWheels = Wheels.Count;

		_camera = GetNodeOrNull<Camera3D>(CameraPath);
		
		InitializeReplication();

		var network = GetTree().Root.GetNodeOrNull<NetworkController>("/root/NetworkController");

		switch (RegistrationMode)
		{
			case NetworkRegistrationMode.LocalPlayer:
				if (network != null && network.IsClient)
				{
					RegisterAsLocalVehicle();
					network.RegisterLocalPlayerCar(this);
					_isNetworked = true;
				}
				break;
			case NetworkRegistrationMode.AuthoritativeVehicle:
				if (network != null && network.IsServer)
				{
					RegisterAsAuthority();
					network.RegisterAuthoritativeVehicle(this);
					_isNetworked = true;
				}
				break;
			default:
				_simulateLocally = false;
				break;
		}

		if (AutoRespawnOnReady && _simulateLocally)
			RespawnAtManagedPoint();

		ApplySimulationMode();
	}
	
	private void InitializeReplication()
	{
		_transformProperty = new ReplicatedTransform3D(
			"Transform",
			() => GlobalTransform,
			(value) => ApplyTransformFromReplication(value),
			ReplicationMode.Always,
			positionThreshold: SnapPosEps,
			rotationThreshold: SnapAngEps
		);
		
		_linearVelocityProperty = new ReplicatedVector3(
			"LinearVelocity",
			() => LinearVelocity,
			(value) => LinearVelocity = value,
			ReplicationMode.Always
		);
		
		_angularVelocityProperty = new ReplicatedVector3(
			"AngularVelocity",
			() => AngularVelocity,
			(value) => AngularVelocity = value,
			ReplicationMode.Always
		);
	}
	
	private void RegisterAsAuthority()
	{
		NetworkId = EntityReplicationRegistry.Instance?.RegisterEntity(this, this) ?? NetworkId;
		GD.Print($"RaycastCar: Registered as authority with NetworkId {NetworkId}");
	}
	
	private void RegisterAsLocalVehicle()
	{
		var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
		if (remoteManager != null)
		{
			remoteManager.RegisterRemoteEntity(NetworkId, this);
			GD.Print($"RaycastCar: Registered as remote with NetworkId {NetworkId}");
		}
		else
		{
			GD.PushWarning("RaycastCar: RemoteEntityManager not found in scene!");
		}
	}
	
	private void ApplyTransformFromReplication(Transform3D value)
	{
		if (_simulateLocally && _pendingSnapshot != null)
		{
			return;
		}
		
		GlobalTransform = value;
	}
	
	public void WriteSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Write(buffer);
		_linearVelocityProperty.Write(buffer);
		_angularVelocityProperty.Write(buffer);
	}
	
	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Read(buffer);
		_linearVelocityProperty.Read(buffer);
		_angularVelocityProperty.Read(buffer);
	}
	
	public int GetSnapshotSizeBytes()
	{
		return _transformProperty.GetSizeBytes() 
			 + _linearVelocityProperty.GetSizeBytes() 
			 + _angularVelocityProperty.GetSizeBytes();
	}

	public override void _Input(InputEvent @event)
	{
		if (!_simulateLocally)
			return;

		if (!_isNetworked && @event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.R)
			{
				Respawn();
			}
		}
	}

	public void Respawn()
	{
		if (!_simulateLocally)
			return;

		RespawnAtManagedPoint();
		_pendingSnapshot = null;
	}

	private void RespawnAtManagedPoint()
	{
		var previousPosition = GlobalTransform.Origin;
		var manager = RespawnManager.Instance;
		var success = manager.RespawnEntityAtBestPoint(this, this);
		if (!success)
		{
			var fallback = RespawnManager.RespawnRequest.Create(GlobalTransform);
			manager.RespawnEntity(this, fallback);
		}

		var currentPosition = GlobalTransform.Origin;

		var shouldLog = !Name.ToString().StartsWith("ServerCar_") && !Name.ToString().StartsWith("RemotePlayer_");

		if (!shouldLog)
			return;

		if (!_hasLoggedSpawn)
		{
			_hasLoggedSpawn = true;
			_initialSpawnPosition = currentPosition;
			GD.Print($"TEST_EVENT: CAR_INITIAL_SPAWN name={Name} pos={FormatVector(currentPosition)}");
		}
		else
		{
			var distance = previousPosition.DistanceTo(currentPosition);
			GD.Print($"TEST_EVENT: CAR_RESPAWNED name={Name} prev={FormatVector(previousPosition)} new={FormatVector(currentPosition)} distance={distance:F2}");
		}
	}

	private static string FormatVector(Vector3 value)
	{
		return $"{value.X:F2},{value.Y:F2},{value.Z:F2}";
	}

	public Vector3 GetPointVelocity(Vector3 point)
	{
		return LinearVelocity + AngularVelocity.Cross(point - GlobalPosition);
	}

	public void SetInputState(CarInputState state)
	{
		_inputState.CopyFrom(state);
		MotorInput = Mathf.Clamp(_inputState.Throttle, -1.0f, 1.0f);
		HandBreak = _inputState.Handbrake;
		_brakePressed = _inputState.Brake;
		if (_inputState.Handbrake)
			IsSlipping = true;
		if (_inputState.Respawn)
			Respawn();
	}

	public CarSnapshot CaptureSnapshot(int tick)
	{
		var snapshot = new CarSnapshot
		{
			Tick = tick,
			Transform = GlobalTransform,
			LinearVelocity = LinearVelocity,
			AngularVelocity = AngularVelocity
		};
		return snapshot;
	}

	public void QueueSnapshot(CarSnapshot snapshot)
	{
		_pendingSnapshot = snapshot;
	}

	private void BasicSteeringRotation(RaycastWheel wheel, float delta)
	{
		if (!wheel.IsSteer) return;

		var turnInput = _smoothedSteerInput * TireTurnSpeed;
		if (turnInput != 0)
		{
			wheel.Rotation = new Vector3(wheel.Rotation.X, 
				Mathf.Clamp(wheel.Rotation.Y + turnInput * delta,
					Mathf.DegToRad(-TireMaxTurnDegrees), Mathf.DegToRad(TireMaxTurnDegrees)),
				wheel.Rotation.Z);
		}
		else
		{
			wheel.Rotation = new Vector3(wheel.Rotation.X,
				Mathf.MoveToward(wheel.Rotation.Y, 0, TireTurnSpeed * delta),
				wheel.Rotation.Z);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_simulateLocally)
			return;

		var raw = Mathf.Clamp(_inputState.Steer, -1f, 1f);
		var df = (float)delta;

		float speed = LinearVelocity.Length();
		float speed01 = Mathf.Clamp(speed / MaxSpeed, 0f, 1f);

		float maxSteerNorm = Mathf.Lerp(1f, 0.25f, speed01 * 0.85f);

		float accel = SteerAccelerationRate * df;
		float decel = SteerDecelerationRate * df;

		bool reversing = Mathf.Sign(raw) != Mathf.Sign(_smoothedSteerInput) && Mathf.Abs(_smoothedSteerInput) > 0.1f;
		float rate = reversing ? decel * 1.6f : accel;

		_smoothedSteerInput = Mathf.MoveToward(_smoothedSteerInput, raw * maxSteerNorm, rate);

		if (Mathf.Abs(raw) < 0.01f)
		{
			_smoothedSteerInput = Mathf.MoveToward(_smoothedSteerInput, 0f, decel);
		}

		var id = 0;
		var grounded = false;
		foreach (var wheel in Wheels)
		{
			wheel.ApplyWheelPhysics(this);
			BasicSteeringRotation(wheel, (float)delta);

			wheel.IsBraking = _brakePressed;

			if (SkidMarks != null && id < SkidMarks.Count)
			{
				SkidMarks[id].GlobalPosition = wheel.GetCollisionPoint() + Vector3.Up * 0.01f;
				SkidMarks[id].LookAt(SkidMarks[id].GlobalPosition + GlobalBasis.Z);

				if (!HandBreak && wheel.GripFactor < 0.2f)
				{
					IsSlipping = false;
					SkidMarks[id].Emitting = false;
				}

				if (HandBreak && !SkidMarks[id].Emitting)
					SkidMarks[id].Emitting = true;
			}

			if (wheel.IsColliding())
				grounded = true;

			id++;
		}

		if (grounded)
		{
			CenterOfMass = Vector3.Zero;
		}
		else
		{
			CenterOfMassMode = CenterOfMassModeEnum.Custom;
			CenterOfMass = Vector3.Down * 0.5f;
		}
	}

	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		if (_pendingSnapshot == null) return;

		var target = _pendingSnapshot;
		var currentTransform = state.Transform;
		var blendedOrigin = currentTransform.Origin.Lerp(target.Transform.Origin, SnapBlend);
		var currentQuat = currentTransform.Basis.GetRotationQuaternion();
		var targetQuat = target.Transform.Basis.GetRotationQuaternion();
		var blendedQuat = currentQuat.Slerp(targetQuat, SnapBlend);
		state.Transform = new Transform3D(new Basis(blendedQuat), blendedOrigin);

		var blendedLinear = state.LinearVelocity.Lerp(target.LinearVelocity, SnapBlend);
		state.LinearVelocity = blendedLinear;
		var blendedAngular = state.AngularVelocity.Lerp(target.AngularVelocity, SnapBlend);
		state.AngularVelocity = blendedAngular;

		var posError = blendedOrigin.DistanceTo(target.Transform.Origin);
		var angError = blendedQuat.AngleTo(targetQuat);
		var linError = blendedLinear.DistanceTo(target.LinearVelocity);
		var angVelError = blendedAngular.DistanceTo(target.AngularVelocity);
		if (posError <= SnapPosEps && angError <= SnapAngEps &&
			linError <= SnapVelEps && angVelError <= SnapVelEps)
		{
			_pendingSnapshot = null;
		}
	}

	public new Vector3 GetGravity()
	{
		var gravityMagnitude = (float)PhysicsServer3D.AreaGetParam(GetWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity);
		var gravityVector = (Vector3)PhysicsServer3D.AreaGetParam(GetWorld3D().Space, PhysicsServer3D.AreaParameter.GravityVector);
		return gravityMagnitude * gravityVector;
	}

	public void SetSimulationEnabled(bool enabled)
	{
		_simulateLocally = enabled;
		ApplySimulationMode();
	}

	private void ApplySimulationMode()
	{
		if (_simulateLocally)
		{
			Freeze = false;
			FreezeMode = FreezeModeEnum.Static;
		}
		else
		{
			FreezeMode = FreezeModeEnum.Kinematic;
			Freeze = true;
		}
	}

	public void SetCameraActive(bool active)
	{
		_cameraActive = active;
		if (_camera != null)
			_camera.Current = active;
	}

	public void ApplyRemoteSnapshot(VehicleStateSnapshot snapshot, float blend = 0.35f)
	{
		if (snapshot == null)
			return;

		if (_simulateLocally)
		{
			QueueSnapshot(snapshot.ToCarSnapshot());
			return;
		}

		var appliedBlend = Mathf.Clamp(blend, 0.01f, 1.0f);
		var current = GlobalTransform;
		var blendedOrigin = current.Origin.Lerp(snapshot.Transform.Origin, appliedBlend);
		var currentQuat = current.Basis.GetRotationQuaternion();
		var targetQuat = snapshot.Transform.Basis.GetRotationQuaternion();
		var blendedQuat = currentQuat.Slerp(targetQuat, appliedBlend);
		GlobalTransform = new Transform3D(new Basis(blendedQuat), blendedOrigin);
		LinearVelocity = LinearVelocity.Lerp(snapshot.LinearVelocity, appliedBlend);
		AngularVelocity = AngularVelocity.Lerp(snapshot.AngularVelocity, appliedBlend);
	}

	public void ConfigureForLocalDriver(bool enable)
	{
		SetSimulationEnabled(enable);
		if (!enable)
		{
			SetCameraActive(false);
		}
	}
}

