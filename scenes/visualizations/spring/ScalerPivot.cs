using Godot;

[Tool]
public partial class ScalerPivot : Marker3D
{
	private Vector3 _startPos = Vector3.Zero;
	private MeshInstance3D _wheelMesh;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			_wheelMesh = GetNode<MeshInstance3D>("%WheelMesh");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint()) return;
		if (_wheelMesh == null) return;

		var diff = 1 - (_wheelMesh.GlobalPosition.Y - _startPos.Y);
		Scale = new Vector3(Scale.X, diff, Scale.Z);
	}
}

