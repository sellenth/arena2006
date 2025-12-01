using System.Diagnostics;
using Godot;

public partial class PlayerCharacter : CharacterBody3D, IReplicatedEntity
{
	[Export] public NodePath HeadPath { get; set; } = "Head";
	[Export] public NodePath CameraPath { get; set; } = "Head/Cam";
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";
	[Export] public NodePath AnimationPlayerPath { get; set; } = "Body/AnimationPlayer";
	[Export] public NodePath CollisionShapePath { get; set; } = "Collision";
	[Export] public bool AutoRegisterWithNetwork { get; set; } = true;
	[Export] public int NetworkId { get; set; } = 0;
	[Export] public int MaxHealth { get; set; } = 100;
	[Export] public int MaxArmor { get; set; } = 100;
	[Export] public float StandingHeight { get; set; } = 1.8f;
	[Export] public float CrouchHeight { get; set; } = 1.3f;
	[Export] public float CrouchTransitionSpeed { get; set; } = 10f;
	public long OwnerPeerId => GetOwnerPeerId();

	private Node3D _head;
	private Camera3D _camera;
	private MeshInstance3D _mesh;
	private CollisionShape3D _collisionShape;
	private NetworkController _networkController;
	private readonly PlayerInputState _inputState = new PlayerInputState();
	private readonly PlayerLookController _lookController = new PlayerLookController();
	private PlayerMovementComponent _movementComponent;
	private HealthComponent _healthComponent;
	private readonly PlayerReconciliationController _reconciliation = new PlayerReconciliationController();
	private readonly PlayerAnimationController _animationController = new PlayerAnimationController();
	private Color _playerColor = Colors.Red;
	private bool _isAuthority = true;
	private bool _simulateLocally = true;
	private bool _cameraActive = false;
	private bool _worldActive = true;
	private bool _managesMouseMode = false;
	private bool _registeredWithRemoteManager = false;
	private bool _hasPendingReplicatedTransform = false;
	private Transform3D _pendingReplicatedTransform = Transform3D.Identity;
	private int _replicatedMode = (int)PlayerMode.Foot;
	private int _replicatedVehicleId = 0;

	private ReplicatedTransform3D _transformProperty;
	private ReplicatedVector3 _velocityProperty;
	private ReplicatedFloat _viewYawProperty;
	private ReplicatedFloat _viewPitchProperty;
	private ReplicatedInt _modeProperty;
	private ReplicatedInt _vehicleIdProperty;
	private ReplicatedInt _movementStateProperty;
	private ReplicatedInt _healthProperty;
	private ReplicatedInt _armorProperty;
	private ReplicatedInt _weaponIdProperty;
	private ReplicatedInt _weaponMagProperty;
	private ReplicatedInt _weaponReserveProperty;
	private ReplicatedInt _weaponFireSeqProperty;
	private ReplicatedInt _weaponReloadMsProperty;
	private ReplicatedInt _weaponReloadingProperty;
	public int Health => _healthComponent?.Health ?? MaxHealth;
	public int Armor => _healthComponent?.Armor ?? MaxArmor;
	private WeaponInventory _weaponInventory;
	private WeaponController _weaponController;
	private AmmoUI _ammoUi;
	private int _repWeaponId = (int)WeaponType.None;
	private int _repWeaponMag = 0;
	private int _repWeaponReserve = 0;
	private int _repWeaponFireSeq = 0;
	private int _repWeaponReloadMs = 0;
	private int _repWeaponReloading = 0;
	private int _replicatedMovementState = (int)PlayerMovementStateKind.Grounded;

	public PlayerCharacter()
	{
		// CharacterBody defaults for surf-style movement
		FloorStopOnSlope = false;
		FloorBlockOnWall = false;
		WallMinSlideAngle = 0f;
	}

	public override void _Ready()
	{
		_head = GetNodeOrNull<Node3D>(HeadPath);
		_camera = GetNodeOrNull<Camera3D>(CameraPath);
		_mesh = GetNodeOrNull<MeshInstance3D>(MeshPath);
		_collisionShape = GetNodeOrNull<CollisionShape3D>(CollisionShapePath);
		_networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
		_animationController.Initialize(this, AnimationPlayerPath);

		if (_head == null || _camera == null)
		{
			Debug.Assert(false, "head or camera not found :o");
		}

		InitializeMovementModules();
		InitializeHealthComponent();

		InitializeReplication();

		if (AutoRegisterWithNetwork)
		{
			if (_networkController != null && _networkController.IsClient)
			{
				if (NetworkId != 0)
					RegisterAsRemoteReplica();
				_networkController.RegisterPlayerCharacter(this);
				SetCameraActive(true);
			}
		}

		RefreshMouseModeManagement();

		ApplyColor(_playerColor);

		EnsureWeaponSystems();
	}

