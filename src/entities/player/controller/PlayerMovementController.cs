using Godot;

public enum PlayerMovementStateKind
{
	Grounded,
	Airborne
}

public readonly struct PlayerMovementContext
{
	public PlayerMovementContext(Vector3 velocity, Vector2 moveInput, bool jump, bool sprint, bool onFloor, Basis referenceBasis)
	{
		Velocity = velocity;
		MoveInput = moveInput;
		Jump = jump;
		Sprint = sprint;
		OnFloor = onFloor;
		ReferenceBasis = referenceBasis;
	}

	public Vector3 Velocity { get; }
	public Vector2 MoveInput { get; }
	public bool Jump { get; }
	public bool Sprint { get; }
	public bool OnFloor { get; }
	public Basis ReferenceBasis { get; }
}

public sealed class PlayerMovementSettings
{
	public float Gravity = 9.8f;
	public float JumpVelocity = 3f;
	public float GroundAcceleration = 40f;
	public float GroundSpeedLimit = 60f;
	public float GroundFriction = 0.9f;
	public float AirAcceleration = 80f;
	public float AirSpeedLimit = 0.8f;
	public float AirDrag = 0f;

	public static PlayerMovementSettings CreateDefaults()
	{
		return new PlayerMovementSettings();
	}
}

public sealed class PlayerMovementController
{
	private readonly PlayerMovementSettings _settings;
	private readonly IMovementState _groundState;
	private readonly IMovementState _airState;
	private IMovementState _currentState;

	public PlayerMovementController(PlayerMovementSettings settings = null)
	{
		_settings = settings ?? PlayerMovementSettings.CreateDefaults();
		_groundState = new GroundedState(this);
		_airState = new AirborneState(this);
		_currentState = _airState;
	}

	public PlayerMovementStateKind State => _currentState.StateKind;
	public bool JumpedThisFrame { get; private set; }
	public float LastPlanarSpeed { get; private set; }

	public Vector3 Step(in PlayerMovementContext context, float delta)
	{
		JumpedThisFrame = false;
		SwitchState(context.OnFloor ? PlayerMovementStateKind.Grounded : PlayerMovementStateKind.Airborne);
		var velocity = _currentState.Tick(context, delta);
		LastPlanarSpeed = new Vector3(velocity.X, 0f, velocity.Z).Length();
		return velocity;
	}

	public void Reset()
	{
		_currentState = _airState;
		JumpedThisFrame = false;
		LastPlanarSpeed = 0f;
	}

	internal void MarkJumped()
	{
		JumpedThisFrame = true;
		SwitchState(PlayerMovementStateKind.Airborne);
	}

	internal Vector3 ApplyAcceleration(Vector3 velocity, Vector3 wishDir, float speedLimit, float acceleration, float delta)
	{
		if (wishDir == Vector3.Zero || speedLimit <= 0f || acceleration <= 0f)
			return velocity;

		var currentSpeed = velocity.Dot(wishDir);
		var addSpeed = speedLimit - currentSpeed;
		if (addSpeed <= 0f)
			return velocity;

		var accelSpeed = acceleration * delta;
		if (accelSpeed > addSpeed)
			accelSpeed = addSpeed;

		return velocity + wishDir * accelSpeed;
	}

	internal Vector3 BuildWishDirection(Basis basis, Vector2 input)
	{
		if (input == Vector2.Zero)
			return Vector3.Zero;

		var forward = basis.Z;
		forward.Y = 0f;
		if (!forward.IsZeroApprox())
			forward = forward.Normalized();

		var right = basis.X;
		right.Y = 0f;
		if (!right.IsZeroApprox())
			right = right.Normalized();

		var wish = forward * input.Y + right * input.X;
		if (wish.LengthSquared() > 1f)
			wish = wish.Normalized();
		return wish;
	}

	private void SwitchState(PlayerMovementStateKind target)
	{
		var desired = target == PlayerMovementStateKind.Grounded ? _groundState : _airState;
		if (_currentState == desired)
			return;
		_currentState = desired;
	}

	private interface IMovementState
	{
		PlayerMovementStateKind StateKind { get; }
		Vector3 Tick(in PlayerMovementContext context, float delta);
	}

	private sealed class GroundedState : IMovementState
	{
		private readonly PlayerMovementController _controller;
		private readonly PlayerMovementSettings _settings;

		public GroundedState(PlayerMovementController controller)
		{
			_controller = controller;
			_settings = controller._settings;
		}

		public PlayerMovementStateKind StateKind => PlayerMovementStateKind.Grounded;

		public Vector3 Tick(in PlayerMovementContext context, float delta)
		{
			var velocity = context.Velocity;

			if (context.Jump)
			{
				velocity.Y = _settings.JumpVelocity;
				_controller.MarkJumped();
			}
			else
			{
				var friction = Mathf.Clamp(_settings.GroundFriction, 0f, 1f);
				velocity.X *= friction;
				velocity.Z *= friction;
			}

			var wishDir = _controller.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
			velocity = _controller.ApplyAcceleration(velocity, wishDir, _settings.GroundSpeedLimit, _settings.GroundAcceleration, delta);

			return velocity;
		}
	}

	private sealed class AirborneState : IMovementState
	{
		private readonly PlayerMovementController _controller;
		private readonly PlayerMovementSettings _settings;

		public AirborneState(PlayerMovementController controller)
		{
			_controller = controller;
			_settings = controller._settings;
		}

		public PlayerMovementStateKind StateKind => PlayerMovementStateKind.Airborne;

		public Vector3 Tick(in PlayerMovementContext context, float delta)
		{
			var velocity = context.Velocity;
			velocity.Y -= _settings.Gravity * delta;

			var wishDir = _controller.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
			velocity = _controller.ApplyAcceleration(velocity, wishDir, _settings.AirSpeedLimit, _settings.AirAcceleration, delta);

			var drag = Mathf.Clamp(_settings.AirDrag * delta, 0f, 1f);
			velocity.X *= 1f - drag;
			velocity.Z *= 1f - drag;

			return velocity;
		}
	}
}
