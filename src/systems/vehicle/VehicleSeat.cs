using Godot;

public partial class VehicleSeat : Node3D
{
	[Export] public string SeatId { get; set; } = "driver";
	[Export] public bool IsDriverSeat { get; set; } = true;
	[Export] public Vector3 ExitOffset { get; set; } = new Vector3(0, 0, 0);
	[Export] public float InteractionRadius { get; set; } = 2.5f;
	[Export] public NodePath VehicleRootPath { get; set; } = new NodePath();

	public Node3D VehicleRoot { get; private set; }

	public override void _Ready()
	{
		VehicleRoot = ResolveVehicleRoot();
		AddToGroup("vehicle_seats");
	}

	private Node3D ResolveVehicleRoot()
	{
		if (!VehicleRootPath.IsEmpty)
		{
			return GetNodeOrNull<Node3D>(VehicleRootPath);
		}

		var node = GetParent() as Node3D;
		return node ?? this;
	}

	public Vector3 GetSeatPosition()
	{
		return GlobalTransform.Origin;
	}

	public Vector3 GetExitPosition()
	{
		return GlobalTransform.Origin + GlobalTransform.Basis * ExitOffset;
	}
}
