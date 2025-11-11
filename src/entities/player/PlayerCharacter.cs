using System.Diagnostics;
using Godot;

public partial class PlayerCharacter : CharacterBody3D
{
	[Export] public NodePath HeadPath { get; set; } = "Head";
	[Export] public NodePath CameraPath { get; set; } = "Head/Cam";
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";
	[Export] public NodePath CollisionShapePath { get; set; } = "Collision";
	[Export] public bool AutoRegisterWithNetwork { get; set; } = true;

	private const float MouseSensitivity = 0.002f;
	private const float JumpVelocity = 3f;
	private const float Gravity = 9.8f;
	private const float GroundAccel = 40f;
	private const float GroundSpeedLimit = 60f;
	private const float AirAccel = 80f;
	private const float AirSpeedLimit = .8f;
	private const float GroundFriction = 0.9f;
	private const float MinPitch = -1.2f;
	private const float MaxPitch = 1.2f;

	private Node3D _head;
	private Camera3D _camera;
	private MeshInstance3D _mesh;
	private CollisionShape3D _collisionShape;
	private NetworkController _networkController;

	private readonly PlayerInputState _inputState = new PlayerInputState();
	private PlayerSnapshot _pendingSnapshot;
	private Vector2 _lookAccumulator = Vector2.Zero;
	private Vector2 _pendingMouseDelta = Vector2.Zero;
	private Color _playerColor = Colors.Red;
	private bool _isAuthority = true;
	private bool _simulateLocally = true;
	private bool _cameraActive = false;
	private bool _worldActive = true;
	private bool _managesMouseMode = false;
	private float _viewYaw = 0f;
	private float _viewPitch = 0f;

	public PlayerCharacter()
	{
		// CharacterBody defaults for surf-style movement
		FloorStopOnSlope = false;
		FloorBlockOnWall = false;
		WallMinSlideAngle = Mathf.DegToRad(167);
	}

	public override void _Ready()
	{
		_head = GetNodeOrNull<Node3D>(HeadPath);
		_camera = GetNodeOrNull<Camera3D>(CameraPath);
		_mesh = GetNodeOrNull<MeshInstance3D>(MeshPath);
		_collisionShape = GetNodeOrNull<CollisionShape3D>(CollisionShapePath);

		if (_head == null || _camera == null)
		{
			Debug.Assert(false, "head or camera not found :o");
		}
		else
		{
			_viewYaw = _head.Rotation.Y;
			_viewPitch = _camera.Rotation.X;
		}

		if (AutoRegisterWithNetwork)
		{
			_networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
			if (_networkController != null && _networkController.IsClient)
				_networkController.RegisterPlayerCharacter(this);
		}

		_managesMouseMode = AutoRegisterWithNetwork && (_networkController == null || _networkController.IsClient);

		ApplyColor(_playerColor);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_worldActive)
			return;

		var deltaFloat = (float)delta;
		if (_simulateLocally)
		{
			SimulateMovement(deltaFloat);
		}

		ApplySnapshotCorrection(deltaFloat);

		if (_head != null)
		{
			ApplyLookAccumulator();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_cameraActive)
			return;