	private void RefreshMouseModeManagement()
	{
		if (_networkController == null && IsInsideTree())
		{
			_networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
		}

		_managesMouseMode = AutoRegisterWithNetwork && (_networkController == null || _networkController.IsClient);
	}

	private void EnsureWeaponSystems()
	{
		_weaponInventory = GetNodeOrNull<WeaponInventory>("WeaponInventory");
		if (_weaponInventory == null)
		{
			_weaponInventory = new WeaponInventory { Name = "WeaponInventory" };
			AddChild(_weaponInventory);
		}

		if (_weaponInventory.StartingWeapons.Count == 0)
		{
			var rocketDefPath = "res://src/entities/weapon/rocket_launcher/weapon_definition.tres";
			if (ResourceLoader.Exists(rocketDefPath))
			{
				var rocketDef = ResourceLoader.Load<WeaponDefinition>(rocketDefPath);
				if (rocketDef != null)
				{
					_weaponInventory.StartingWeapons.Add(rocketDef);
				}
			}
		}

		_weaponController = GetNodeOrNull<WeaponController>("WeaponController");
		if (_weaponController == null)
		{
			_weaponController = new WeaponController { Name = "WeaponController" };
			AddChild(_weaponController);
		}

		ConnectAmmoUi();
	}

	private void ConnectAmmoUi()
	{
		if (_weaponInventory == null)
			return;

		if (_ammoUi == null)
		{
			_ammoUi = FindAmmoUi();
		}

		if (_ammoUi != null)
		{
			_weaponInventory.AmmoChanged -= _ammoUi.UpdateAmmo;
			_weaponInventory.AmmoChanged += _ammoUi.UpdateAmmo;
			_weaponInventory.EmitAmmo();
		}
	}

	private AmmoUI FindAmmoUi()
	{
		var scene = GetTree()?.CurrentScene;
		if (scene == null)
			return null;
		// Search by name to avoid hard-coded paths.
		var node = scene.FindChild("AmmoUI", recursive: true, owned: false);
		return node as AmmoUI;
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

		_modeProperty = new ReplicatedInt(
			"PlayerMode",
			() => _replicatedMode,
			value => ApplyReplicatedMode((PlayerMode)value),
			ReplicationMode.OnChange
		);

		_vehicleIdProperty = new ReplicatedInt(
			"VehicleId",
			() => _replicatedVehicleId,
			value => _replicatedVehicleId = value,
			ReplicationMode.OnChange
		);

		_movementStateProperty = new ReplicatedInt(
			"MovementState",
			() => (int)(_movementComponent?.State ?? PlayerMovementStateKind.Grounded),
			value => _replicatedMovementState = value,
			ReplicationMode.OnChange
		);

		_healthProperty = new ReplicatedInt(
			"Health",
			() => Health,
			value => _healthComponent?.SetHealthFromReplication(value),
			ReplicationMode.OnChange
		);

		_armorProperty = new ReplicatedInt(
			"Armor",
			() => Armor,
			value => _healthComponent?.SetArmorFromReplication(value),
			ReplicationMode.OnChange
		);

		_weaponIdProperty = new ReplicatedInt(
			"WeaponId",
			() => (int)GetEquippedWeaponId(),
			value => OnReplicatedWeaponId(value),
			ReplicationMode.Always
		);

		_weaponMagProperty = new ReplicatedInt(
			"WeaponMag",
			() => GetEquippedMagazine(),
			value => _repWeaponMag = value,
			ReplicationMode.Always
		);

		_weaponReserveProperty = new ReplicatedInt(
			"WeaponReserve",
			() => GetEquippedReserve(),
			value => _repWeaponReserve = value,
			ReplicationMode.Always
		);

		_weaponFireSeqProperty = new ReplicatedInt(
			"WeaponFireSeq",
			() => GetEquippedFireSequence(),
			value => OnReplicatedFireSequence(value),
			ReplicationMode.Always
		);

		_weaponReloadMsProperty = new ReplicatedInt(
			"WeaponReloadMs",
			() => GetEquippedReloadMs(),
			value => _repWeaponReloadMs = value,
			ReplicationMode.Always
		);

		_weaponReloadingProperty = new ReplicatedInt(
			"WeaponReloading",
			() => GetEquippedReloadingFlag(),
			value => _repWeaponReloading = value,
			ReplicationMode.Always
		);
	}

