using Godot;

public enum PlayerMovementStateKind
{
	Grounded,
	Airborne,
	WallRunning
}

public readonly struct PlayerMovementContext
{
	public PlayerMovementContext(
		Vector3 velocity,
		Vector2 moveInput,
		bool jump,
		bool sprint,
		bool onFloor,
		Basis referenceBasis,
		bool canWallRun,
		Vector3 wallNormal,
		Vector3 wallDirection)
	{
		Velocity = velocity;
		MoveInput = moveInput;
		Jump = jump;
		Sprint = sprint;
		OnFloor = onFloor;
		ReferenceBasis = referenceBasis;
		CanWallRun = canWallRun;
		WallNormal = wallNormal;
		WallDirection = wallDirection;
	}

	public Vector3 Velocity { get; }
	public Vector2 MoveInput { get; }
	public bool Jump { get; }
	public bool Sprint { get; }
	public bool OnFloor { get; }
	public Basis ReferenceBasis { get; }
	public bool CanWallRun { get; }
	public Vector3 WallNormal { get; }
	public Vector3 WallDirection { get; }
}

public sealed class PlayerMovementSettings
{
	public float Gravity = 9.8f;
	public float JumpVelocity = 3f;
	public float JumpCooldown = 0.3f;

	public float GroundAcceleration = 40f;
	public float GroundSpeedLimit = 60f;
	public float GroundFriction = 0.9f;

	public float AirAcceleration = 80f;
	public float AirSpeedLimit = 0.8f;
	public float AirDrag = 0f;
	public float SprintSpeedMultiplier = 1.5f;

	public float WallCheckDistance = 0.9f;
	public float WallRunMaxNormalY = 0.25f;
	public float WallRunMinInput = 0.2f;
	public float WallRunMaxDuration = 1.75f;
	public float WallRunCooldown = 0.35f;
	public float WallRunGravityScale = 0.25f;
	public float WallRunAcceleration = 65f;
	public float WallRunSpeedLimit = 12f;
	public float WallRunStickForce = 14f;
	public float WallRunMaxDownwardSpeed = 18f;
	public float WallJumpAwayImpulse = 4f;
	public float WallJumpUpImpulse = 4f;
	public float WallJumpAlongBoost = 4f;

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
	private readonly IMovementState _wallState;
	private IMovementState _currentState;

	public PlayerMovementController(PlayerMovementSettings settings = null)
	{
		_settings = settings ?? PlayerMovementSettings.CreateDefaults();
		_groundState = new GroundedState(this);
		_airState = new AirborneState(this);
		_wallState = new WallRunState(this);
		_currentState = _airState;
	}

	public PlayerMovementStateKind State => _currentState.StateKind;
	public bool JumpedThisFrame { get; private set; }
	public bool WallJumpedThisFrame { get; private set; }
	public float LastPlanarSpeed { get; private set; }
	public bool IsWallRunning => _currentState == _wallState;
	public Vector3 LastWallNormal { get; private set; }
	public PlayerMovementSettings Settings => _settings;