		if (@event is InputEventMouseMotion motion)
		{
			_pendingMouseDelta += motion.Relative;
		}
	}

	public PlayerInputState CollectClientInputState()
	{
		var state = new PlayerInputState
		{
			MoveInput = Input.GetVector("move_left", "move_right", "move_forward", "move_backward"),
			Jump = Input.IsActionPressed("jump"),
			Interact = Input.IsActionJustPressed("interact"),
			LookDelta = _pendingMouseDelta
		};
		_pendingMouseDelta = Vector2.Zero;
		return state;
	}

	public void SetInputState(PlayerInputState state)
	{
		_inputState.CopyFrom(state);
		_lookAccumulator += state.LookDelta;
	}

	public PlayerSnapshot CaptureSnapshot(int tick)
	{
		return new PlayerSnapshot
		{
			Tick = tick,
			Transform = GlobalTransform,
			Velocity = Velocity,
			ViewYaw = _viewYaw,
			ViewPitch = _viewPitch
		};
	}

	public void QueueSnapshot(PlayerSnapshot snapshot)
	{
		_pendingSnapshot = snapshot;
	}

	public void ConfigureAuthority(bool isAuthority)
	{
		_isAuthority = isAuthority;
		_simulateLocally = isAuthority;
		SetPhysicsProcess(_worldActive);
	}

	public void SetCameraActive(bool active)
	{
		_cameraActive = active;
		if (_camera != null)
		{
			_camera.Current = active;
		}

		if (_managesMouseMode)
			Input.MouseMode = active ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
	}

	public void SetWorldActive(bool active)
	{
		_worldActive = active;
		Visible = active;
		if (_collisionShape != null)
		{
			_collisionShape.Disabled = !active;
		}
		SetPhysicsProcess(active);
	}

	public void SetPlayerColor(Color color)
	{
		_playerColor = color;
		ApplyColor(color);
	}

	public void SetYawPitch(float yaw, float pitch)
	{
		_viewYaw = yaw;
		_viewPitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
		UpdateViewNodes();
	}

	public void TeleportTo(Transform3D transform)
	{
		GlobalTransform = transform;
		Velocity = Vector3.Zero;
	}

	private void ApplyColor(Color color)
	{
		if (_mesh?.MaterialOverlay is StandardMaterial3D mat)
		{
			mat.AlbedoColor = color;
		}
	}

	private void SimulateMovement(float delta)
	{
		var velocity = Velocity;
		var onFloor = IsOnFloor();

		if (!onFloor)
		{
			velocity.Y -= Gravity * delta;
		}
		else
		{
			if (_inputState.Jump)
			{
				velocity.Y = JumpVelocity;
			}
			else
			{
				velocity *= GroundFriction;
			}
		}

		var moveInput = new Vector3(_inputState.MoveInput.X, 0f, _inputState.MoveInput.Y);
		if (moveInput.LengthSquared() > 1f)
			moveInput = moveInput.Normalized();

		if (moveInput.LengthSquared() > 0f)
		{
			var basis = _head?.GlobalTransform.Basis ?? GlobalTransform.Basis;
			var strafeDirection = (basis * moveInput).Normalized();
			if (strafeDirection.LengthSquared() > 0f)
			{
				var strafeAccel = onFloor ? GroundAccel : AirAccel;
				var speedLimit = onFloor ? GroundSpeedLimit : AirSpeedLimit;

				var currentSpeed = strafeDirection.Dot(velocity);
				var accel = strafeAccel * delta;
				var remaining = speedLimit - currentSpeed;
				if (remaining <= 0f)
				{
					accel = 0f;
				}
				else if (accel > remaining)
				{
					accel = remaining;
				}

				velocity += strafeDirection * accel;
			}
		}

		Velocity = velocity;
		MoveAndSlide();

		if (GetSlideCollisionCount() > 0 && !IsOnFloor())
		{
			var collision = GetLastSlideCollision();
			if (collision != null)
			{
				Velocity = Velocity.Slide(collision.GetNormal());
			}
		}
	}

	private void ApplySnapshotCorrection(float delta)
	{
		if (_pendingSnapshot == null)
			return;

		var target = _pendingSnapshot;
		var blend = Mathf.Clamp(delta * 10f, 0f, 1f);
		GlobalTransform = GlobalTransform.InterpolateWith(target.Transform, blend);
		Velocity = Velocity.Lerp(target.Velocity, blend);
		_viewYaw = Mathf.LerpAngle(_viewYaw, target.ViewYaw, blend);
		_viewPitch = Mathf.Lerp(_viewPitch, target.ViewPitch, blend);
		UpdateViewNodes();
		var posError = GlobalPosition.DistanceTo(target.Transform.Origin);
		if (posError < 0.01f)
		{
			_pendingSnapshot = null;
		}
	}

	private void ApplyLookAccumulator()
	{
		if (_lookAccumulator == Vector2.Zero)
			return;

		_viewYaw -= _lookAccumulator.X * MouseSensitivity;
		_viewPitch = Mathf.Clamp(_viewPitch - _lookAccumulator.Y * MouseSensitivity, MinPitch, MaxPitch);
		_lookAccumulator = Vector2.Zero;
		UpdateViewNodes();
	}

	private void UpdateViewNodes()
	{
		if (_head == null)
			return;

		_head.Rotation = new Vector3(_head.Rotation.X, _viewYaw, _head.Rotation.Z);
		if (_camera != null)
		{
			_camera.Rotation = new Vector3(_viewPitch, _camera.Rotation.Y, _camera.Rotation.Z);
		}
	}

	public Vector3 GetViewDirection()
	{
		if (_head != null)
			return -_head.GlobalTransform.Basis.Z;
		return -GlobalTransform.Basis.Z;
	}

	public float DistanceTo(Node3D other)
	{
		if (other == null)
			return float.MaxValue;
		return GlobalPosition.DistanceTo(other.GlobalPosition);
	}
}
