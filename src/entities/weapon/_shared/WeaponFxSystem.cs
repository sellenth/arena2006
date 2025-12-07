using Godot;

/// <summary>
/// Handles weapon audio and visual effects (muzzle flashes, smoke, etc.).
/// </summary>
public partial class WeaponFxSystem : Node
{
	private PlayerCharacter _player;

	public void Initialize(PlayerCharacter player)
	{
		_player = player;
	}

	public void SpawnMuzzleFx(WeaponDefinition def)
	{
		if (def?.MuzzleFx == null || _player == null)
			return;

		TryGetMuzzleTransform(out var muzzleTransform, out var muzzleNode);
		if (muzzleTransform == Transform3D.Identity)
		{
			muzzleTransform = ResolveFallbackMuzzleTransform(def);
		}

		var parent = (Node)muzzleNode ?? _player;
		def.MuzzleFx.Spawn(parent, muzzleTransform);
	}

	public void PlayAudio(WeaponAudioSet set, Vector3 position)
	{
		if (set == null || set.Stream == null)
			return;

		if (set.Spatial)
		{
			var spatial = set.Create3D(this, position);
			if (spatial == null)
				return;

			spatial.Bus = AudioSettingsManager.WeaponsBusName;
			spatial.PitchScale = (float)GD.RandRange(set.RandomPitchMin, set.RandomPitchMax);
			spatial.Play();
			spatial.Finished += () =>
			{
				if (IsInstanceValid(spatial)) spatial.QueueFree();
			};
			return;
		}

		var flat = new AudioStreamPlayer
		{
			Stream = set.Stream,
			VolumeDb = set.VolumeDb,
			PitchScale = (float)GD.RandRange(set.RandomPitchMin, set.RandomPitchMax)
		};

		AddChild(flat);
		flat.Bus = AudioSettingsManager.WeaponsBusName;
		flat.Play();
		flat.Finished += () =>
		{
			if (IsInstanceValid(flat)) flat.QueueFree();
		};
	}

	public bool TryGetMuzzleTransform(out Transform3D transform, out Node3D muzzleNode)
	{
		transform = Transform3D.Identity;
		muzzleNode = null;
		if (_player == null)
			return false;

		var weaponView = _player.GetNodeOrNull<WeaponView>("WeaponView") ?? _player.FindChild("WeaponView", recursive: true, owned: false) as WeaponView;
		if (weaponView == null)
			return false;

		var currentView = weaponView.CurrentView;
		if (currentView == null)
			return false;

		muzzleNode = currentView.GetNodeOrNull<Node3D>("Muzzle")
			?? currentView.FindChild("Muzzle", recursive: true, owned: false) as Node3D;
		if (muzzleNode == null)
			return false;

		transform = muzzleNode.GlobalTransform;
		return true;
	}

	private Transform3D ResolveFallbackMuzzleTransform(WeaponDefinition def)
	{
		if (_player == null)
			return Transform3D.Identity;

		var viewDir = _player.GetViewDirection().Normalized();
		if (viewDir.IsZeroApprox())
		{
			viewDir = -_player.GlobalTransform.Basis.Z;
		}

		var basis = Basis.LookingAt(viewDir, Vector3.Up);

		const float ForwardOffset = 0.3f;
		const float VerticalOffset = 0.9f;
		var origin = _player.GlobalTransform.Origin + (viewDir * ForwardOffset) + (Vector3.Up * VerticalOffset);

		var spawn = def.ProjectileSpawn;
		return new Transform3D(basis, origin) * spawn;
	}
}
