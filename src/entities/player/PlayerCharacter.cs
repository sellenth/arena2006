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
	[Export] public int MaxHealth { get; set; } = 100;
	[Export] public int MaxArmor { get; set; } = 100;
	public long OwnerPeerId => GetOwnerPeerId();

	private Node3D _head;
	private Camera3D _camera;
	private MeshInstance3D _mesh;
	private CollisionShape3D _collisionShape;
	private NetworkController _networkController;
	private int _health = 100;
	private int _armor = 100;

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
	private ReplicatedInt _healthProperty;
	private ReplicatedInt _armorProperty;
	private ReplicatedInt _weaponIdProperty;
	private ReplicatedInt _weaponMagProperty;
	private ReplicatedInt _weaponReserveProperty;
	private ReplicatedInt _weaponFireSeqProperty;
	private ReplicatedInt _weaponReloadMsProperty;
	private ReplicatedInt _weaponReloadingProperty;
	public int Health => _health;
	public int Armor => _armor;
	private WeaponInventory _weaponInventory;
	private WeaponController _weaponController;
	private AmmoUI _ammoUi;
	private int _repWeaponId = (int)WeaponType.None;
	private int _repWeaponMag = 0;
	private int _repWeaponReserve = 0;
	private int _repWeaponFireSeq = 0;
	private int _repWeaponReloadMs = 0;
	private int _repWeaponReloading = 0;

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

		ClampVitals();

		if (AutoRegisterWithNetwork)
		{
			_networkController = GetNodeOrNull<NetworkController>("/root/NetworkController");
			if (_networkController != null && _networkController.IsClient)
			{
				if (NetworkId != 0)
					RegisterAsRemoteReplica();
				_networkController.RegisterPlayerCharacter(this);
				SetCameraActive(true);
			}
		}

		_managesMouseMode = AutoRegisterWithNetwork && (_networkController == null || _networkController.IsClient);

		ApplyColor(_playerColor);

		EnsureWeaponSystems();
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

		_healthProperty = new ReplicatedInt(
			"Health",
			() => _health,
			value => SetHealth(value),
			ReplicationMode.OnChange
		);

			_armorProperty = new ReplicatedInt(
				"Armor",
				() => _armor,
				value => SetArmor(value),
				ReplicationMode.OnChange
			);

			_weaponIdProperty = new ReplicatedInt(
				"WeaponId",
				() => (int)GetEquippedWeaponId(),
				value => _repWeaponId = value,
				ReplicationMode.OnChange
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
			PrimaryFire = InputMap.HasAction("fire") && Input.IsActionPressed("fire"),
			PrimaryFireJustPressed = InputMap.HasAction("fire") && Input.IsActionJustPressed("fire"),
			Reload = (InputMap.HasAction("reload") && Input.IsActionJustPressed("reload")) || Input.IsKeyPressed(Key.R),
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

	private void InitializeControllerModules()
	{
		_movementController = new PlayerMovementController();

		var initialYaw = _head != null ? _head.Rotation.Y : Rotation.Y;
		var initialPitch = _camera != null ? _camera.Rotation.X : 0f;
		_lookController.Initialize(_head, _camera, initialYaw, initialPitch);
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

	private void SetHealth(int value)
	{
		_health = Mathf.Clamp(value, 0, MaxHealth);
	}

	private void SetArmor(int value)
	{
		_armor = Mathf.Clamp(value, 0, MaxArmor);
	}

	private void ClampVitals()
	{
		SetHealth(_health);
		SetArmor(_armor);
	}

	public void ApplyDamage(int amount)
	{
		if (amount <= 0)
			return;

		var remaining = amount;
		if (_armor > 0)
		{
			var armorDamage = Mathf.Min(_armor, remaining);
			SetArmor(_armor - armorDamage);
			remaining -= armorDamage;
		}

		if (remaining > 0)
		{
			SetHealth(_health - remaining);
		}
	}

	public void ApplyExternalImpulse(Vector3 impulse)
	{
		if (impulse == Vector3.Zero)
			return;

		Velocity += impulse;
		_movementController?.Reset();
	}

	public void ApplyLaunchVelocity(Vector3 velocity)
	{
		Velocity += velocity;
		_movementController?.Reset();
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
