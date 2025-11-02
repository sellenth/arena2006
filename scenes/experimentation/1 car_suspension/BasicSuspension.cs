using Godot;
using Godot.Collections;

public partial class BasicSuspension : RigidBody3D
{
	[Export] public Array<RayCast3D> Wheels { get; set; }

	[Export] public bool UseWheels { get; set; } = true;
	[Export] public bool DisablePullForce { get; set; } = false;
	[Export] public bool DisableSuspension { get; set; } = false;
	[Export] public bool DisableForces { get; set; } = false;

	public Vector3 GetPointVelocity(Vector3 point)
	{
		return LinearVelocity + AngularVelocity.Cross(point - GlobalTransform.Origin);
	}

	public override void _PhysicsProcess(double delta)
	{
		foreach (var wheel in Wheels)
		{
			if (DisableSuspension)
				break;
			SingleWheelSuspension(wheel);
		}
	}

	private void SingleWheelSuspension(RayCast3D suspensionRay)
	{
		if (!suspensionRay.IsColliding()) return;

		var contact = suspensionRay.GetCollisionPoint();
		var springUpDir = suspensionRay.GlobalTransform.Basis.Y;
		var restDist = suspensionRay.TargetPosition.Length() / 2.0f;
		var springHitDistance = suspensionRay.GlobalPosition.DistanceTo(contact);
		if (UseWheels)
			springHitDistance -= 0.4f;
		var offset = restDist - springHitDistance;
		offset = Mathf.Clamp(offset, suspensionRay.TargetPosition.Y / 2.0f, -suspensionRay.TargetPosition.Y / 2.0f);

		var wheel = suspensionRay.GetNode<Node3D>("Wheel");

		var worldVel = GetPointVelocity(wheel.GlobalPosition);
		var vel = springUpDir.Dot(worldVel);
		var springDamper = 2;
		var springStrength = 100.0f;

		if (DisablePullForce && offset < 0)
			return;

		var force = (offset * springStrength) - (vel * springDamper);
		var forceVector = springUpDir * force;
		var forcePositionOffset = wheel.GlobalPosition - GlobalPosition;
		if (!DisableForces)
			ApplyForce(forceVector, forcePositionOffset);

		wheel.Position = new Vector3(wheel.Position.X, -springHitDistance, wheel.Position.Z);
		GetNode<Label>("%OffsetLabel").Text = $"Offset: {offset:F3}";

		forceVector = (forceVector / springStrength) * 10.0f;
		// DebugDraw.DrawArrowRay(wheel.GlobalPosition, forceVector, 2.0f, 0.2f);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("jump"))
		{
			SetPhysicsProcess(false);
			ApplyCentralImpulse(Vector3.Up * 5);
			GetTree().CreateTimer(0.3).Timeout += () => SetPhysicsProcess(true);
		}

		if (@event.IsActionPressed("toggle_pull_forces"))
		{
			DisablePullForce = !DisablePullForce;
			Freeze = false;
		}
		if (@event.IsActionPressed("toggle_all_forces"))
		{
			DisableSuspension = !DisableSuspension;
			Freeze = false;
		}

		if (@event.IsActionPressed("speed_1"))
		{
			Engine.TimeScale = 1.0;
			Freeze = false;
		}
		if (@event.IsActionPressed("speed_2"))
		{
			Engine.TimeScale = 0.25;
			Freeze = false;
		}
		if (@event.IsActionPressed("speed_3"))
		{
			Engine.TimeScale = 0.1;
			Freeze = false;
		}

		if (@event.IsActionPressed("quit"))
			GetTree().Quit();
	}
}

