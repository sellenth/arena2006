using Godot;
using Godot.Collections;

public sealed class PlayerMovementComponent
{
	private readonly CharacterBody3D _body;
	private readonly PlayerMovementController _controller;

	private CollisionShape3D _collisionShape;
	private CapsuleShape3D _capsuleShape;
	private Node3D _head;

	private float _standingHeight;
	private float _crouchHeight;
	private float _crouchTransitionSpeed;
	private float _currentCapsuleHeight;
	private float _standingHeadHeight;

	private bool _wasWallRunning = false;
	private bool _wallRunJumpLock = false;
	private float _wallRunTime = 0f;
	private float _wallRunCooldownTimer = 0f;
	private Vector3 _currentWallNormal = Vector3.Zero;
	private Vector3 _currentWallDirection = Vector3.Zero;
	private float _jumpCooldownTimer = 0f;

	private const float WallRunProbeUpperHeight = 1.2f;
	private const float WallRunProbeLowerHeight = 0.6f;

	public PlayerMovementComponent(CharacterBody3D body, PlayerMovementSettings settings = null)
	{
		_body = body;
		_controller = new PlayerMovementController(settings);
	}

	public PlayerMovementStateKind State => _controller.State;
	public bool IsWallRunning => _controller.IsWallRunning;
	public PlayerMovementSettings Settings => _controller.Settings;

	public Vector3 Velocity
	{
		get => _body?.Velocity ?? Vector3.Zero;
		set
		{
			if (_body != null)
				_body.Velocity = value;
		}
	}

	public void ConfigureCollision(CollisionShape3D collisionShape, Node3D head, float standingHeight, float crouchHeight, float crouchTransitionSpeed)
	{
		_collisionShape = collisionShape;
		_head = head;
		_standingHeadHeight = head != null ? head.Position.Y : 0f;
		_standingHeight = standingHeight;
		_crouchHeight = crouchHeight;
		_crouchTransitionSpeed = crouchTransitionSpeed;

		if (_collisionShape?.Shape is CapsuleShape3D capsule)
		{
			_capsuleShape = (CapsuleShape3D)_collisionShape.Shape;
			_collisionShape.Shape = _capsuleShape;
			_currentCapsuleHeight = _capsuleShape.Height;
			_standingHeight = _capsuleShape.Height;
		}
		else
		{
			_currentCapsuleHeight = standingHeight;
		}
	}

	public void Step(PlayerInputState inputState, float delta)
	{
		if (_body == null || inputState == null)
			return;

		var basis = _body.GlobalTransform.Basis;
		UpdateWallRunTimers(delta);

		if (_jumpCooldownTimer > 0f)
			_jumpCooldownTimer = Mathf.Max(0f, _jumpCooldownTimer - delta);

		var canWallRun = TryFindWallRun(basis, inputState.MoveInput, out var wallNormal, out var wallDirection);
		if (canWallRun)
		{
			_currentWallNormal = wallNormal;
			_currentWallDirection = wallDirection;
		}
		var jumpInput = inputState.Jump && _jumpCooldownTimer <= 0f;

		if (canWallRun || _wasWallRunning)
		{
			if (!jumpInput)
			{
				_wallRunJumpLock = false;
			}
			else if (!_wasWallRunning && canWallRun)
			{
				_wallRunJumpLock = true; // prevent holding jump from insta-wall-jumping on entry
			}

			jumpInput = jumpInput && !_wallRunJumpLock;
		}

		var context = new PlayerMovementContext(
			Velocity,
			inputState.MoveInput,
			jumpInput,
			inputState.Sprint,
			inputState.Crouch,
			inputState.CrouchPressed,
			_body.IsOnFloor(),
			basis,
			canWallRun,
			wallNormal,
			wallDirection);
		Velocity = _controller.Step(context, delta);
		_body.MoveAndSlide();

		var isWallRunning = _controller.IsWallRunning;

		if (!isWallRunning && _body.GetSlideCollisionCount() > 0 && !_body.IsOnFloor())
		{
			var collision = _body.GetLastSlideCollision();
			if (collision != null)
			{
				Velocity = Velocity.Slide(collision.GetNormal());
			}
		}

		if (_controller.WallJumpedThisFrame || (_wasWallRunning && !isWallRunning))
		{
			_wallRunCooldownTimer = Mathf.Max(_wallRunCooldownTimer, _controller.Settings.WallRunCooldown);
		}

		if (_controller.JumpedThisFrame)
		{
			var cooldown = _controller.Settings?.JumpCooldown ?? 0f;
			if (cooldown > 0f)
				_jumpCooldownTimer = Mathf.Max(_jumpCooldownTimer, cooldown);
		}

		_wasWallRunning = isWallRunning;

		if (!_wasWallRunning)
		{
			_currentWallNormal = Vector3.Zero;
			_currentWallDirection = Vector3.Zero;
		}
	}