	public void RegisterAsAuthority()
	{
		if (_isAuthority && EntityReplicationRegistry.Instance != null)
		{
			NetworkId = EntityReplicationRegistry.Instance.RegisterEntity(this, this);
			GD.Print($"PlayerCharacter ({Name}): Registered as authority with NetworkId {NetworkId}");
		}
		else if (_isAuthority)
		{
			GD.PushWarning($"PlayerCharacter ({Name}): Cannot register authority, registry missing.");
		}
	}

	public void SetReplicatedMode(PlayerMode mode, int vehicleId)
	{
		_replicatedMode = (int)mode;
		_replicatedVehicleId = vehicleId;
	}

	public void SetNetworkId(int id)
	{
		if (NetworkId == id)
			return;

		if (_registeredWithRemoteManager)
		{
			var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
			if (remoteManager != null)
				remoteManager.UnregisterRemoteEntity(NetworkId);
			_registeredWithRemoteManager = false;
		}

		NetworkId = id;
	}

	public void RegisterAsRemoteReplica()
	{
		if (NetworkId == 0)
		{
			GD.PushWarning($"PlayerCharacter ({Name}): NetworkId is 0, cannot register remote replica.");
			return;
		}

		var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
		if (remoteManager != null)
		{
			if (!_registeredWithRemoteManager)
			{
				remoteManager.RegisterRemoteEntity(NetworkId, this);
				_registeredWithRemoteManager = true;
				GD.Print($"PlayerCharacter ({Name}): Registered as remote with NetworkId {NetworkId}");
			}
		}
		else
		{
			GD.PushWarning("PlayerCharacter: RemoteEntityManager not found in scene!");
		}
	}

	public override void _ExitTree()
	{
		if (_weaponInventory != null && _ammoUi != null)
		{
			_weaponInventory.AmmoChanged -= _ammoUi.UpdateAmmo;
		}

		if (_healthComponent != null)
		{
			_healthComponent.Died -= HandleDeath;
		}

		if (_registeredWithRemoteManager)
		{
			var remoteManager = GetTree().CurrentScene?.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
			remoteManager?.UnregisterRemoteEntity(NetworkId);
			_registeredWithRemoteManager = false;
		}
		base._ExitTree();
	}

