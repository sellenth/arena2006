using System.Diagnostics;
using Godot;

public partial class PlayerCharacter : CharacterBody3D, IReplicatedEntity
{
	[Export] public NodePath HeadPath { get; set; } = "Head";
	[Export] public NodePath CameraPath { get; set; } = "Head/Cam";
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";
	[Export] public NodePath CollisionShapePath { get; set; } = "Collision";
	[Export] public bool AutoRegisterWithNetwork { get; set; } = true;
	[Export] public int NetworkId { get; set; } = 0;

	private Node3D _head;
	private Camera3D _camera;
	private MeshInstance3D _mesh;
	private CollisionShape3D _collisionShape;
	private NetworkController _networkController;

	private readonly PlayerInputState _inputState = new PlayerInputState();
	private readonly PlayerLookController _lookController = new PlayerLookController();
	private PlayerMovementController _movementController;
	private readonly PlayerReconciliationController _reconciliation = new PlayerReconciliationController();
	private Color _playerColor = Colors.Red;
	private bool _isAuthority = true;
	private bool _simulateLocally = true;
	private bool _cameraActive = false;
	private bool _worldActive = true;
	private bool _managesMouseMode = false;
	
	private ReplicatedTransform3D _transformProperty;
	private ReplicatedVector3 _velocityProperty;
	private ReplicatedFloat _viewYawProperty;
	private ReplicatedFloat _viewPitchProperty;

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

		InitializeControllerModules();
		
		InitializeReplication();

		if (AutoRegisterWithNetwork)
		{
			_networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
			if (_networkController != null && _networkController.IsClient)
			{
				RegisterAsLocalPlayer();
				_networkController.RegisterPlayerCharacter(this);
			}
		}

		_managesMouseMode = AutoRegisterWithNetwork && (_networkController == null || _networkController.IsClient);