	public void UpdateCapsuleHeight(float delta, PlayerMovementStateKind state, bool adjustHead)
	{
		if (_capsuleShape == null || _collisionShape == null)
			return;

		var isCrouched = state == PlayerMovementStateKind.Crouching || state == PlayerMovementStateKind.Sliding;
		var targetHeight = isCrouched ? _crouchHeight : _standingHeight;

		if (Mathf.Abs(_currentCapsuleHeight - targetHeight) > 0.001f)
		{
			var blend = Mathf.Clamp(delta * _crouchTransitionSpeed, 0f, 1f);
			_currentCapsuleHeight = Mathf.Lerp(_currentCapsuleHeight, targetHeight, blend);

			_capsuleShape.Height = _currentCapsuleHeight;

			var collisionPos = _collisionShape.Position;
			collisionPos.Y = _currentCapsuleHeight * 0.5f;
			_collisionShape.Position = collisionPos;

			if (_head != null && adjustHead)
			{
				var heightDifference = _standingHeight - _currentCapsuleHeight;
				var headPos = _head.Position;
				headPos.Y = _standingHeadHeight - heightDifference;
				_head.Position = headPos;
			}
		}
	}

	public void Reset()
	{
		_controller.Reset();
		_wasWallRunning = false;
		_wallRunJumpLock = false;
		_wallRunTime = 0f;
		_wallRunCooldownTimer = 0f;
		_currentWallNormal = Vector3.Zero;
		_currentWallDirection = Vector3.Zero;
		_jumpCooldownTimer = 0f;
	}

	public void ApplyImpulse(Vector3 impulse)
	{
		if (impulse == Vector3.Zero)
			return;

		Velocity += impulse;
		_controller.Reset();
	}

	public void ApplyLaunchVelocity(Vector3 velocity)
	{
		Velocity += velocity;
		_controller.Reset();
	}

	private void UpdateWallRunTimers(float delta)
	{
		if (_wallRunCooldownTimer > 0f)
		{
			_wallRunCooldownTimer = Mathf.Max(0f, _wallRunCooldownTimer - delta);
		}

		if (_wasWallRunning)
		{
			_wallRunTime += delta;
		}
		else
		{
			_wallRunTime = 0f;
		}
	}

	private bool TryFindWallRun(Basis basis, Vector2 moveInput, out Vector3 wallNormal, out Vector3 wallDirection)
	{
		wallNormal = Vector3.Zero;
		wallDirection = Vector3.Zero;

		if (_controller.Settings == null)
			return false;

		var settings = _controller.Settings;

		if (_body.IsOnFloor())
			return false;

		if (_wallRunCooldownTimer > 0f)
			return false;

		if (_wallRunTime >= settings.WallRunMaxDuration)
			return false;

		var space = _body.GetWorld3D()?.DirectSpaceState;
		if (space == null)
			return false;

		if (moveInput.Length() < settings.WallRunMinInput)
			return false;

		var wishDir = PlayerMovementController.BuildWishDirection(basis, moveInput);

		if (_wasWallRunning && _currentWallNormal != Vector3.Zero && _currentWallDirection != Vector3.Zero)
		{
			var oppositeIntent = wishDir != Vector3.Zero && wishDir.Dot(_currentWallDirection) < -0.25f;
			if (!oppositeIntent && ConfirmExistingWall(space, settings, _currentWallNormal, out var confirmedNormal))
			{
				wallNormal = confirmedNormal;
				wallDirection = _currentWallDirection;
				return true;
			}
		}

		if (wishDir == Vector3.Zero)
			return false;

		if (!TryProbeWall(basis, space, settings, wishDir, out wallNormal))
			return false;

		wallDirection = CalculateWallRunDirection(wishDir, wallNormal, Velocity);
		return wallDirection != Vector3.Zero;
	}

