using Godot;

public sealed class PlayerAnimationController
{
	private enum MoveDirection
	{
		None,
		Forward,
		Backward,
		Side
	}

	private static readonly StringName AnimIdle = "m root";
	private static readonly StringName AnimRun = "m run";
	private static readonly StringName AnimBackward = "m backward";
	private static readonly StringName AnimCrouchIdle = "m crouch root";
	private static readonly StringName AnimCrouchWalk = "m crouch walk";
	private static readonly StringName AnimCrouchSide = "m crouch side";
	private static readonly StringName AnimCrouchBack = "m crouch backward";
	private static readonly StringName AnimJump = "m jump";
	private static readonly StringName AnimDeath = "m death";

	private const float BlendSeconds = 0.08f;
	private const float DirectionThreshold = 0.25f;

	private AnimationPlayer _animationPlayer;
	private Node3D _owner;
	private StringName _current = default;
	private MoveDirection _lastDirection = MoveDirection.Forward;

	public void Initialize(Node3D owner, NodePath animationPlayerPath)
	{
		_owner = owner;
		_animationPlayer = owner.GetNodeOrNull<AnimationPlayer>(animationPlayerPath);
		if (_animationPlayer == null)
		{
			GD.PushWarning($"PlayerAnimationController: AnimationPlayer not found at path {animationPlayerPath}");
			return;
		}

		EnsureLoopingAnimations();
		Play(AnimIdle, 0f, 1f, immediate: true);
	}

	public void Update(Vector3 velocity, PlayerMovementStateKind movementState, bool isDead)
	{
		if (_animationPlayer == null || _owner == null)
			return;

		var planarVelocity = new Vector3(velocity.X, 0f, velocity.Z);
		var planarSpeed = planarVelocity.Length();
		var direction = ResolveDirection(planarVelocity, _owner.GlobalTransform.Basis);

		if (direction != MoveDirection.None)
			_lastDirection = direction;
		else
			direction = _lastDirection;

		var target = SelectAnimation(movementState, planarSpeed, direction, isDead);
		var speedScale = CalculateSpeedScale(planarSpeed, movementState);

		if (_current != target)
		{
			Play(target, BlendSeconds, speedScale);
			_current = target;
		}
		else
		{
			_animationPlayer.SpeedScale = speedScale;
		}
	}

	public void ResetToIdle()
	{
		_lastDirection = MoveDirection.Forward;
		_current = default;
		if (_animationPlayer != null)
		{
			_animationPlayer.SpeedScale = 1f;
			Play(AnimIdle, 0f, 1f, immediate: true);
		}
	}

	private void EnsureLoopingAnimations()
	{
		if (_animationPlayer == null)
			return;

		var looping = new[]
		{
			AnimIdle,
			AnimRun,
			AnimBackward,
			AnimCrouchIdle,
			AnimCrouchWalk,
			AnimCrouchSide,
			AnimCrouchBack
		};

		foreach (var name in looping)
		{
			var anim = _animationPlayer.GetAnimation(name);
			if (anim != null)
				anim.LoopMode = Animation.LoopModeEnum.Linear;
		}
	}

	private static StringName SelectAnimation(PlayerMovementStateKind movementState, float planarSpeed, MoveDirection direction, bool isDead)
	{
		if (isDead)
			return AnimDeath;

		if (movementState == PlayerMovementStateKind.Airborne || movementState == PlayerMovementStateKind.WallRunning)
			return AnimJump;

		var isCrouched = movementState == PlayerMovementStateKind.Crouching || movementState == PlayerMovementStateKind.Sliding;
		var moving = planarSpeed > 0.2f;

		if (!moving)
			return isCrouched ? AnimCrouchIdle : AnimIdle;

		if (isCrouched)
		{
			return direction switch
			{
				MoveDirection.Backward => AnimCrouchBack,
				MoveDirection.Side => AnimCrouchSide,
				_ => AnimCrouchWalk
			};
		}

		return direction switch
		{
			MoveDirection.Backward => AnimBackward,
			_ => AnimRun
		};
	}

	private static float CalculateSpeedScale(float planarSpeed, PlayerMovementStateKind state)
	{
		var baseline = state switch
		{
			PlayerMovementStateKind.Crouching => 3.5f,
			PlayerMovementStateKind.Sliding => 6f,
			PlayerMovementStateKind.WallRunning => 8f,
			PlayerMovementStateKind.Airborne => 1f,
			_ => 7.5f
		};

		if (baseline <= 0.001f)
			return 1f;

		return Mathf.Clamp(planarSpeed / baseline, 0.6f, 1.8f);
	}

	private static MoveDirection ResolveDirection(Vector3 planarVelocity, Basis basis)
	{
		if (planarVelocity.LengthSquared() < 0.05f)
			return MoveDirection.None;

		var forward = basis.Z;
		forward.Y = 0f;
		if (!forward.IsZeroApprox())
			forward = forward.Normalized();

		var right = basis.X;
		right.Y = 0f;
		if (!right.IsZeroApprox())
			right = right.Normalized();

		var normalized = planarVelocity.Normalized();
		var forwardDot = forward.IsZeroApprox() ? 0f : normalized.Dot(forward);
		var rightDot = right.IsZeroApprox() ? 0f : normalized.Dot(right);

		if (Mathf.Abs(forwardDot) >= Mathf.Abs(rightDot))
		{
			if (forwardDot > DirectionThreshold)
				return MoveDirection.Forward;
			if (forwardDot < -DirectionThreshold)
				return MoveDirection.Backward;
		}

		if (Mathf.Abs(rightDot) > DirectionThreshold * 0.5f)
			return MoveDirection.Side;

		return MoveDirection.Forward;
	}

	private void Play(StringName animation, float blend, float speedScale, bool immediate = false)
	{
		if (_animationPlayer == null)
			return;

		if (!_animationPlayer.HasAnimation(animation))
			return;

		_animationPlayer.SpeedScale = speedScale;
		_animationPlayer.Play(animation, customBlend: immediate ? 0f : blend, customSpeed: 1f);
	}
}
