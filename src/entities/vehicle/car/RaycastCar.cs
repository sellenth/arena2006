using Godot;
using Godot.Collections;

public partial class RaycastCar : RigidBody3D
{
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

	private Transform3D _spawnTransform;
	private bool _isNetworked = false;

	public override void _Ready()
	{
		TotalWheels = Wheels.Count;
		
		var spawnPoint = GetNodeOrNull<Marker3D>("/root/GameRoot/CarSpawnPoint");
		GD.Print($"RaycastCar: Spawn point: {spawnPoint.GlobalPosition}");
		_spawnTransform = spawnPoint.GlobalTransform;
		
		if (Name.ToString().StartsWith("RemotePlayer_"))
		{
			GD.Print($"RaycastCar: Skipping registration for remote player: {Name}");
			return;
		}
		
		var network = GetTree().Root.GetNodeOrNull<NetworkController>("/root/NetworkController");
		if (network != null)
		{
			GD.Print("RaycastCar: Registering with NetworkController");
			network.RegisterCar(this);
			_isNetworked = true;
		}
		else
		{
			GD.Print("RaycastCar: NetworkController not found");
			_isNetworked = false;
		}
	}

	public override void _Input(InputEvent @event)
	{
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
		GlobalTransform = _spawnTransform;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		_pendingSnapshot = null;
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
		var rawSteerInput = _inputState.Steer;
		var deltaFloat = (float)delta;
		
		if (Mathf.Abs(rawSteerInput) > 0.01f)
		{
			var targetSteer = rawSteerInput;
			var steerDelta = SteerAccelerationRate * deltaFloat;
			_smoothedSteerInput = Mathf.MoveToward(_smoothedSteerInput, targetSteer, steerDelta);
		}
		else
		{
			var steerDelta = 1.0f;
			_smoothedSteerInput = Mathf.MoveToward(_smoothedSteerInput, 0.0f, steerDelta);
		}
		
		// if (ShowDebug) 
		// 	DebugDraw.DrawArrowRay(GlobalPosition, LinearVelocity, 2.5f, 0.5f, Colors.Green);

		var id = 0;
		var grounded = false;
		foreach (var wheel in Wheels)
		{
			wheel.ApplyWheelPhysics(this);
			BasicSteeringRotation(wheel, (float)delta);

			wheel.IsBraking = _brakePressed;

			// Only update skid marks if the array is properly configured
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
}