	private void ApplyTransformFromReplication(Transform3D value)
	{
		_hasPendingReplicatedTransform = true;
		_pendingReplicatedTransform = value;
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

	private void ApplyReplicatedMode(PlayerMode mode)
	{
		_replicatedMode = (int)mode;
		if (_isAuthority)
			return;

		// Toggle visibility/physics based on replicated mode
		SetWorldActive(mode == PlayerMode.Foot);
	}

	public long GetOwnerPeerId()
	{
		if (NetworkId >= 3000 && NetworkId < 4000)
		{
			return NetworkId - 3000;
		}
		return 0;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_worldActive)
			return;

		var deltaFloat = (float)delta;

		if (_simulateLocally)
		{
			_movementComponent?.Step(_inputState, deltaFloat);

			if (_networkController != null && _networkController.IsClient)
			{
				_networkController.RecordLocalPlayerPrediction(_inputState.Tick, GlobalTransform, Velocity);
			}
		}
		var capsuleState = _isAuthority
			? _movementComponent?.State ?? PlayerMovementStateKind.Grounded
			: (PlayerMovementStateKind)_replicatedMovementState;
		_movementComponent?.UpdateCapsuleHeight(deltaFloat, capsuleState, _isAuthority);


		ApplySnapshotCorrection(deltaFloat);

		ApplyPendingLookInput();
		_lookController?.Update(deltaFloat, Velocity, IsOnFloor());
		UpdateAnimations();
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
			MoveInput = Input.GetVector("move_right", "move_left", "move_backward", "move_forward"),
			Jump = Input.IsActionPressed("jump"),
			Interact = Input.IsActionJustPressed("interact"),
			Crouch = InputMap.HasAction("crouch") && Input.IsActionPressed("crouch"),
			CrouchPressed = InputMap.HasAction("crouch") && Input.IsActionJustPressed("crouch"),
			Sprint = InputMap.HasAction("sprint") && Input.IsActionPressed("sprint"),
			PrimaryFire = InputMap.HasAction("fire") && Input.IsActionPressed("fire"),
			PrimaryFireJustPressed = InputMap.HasAction("fire") && Input.IsActionJustPressed("fire"),
			Reload = (InputMap.HasAction("reload") && Input.IsActionJustPressed("reload")) || Input.IsKeyPressed(Key.R),
			WeaponToggle = InputMap.HasAction("weapon_toggle") && Input.IsActionJustPressed("weapon_toggle"),
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

		_weaponController?.SetInput(_inputState);
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

	public bool HasAuthority()
	{
		return _isAuthority;
	}

	public void SetCameraActive(bool active)
	{
		RefreshMouseModeManagement();

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
		_weaponController?.SetPhysicsProcess(active);
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

	private void InitializeMovementModules()
	{
		_movementComponent = new PlayerMovementComponent(this);
		_movementComponent.ConfigureCollision(_collisionShape, _head, StandingHeight, CrouchHeight, CrouchTransitionSpeed);

		if (_collisionShape?.Shape is CapsuleShape3D capsule)
		{
			StandingHeight = capsule.Height;
		}

		var initialYaw = Rotation.Y;
		var initialPitch = _camera != null ? _camera.Rotation.X : 0f;
		_lookController.Initialize(this, _head, _camera, initialYaw, initialPitch);
	}

	private void InitializeHealthComponent()
	{
		_healthComponent = GetNodeOrNull<HealthComponent>("HealthComponent");
		if (_healthComponent == null)
		{
			_healthComponent = new HealthComponent { Name = "HealthComponent" };
			AddChild(_healthComponent);
		}

		_healthComponent.Initialize(MaxHealth, MaxArmor);
		_healthComponent.Died += HandleDeath;
	}

	private WeaponType GetEquippedWeaponId()
	{
		return _weaponInventory?.EquippedType ?? WeaponType.None;
	}

	private int GetEquippedMagazine()
	{
		return _weaponInventory?.Equipped?.Magazine ?? 0;
	}

	private int GetEquippedReserve()
	{
		return _weaponInventory?.Equipped?.Reserve ?? 0;
	}

	private int GetEquippedFireSequence()
	{
		return _weaponController?.GetFireSequence() ?? 0;
	}

	private int GetEquippedReloadMs()
	{
		if (_weaponInventory?.Equipped == null)
			return 0;
		return _weaponInventory.Equipped.IsReloading
			? Mathf.Max(0, (int)(_weaponInventory.Equipped.ReloadEndTimeMs - Time.GetTicksMsec()))
			: 0;
	}

	private int GetEquippedReloadingFlag()
	{
		return _weaponInventory?.Equipped?.IsReloading == true ? 1 : 0;
	}

	private void OnReplicatedWeaponId(int value)
	{
		_repWeaponId = value;
		if (_isAuthority)
		{
			_weaponIdProperty.MarkClean();
			return;
		}

		var type = (WeaponType)value;
		if (_weaponInventory != null && _weaponInventory.Equip(type))
		{
			_weaponInventory.EmitAmmo();
		}
		else
		{
			GD.PushWarning($"PlayerCharacter ({Name}): Missing weapon {type} locally, cannot show remote view.");
		}
	}

	private void OnReplicatedFireSequence(int value)
	{
		if (_isAuthority)
		{
			_weaponFireSeqProperty.MarkClean();
			return;
		}

		if (value != _repWeaponFireSeq)
		{
			var ownerPeerId = GetOwnerPeerId();
			_weaponController?.PlayRemoteFireFx((WeaponType)_repWeaponId);
			_weaponController?.SpawnRemoteProjectile((WeaponType)_repWeaponId, ownerPeerId, value);
		}

		_repWeaponFireSeq = value;
	}

	private void ApplySnapshotCorrection(float delta)
	{
		_reconciliation.Apply(this, _lookController, delta);
	}

	private void ApplyPendingLookInput()
	{
		_lookController?.ApplyQueuedLook();
	}

	public string GetMovementStateName()
	{
		if (_movementComponent == null)
			return "None";
		return _movementComponent.State.ToString();
	}

	private bool ShouldProcessDamage()
	{
		// In networked games the server is authoritative over damage.
		if (_networkController == null)
			return true;
		return _networkController.IsServer;
	}

	public void ApplyDamage(int amount, long instigatorPeerId = 0)
	{
		if (amount <= 0)
			return;

		if (_healthComponent == null)
			return;

		if (_healthComponent?.IsDead == true)
			return;

		if (!ShouldProcessDamage())
			return;

		_healthComponent?.ApplyDamage(amount, instigatorPeerId);
	}

	private void HandleDeath(long instigatorPeerId)
	{
		if (_healthComponent?.IsDead != true)
			return;

		Velocity = Vector3.Zero;
		_movementComponent?.Reset();
		SetWorldActive(false);

		if (_networkController != null && _networkController.IsServer)
		{
			_networkController.NotifyPlayerKilled(this, instigatorPeerId);
		}
		else
		{
			RespawnLocally();
		}
	}

	private void RespawnLocally()
	{
		var transform = GlobalTransform;
		transform.Origin += Vector3.Up * 1.5f;
		ForceRespawn(transform);
	}

	public void ForceRespawn(Transform3D transform)
	{
		Velocity = Vector3.Zero;
		_movementComponent?.Reset();
		SetWorldActive(true);
		_healthComponent?.ResetVitals();
		_animationController.ResetToIdle();
		if (RespawnManager.Instance != null)
		{
			RespawnManager.Instance.TeleportEntity(this, transform);
		}
		else
		{
			GlobalTransform = transform;
		}
	}

	private void UpdateAnimations()
	{
		var state = _isAuthority
			? _movementComponent?.State ?? PlayerMovementStateKind.Airborne
			: (PlayerMovementStateKind)_replicatedMovementState;
		_animationController.Update(Velocity, state, _healthComponent?.IsDead == true || Health <= 0);
	}

	public void ApplyExternalImpulse(Vector3 impulse)
	{
		if (impulse == Vector3.Zero)
			return;

		if (_movementComponent != null)
		{
			_movementComponent.ApplyImpulse(impulse);
		}
		else
		{
			Velocity += impulse;
		}
	}

	public void ApplyLaunchVelocity(Vector3 velocity)
	{
		if (_movementComponent != null)
		{
			_movementComponent.ApplyLaunchVelocity(velocity);
		}
		else
		{
			Velocity += velocity;
		}
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
		_modeProperty.Write(buffer);
		_vehicleIdProperty.Write(buffer);
		_movementStateProperty.Write(buffer);
		_healthProperty.Write(buffer);
		_armorProperty.Write(buffer);
		_weaponIdProperty.Write(buffer);
		_weaponMagProperty.Write(buffer);
		_weaponReserveProperty.Write(buffer);
		_weaponFireSeqProperty.Write(buffer);
		_weaponReloadMsProperty.Write(buffer);
		_weaponReloadingProperty.Write(buffer);
	}

	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		_transformProperty.Read(buffer);
		_velocityProperty.Read(buffer);
		_viewYawProperty.Read(buffer);
		_viewPitchProperty.Read(buffer);
		_modeProperty.Read(buffer);
		_vehicleIdProperty.Read(buffer);
		_movementStateProperty.Read(buffer);
		_healthProperty.Read(buffer);
		_armorProperty.Read(buffer);
		_weaponIdProperty.Read(buffer);
		_weaponMagProperty.Read(buffer);
		_weaponReserveProperty.Read(buffer);
		_weaponFireSeqProperty.Read(buffer);
		_weaponReloadMsProperty.Read(buffer);
		_weaponReloadingProperty.Read(buffer);

		if (_hasPendingReplicatedTransform)
		{
			var snapshot = new PlayerSnapshot
			{
				Transform = _pendingReplicatedTransform,
				Velocity = Velocity,
				ViewYaw = _lookController?.Yaw ?? 0f,
				ViewPitch = _lookController?.Pitch ?? 0f
			};
			QueueSnapshot(snapshot);
			_hasPendingReplicatedTransform = false;
		}
	}

	public int GetSnapshotSizeBytes()
	{
		return _transformProperty.GetSizeBytes()
			 + _velocityProperty.GetSizeBytes()
			 + _viewYawProperty.GetSizeBytes()
			 + _viewPitchProperty.GetSizeBytes()
			 + _modeProperty.GetSizeBytes()
			 + _vehicleIdProperty.GetSizeBytes()
			 + _movementStateProperty.GetSizeBytes()
			 + _healthProperty.GetSizeBytes()
			 + _armorProperty.GetSizeBytes()
			 + _weaponIdProperty.GetSizeBytes()
			 + _weaponMagProperty.GetSizeBytes()
			 + _weaponReserveProperty.GetSizeBytes()
			 + _weaponFireSeqProperty.GetSizeBytes()
			 + _weaponReloadMsProperty.GetSizeBytes()
			 + _weaponReloadingProperty.GetSizeBytes();
	}
}