	private bool TryProbeWall(Basis basis, PhysicsDirectSpaceState3D space, PlayerMovementSettings settings, Vector3 wishDir, out Vector3 wallNormal)
	{
		wallNormal = Vector3.Zero;

		var directions = new[] { basis.X, -basis.X };
		var heights = new[] { WallRunProbeUpperHeight, WallRunProbeLowerHeight };
		foreach (var height in heights)
		{
			var start = _body.GlobalTransform.Origin + Vector3.Up * height;
			foreach (var dir in directions)
			{
				var to = start + dir.Normalized() * settings.WallCheckDistance;
				var query = PhysicsRayQueryParameters3D.Create(start, to);
				query.CollideWithAreas = false;
				query.Exclude = new Array<Rid> { _body.GetRid() };
				var result = space.IntersectRay(query);
				if (result.Count == 0)
					continue;

				var normal = ((Vector3)result["normal"]).Normalized();
				if (Mathf.Abs(normal.Y) > settings.WallRunMaxNormalY)
					continue;

				if (wishDir.Dot(normal) > -0.1f)
					continue;

				wallNormal = normal;
				return true;
			}
		}

		return false;
	}

	private bool ConfirmExistingWall(PhysicsDirectSpaceState3D space, PlayerMovementSettings settings, Vector3 wallNormal, out Vector3 confirmedNormal)
	{
		confirmedNormal = Vector3.Zero;

		var normalDir = wallNormal.Normalized();
		if (normalDir == Vector3.Zero)
			return false;

		var heights = new[] { WallRunProbeUpperHeight, WallRunProbeLowerHeight };
		foreach (var height in heights)
		{
			var start = _body.GlobalTransform.Origin + Vector3.Up * height;
			var to = start - normalDir * settings.WallCheckDistance;
			var query = PhysicsRayQueryParameters3D.Create(start, to);
			query.CollideWithAreas = false;
			query.Exclude = new Array<Rid> { _body.GetRid() };
			var result = space.IntersectRay(query);
			if (result.Count == 0)
				continue;

			var normal = ((Vector3)result["normal"]).Normalized();
			if (Mathf.Abs(normal.Y) > settings.WallRunMaxNormalY)
				continue;

			if (normal.Dot(normalDir) > 0.5f)
			{
				confirmedNormal = normal;
				return true;
			}
		}

		return false;
	}

	private static Vector3 CalculateWallRunDirection(Vector3 wishDir, Vector3 wallNormal, Vector3 velocity)
	{
		var alongWall = wallNormal.Cross(Vector3.Up);
		if (alongWall == Vector3.Zero)
			return Vector3.Zero;

		alongWall = alongWall.Normalized();

		var planarVel = new Vector3(velocity.X, 0f, velocity.Z);
		var directionalIntent = wishDir;
		if (planarVel.LengthSquared() > 0.05f)
		{
			directionalIntent += planarVel.Normalized() * 0.75f; // bias towards actual travel to avoid backward runs
		}

		if (directionalIntent != Vector3.Zero && alongWall.Dot(directionalIntent) < 0f)
			alongWall = -alongWall;

		return directionalIntent == Vector3.Zero || alongWall.Dot(directionalIntent) > 0.05f ? alongWall : Vector3.Zero;
	}
}