	public Vector3 Step(in PlayerMovementContext context, float delta)
	{
		JumpedThisFrame = false;
		WallJumpedThisFrame = false;
		SwitchState(ResolveState(context), context);
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

	internal void MarkJumped(in PlayerMovementContext context)
	{
		JumpedThisFrame = true;
		SwitchState(PlayerMovementStateKind.Airborne, context);
	}

	internal void MarkWallJump(Vector3 wallNormal, in PlayerMovementContext context)
	{
		LastWallNormal = wallNormal;
		WallJumpedThisFrame = true;
		MarkJumped(context);
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

	internal static Vector3 BuildWishDirection(Basis basis, Vector2 input)
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

	private PlayerMovementStateKind ResolveState(in PlayerMovementContext context)
	{
		if (context.OnFloor)
			return PlayerMovementStateKind.Grounded;
		if (context.CanWallRun)
			return PlayerMovementStateKind.WallRunning;
		return PlayerMovementStateKind.Airborne;
	}

	private void SwitchState(PlayerMovementStateKind target, in PlayerMovementContext context)
	{
		IMovementState desired = target switch
		{
			PlayerMovementStateKind.Grounded => _groundState,
			PlayerMovementStateKind.WallRunning => _wallState,
			_ => _airState
		};
		if (_currentState == desired)
			return;
		_currentState = desired;
		_currentState.Enter(context);
	}

	private interface IMovementState
	{
		PlayerMovementStateKind StateKind { get; }
		void Enter(in PlayerMovementContext context);
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

		public void Enter(in PlayerMovementContext context)
		{
		}

		public Vector3 Tick(in PlayerMovementContext context, float delta)
		{
			var velocity = context.Velocity;

			if (context.Jump)
			{
				velocity.Y = _settings.JumpVelocity;
				_controller.MarkJumped(context);
			}
			else
			{
				var friction = Mathf.Clamp(_settings.GroundFriction, 0f, 1f);
				velocity.X *= friction;
				velocity.Z *= friction;
			}

			var wishDir = PlayerMovementController.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
			var sprintMul = context.Sprint ? _settings.SprintSpeedMultiplier : 1f;
			velocity = _controller.ApplyAcceleration(
				velocity,
				wishDir,
				_settings.GroundSpeedLimit * sprintMul,
				_settings.GroundAcceleration * sprintMul,
				delta);

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

		public void Enter(in PlayerMovementContext context)
		{
		}

		public Vector3 Tick(in PlayerMovementContext context, float delta)
		{
			var velocity = context.Velocity;
			velocity.Y -= _settings.Gravity * delta;

			var wishDir = PlayerMovementController.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
			var sprintMul = context.Sprint ? _settings.SprintSpeedMultiplier : 1f;
			velocity = _controller.ApplyAcceleration(
				velocity,
				wishDir,
				_settings.AirSpeedLimit * sprintMul,
				_settings.AirAcceleration * sprintMul,
				delta);

			var drag = Mathf.Clamp(_settings.AirDrag * delta, 0f, 1f);
			velocity.X *= 1f - drag;
			velocity.Z *= 1f - drag;

			return velocity;
		}
	}

	private sealed class WallRunState : IMovementState
	{
		private readonly PlayerMovementController _controller;
		private readonly PlayerMovementSettings _settings;
		private Vector3 _wallNormal = Vector3.Zero;
		private Vector3 _wallDirection = Vector3.Zero;
		private float _elapsed;

		public WallRunState(PlayerMovementController controller)
		{
			_controller = controller;
			_settings = controller._settings;
		}

		public PlayerMovementStateKind StateKind => PlayerMovementStateKind.WallRunning;

		public void Enter(in PlayerMovementContext context)
		{
			_elapsed = 0f;
			_wallNormal = context.WallNormal;
			_wallDirection = context.WallDirection;
		}

		public Vector3 Tick(in PlayerMovementContext context, float delta)
		{
			_elapsed += delta;

			if (!context.CanWallRun || context.WallDirection == Vector3.Zero || _elapsed > _settings.WallRunMaxDuration)
			{
				var exitVelocity = context.Velocity;
				if (_wallNormal != Vector3.Zero)
				{
					var away = exitVelocity.Dot(_wallNormal);
					if (away > 0f)
						exitVelocity -= _wallNormal * away;
				}
				_controller.SwitchState(context.OnFloor ? PlayerMovementStateKind.Grounded : PlayerMovementStateKind.Airborne, context);
				return exitVelocity;
			}

			_wallNormal = context.WallNormal;
			_wallDirection = context.WallDirection;

			var velocity = context.Velocity;

			var wishDir = PlayerMovementController.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
			var alongWall = _wallDirection;
			if (wishDir != Vector3.Zero && alongWall.Dot(wishDir) < 0f)
				alongWall = -alongWall;

			if (alongWall != Vector3.Zero)
			{
				velocity = _controller.ApplyAcceleration(
					velocity,
					alongWall,
					_settings.WallRunSpeedLimit,
					_settings.WallRunAcceleration,
					delta);
			}

			var normalComponent = velocity.Dot(_wallNormal);
			if (normalComponent > 0f)
				velocity -= _wallNormal * normalComponent;
			velocity -= _wallNormal * _settings.WallRunStickForce * delta;

			velocity.Y -= _settings.Gravity * _settings.WallRunGravityScale * delta;
			if (velocity.Y < -_settings.WallRunMaxDownwardSpeed)
				velocity.Y = -_settings.WallRunMaxDownwardSpeed;

			if (context.Jump)
			{
				var jumpDir = -context.ReferenceBasis.Z;
				jumpDir.Y = 0;
				if (jumpDir.LengthSquared() > 0.01f)
					jumpDir = jumpDir.Normalized();
				else
					jumpDir = _wallNormal;

				velocity = jumpDir * (_settings.WallJumpAwayImpulse + _settings.WallJumpAlongBoost)
					+ Vector3.Up * _settings.WallJumpUpImpulse;
				_controller.MarkWallJump(_wallNormal, context);
			}

			return velocity;
		}
	}
}
