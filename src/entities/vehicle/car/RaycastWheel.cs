using Godot;

public partial class RaycastWheel : RayCast3D
{
	[ExportGroup("Wheel properties")]
	[Export] public float SpringStrength { get; set; } = 5000.0f;
	[Export] public float SpringDamping { get; set; } = 120.0f;
	[Export] public float RestDist { get; set; } = 0.1f;
	[Export] public float OverExtend { get; set; } = 0.05f;
	[Export] public float WheelRadius { get; set; } = 0.33f;
	[Export] public float ZTraction { get; set; } = 0.05f;
	[Export] public float ZBrakeTraction { get; set; } = 0.25f;
	[Export] public bool IsBackWheel { get; set; } = false;

	[ExportCategory("Motor")]
	[Export] public bool IsMotor { get; set; } = false;
	[Export] public bool IsSteer { get; set; } = false;
	[Export] public Curve GripCurve { get; set; }

	[ExportCategory("Debug")]
	[Export] public bool ShowDebug { get; set; } = false;

	private Node3D _wheel;

	public float EngineForce { get; set; } = 0.0f;
	public float GripFactor { get; set; } = 0.0f;
	public bool IsBraking { get; set; } = false;
	public float CurrentOffset { get; private set; } = 0.0f;

	public override void _Ready()
	{
		_wheel = EnsureWheelNode();
		TargetPosition = new Vector3(TargetPosition.X, -(RestDist + WheelRadius + OverExtend), TargetPosition.Z);
	}

	private Node3D EnsureWheelNode()
	{
		if (GetChildCount() == 0)
			return CreateWheelPlaceholder("WheelPlaceholder");

		var firstChild = GetChild(0);
		if (firstChild is Node3D node3D)
			return node3D;

		GD.PushWarning("RaycastWheel expects a Node3D visual child; creating placeholder so gameplay can continue.");
		return CreateWheelPlaceholder("WheelPlaceholderInvalid");
	}

	private Node3D CreateWheelPlaceholder(string placeholderName)
	{
		var placeholder = new Node3D { Name = placeholderName };
		AddChild(placeholder, false);
		return placeholder;
	}

	public void ApplyWheelPhysics(RaycastCar car)
	{
		ForceRaycastUpdate();
		TargetPosition = new Vector3(TargetPosition.X, -(RestDist + WheelRadius + OverExtend), TargetPosition.Z);

		var forwardDir = -GlobalBasis.Z;
		var vel = forwardDir.Dot(car.LinearVelocity);
		_wheel.RotateX((-vel * (float)GetPhysicsProcessDeltaTime()) / WheelRadius);

		if (!IsColliding())
		{
			CurrentOffset = 0.0f;
			return;
		}

		var contact = GetCollisionPoint();
		var springLen = Mathf.Max(0.0f, GlobalPosition.DistanceTo(contact) - WheelRadius);
		CurrentOffset = RestDist - springLen;

		_wheel.Position = new Vector3(_wheel.Position.X, 
			Mathf.MoveToward(_wheel.Position.Y, -springLen, 5 * (float)GetPhysicsProcessDeltaTime()), 
			_wheel.Position.Z);
		contact = _wheel.GlobalPosition;
		var forcePos = contact - car.GlobalPosition;

		var springForce = SpringStrength * CurrentOffset;
		var tireVel = car.GetPointVelocity(contact);
		var springDampF = SpringDamping * GlobalBasis.Y.Dot(tireVel);

		var yForce = (springForce - springDampF) * GetCollisionNormal();

		if (IsMotor && car.MotorInput != 0)
		{
			var speedRatio = vel / car.MaxSpeed;
			var ac = car.AccelCurve.SampleBaked(speedRatio);
			var accelForce = forwardDir * car.Acceleration * car.MotorInput * ac;
			car.ApplyForce(accelForce, forcePos);
			// if (ShowDebug) 
			// 	DebugDraw.DrawArrowRay(contact, accelForce / car.Mass, 2.5f, 0.5f, Colors.Red);
		}

		var steeringXVel = GlobalBasis.X.Dot(tireVel);
		var tireSpeed = tireVel.Length();
		GripFactor = tireSpeed > 0.001f ? Mathf.Abs(steeringXVel / tireSpeed) : 0.0f;
		var xTraction = GripCurve.SampleBaked(Mathf.Clamp(GripFactor, 0.0f, 1.0f));

		if (!car.HandBreak && GripFactor < 0.2f)
			car.IsSlipping = false;
		if (car.HandBreak && IsBackWheel)
			xTraction = 0.1f;
		else if (car.IsSlipping)
			xTraction = 0.1f;

		var gravity = -car.GetGravity().Y;
		var xForce = -GlobalBasis.X * steeringXVel * xTraction * ((car.Mass * gravity) / car.TotalWheels);

		var fVel = forwardDir.Dot(tireVel);
		var zFriction = IsBraking ? ZBrakeTraction : ZTraction;
		var zForce = GlobalBasis.Z * fVel * zFriction * ((car.Mass * gravity) / car.TotalWheels);

		car.ApplyForce(yForce, forcePos);
		car.ApplyForce(xForce, forcePos);
		car.ApplyForce(zForce, forcePos);

		// if (ShowDebug) 
		// 	DebugDraw.DrawArrowRay(contact, yForce / car.Mass, 2.5f);
		// if (ShowDebug) 
		// 	DebugDraw.DrawArrowRay(contact, xForce / car.Mass, 1.5f, 0.2f, Colors.Yellow);
	}
}