		ApplyColor(_playerColor);
	}
	
	private void InitializeReplication()
	{
		_transformProperty = new ReplicatedTransform3D(
			"Transform",
			() => GlobalTransform,
			(value) => ApplyTransformFromReplication(value),
			ReplicationMode.Always,
			positionThreshold: 0.01f,
			rotationThreshold: Mathf.DegToRad(1.0f)
		);
		
		_velocityProperty = new ReplicatedVector3(
			"Velocity",
			() => Velocity,
			(value) => Velocity = value,
			ReplicationMode.Always
		);
		
		_viewYawProperty = new ReplicatedFloat(
			"ViewYaw",
			() => _lookController?.Yaw ?? 0f,
			(value) => SetViewYaw(value),
			ReplicationMode.Always
		);
		
		_viewPitchProperty = new ReplicatedFloat(
			"ViewPitch",
			() => _lookController?.Pitch ?? 0f,
			(value) => SetViewPitch(value),
			ReplicationMode.Always
		);
	}
	
	private void RegisterAsLocalPlayer()
	{
		var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
		if (remoteManager != null)
		{
			remoteManager.RegisterRemoteEntity(NetworkId, this);
			GD.Print($"PlayerCharacter: Registered as remote with NetworkId {NetworkId}");
		}
		else
		{
			GD.PushWarning("PlayerCharacter: RemoteEntityManager not found in scene!");
		}
	}
	
	private void ApplyTransformFromReplication(Transform3D value)
	{
		var snapshot = new PlayerSnapshot
		{
			Transform = value,
			Velocity = Velocity,
			ViewYaw = _lookController?.Yaw ?? 0f,
			ViewPitch = _lookController?.Pitch ?? 0f
		};
		QueueSnapshot(snapshot);
	}
	
	private void SetViewYaw(float yaw)
	{
		if (_lookController != null && !_isAuthority)
		{
			_lookController.SetYawPitch(yaw, _lookController.Pitch);
		}
	}
	
	private void SetViewPitch(float pitch)
	{
		if (_lookController != null && !_isAuthority)
		{
			_lookController.SetYawPitch(_lookController.Yaw, pitch);
		}
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

		ApplyPendingLookInput();
		_lookController?.Update(deltaFloat, Velocity, IsOnFloor());
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_cameraActive)
			return;

		if (@event is InputEventMouseMotion motion)
		{
			_lookController?.QueueLookDelta(motion.Relative);
		}
	}

	public PlayerInputState CollectClientInputState()
	{
		ApplyPendingLookInput();

		var state = new PlayerInputState
		{
			MoveInput = Input.GetVector("move_left", "move_right", "move_forward", "move_backward"),
			Jump = Input.IsActionPressed("jump"),
			Interact = Input.IsActionJustPressed("interact"),
			ViewYaw = _lookController?.Yaw ?? 0f,
			ViewPitch = _lookController?.Pitch ?? 0f
		};
		return state;
	}

	public void SetInputState(PlayerInputState state)
	{
		if (state == null)
			return;

		_inputState.CopyFrom(state);
		if (!float.IsNaN(state.ViewYaw) && !float.IsNaN(state.ViewPitch))
		{
			SetYawPitch(state.ViewYaw, state.ViewPitch);
		}
	}

	public PlayerSnapshot CaptureSnapshot(int tick)
	{
		return new PlayerSnapshot
		{
			Tick = tick,
			Transform = GlobalTransform,
			Velocity = Velocity,
			ViewYaw = _lookController?.Yaw ?? 0f,
			ViewPitch = _lookController?.Pitch ?? 0f
		};
	}

	public void QueueSnapshot(PlayerSnapshot snapshot)
	{
		_reconciliation.Queue(snapshot);
	}

	public void ClearPendingSnapshot()
	{
		_reconciliation.Clear();
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
		_lookController?.SetYawPitch(yaw, pitch);
	}


	private void ApplyColor(Color color)
	{
		if (_mesh?.MaterialOverlay is StandardMaterial3D mat)
		{
			mat.AlbedoColor = color;
		}
	}

	private void InitializeControllerModules()
	{
		_movementController = new PlayerMovementController();

		var initialYaw = _head != null ? _head.Rotation.Y : Rotation.Y;
		var initialPitch = _camera != null ? _camera.Rotation.X : 0f;
		_lookController.Initialize(_head, _camera, initialYaw, initialPitch);
	}

	private void SimulateMovement(float delta)
	{
		if (_movementController == null)
			return;

		var basis = _head?.GlobalTransform.Basis ?? GlobalTransform.Basis;
		var context = new PlayerMovementContext(Velocity, _inputState.MoveInput, _inputState.Jump, false, IsOnFloor(), basis);
		Velocity = _movementController.Step(context, delta);
		MoveAndSlide();

		if (GetSlideCollisionCount() > 0 && !IsOnFloor())
		{
			var collision = GetLastSlideCollision();
			if (collision != null)
			{
				Velocity = Velocity.Slide(collision.GetNormal());
			}
		}

		if (_networkController != null && _networkController.IsClient && _simulateLocally)
		{
			_networkController.RecordLocalPlayerPrediction(_inputState.Tick, GlobalTransform, Velocity);
		}
	}

	private void ApplySnapshotCorrection(float delta)
	{
		_reconciliation.Apply(this, _lookController, delta);
	}

	private void ApplyPendingLookInput()
	{
		_lookController?.ApplyQueuedLook();
	}

	public Vector3 GetViewDirection()
	{
		return _lookController?.GetViewDirection(this) ?? -GlobalTransform.Basis.Z;
	}

	public float DistanceTo(Node3D other)
	{
		if (other == null)
			return float.MaxValue;
		return GlobalPosition.DistanceTo(other.GlobalPosition);
	}
	
	public void WriteSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Write(buffer);
		_velocityProperty.Write(buffer);
		_viewYawProperty.Write(buffer);
		_viewPitchProperty.Write(buffer);
	}
	
	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Read(buffer);
		_velocityProperty.Read(buffer);
		_viewYawProperty.Read(buffer);
		_viewPitchProperty.Read(buffer);
	}
	
	public int GetSnapshotSizeBytes()
	{
		return _transformProperty.GetSizeBytes() 
			 + _velocityProperty.GetSizeBytes() 
			 + _viewYawProperty.GetSizeBytes() 
			 + _viewPitchProperty.GetSizeBytes();
	}
}
