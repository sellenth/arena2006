using Godot;

public partial class FootInputState : RefCounted
{
	public int Tick { get; set; }
	public Vector2 MoveInput { get; set; } = Vector2.Zero;
	public bool Jump { get; set; }
	public Vector2 LookDelta { get; set; } = Vector2.Zero;
	public bool Interact { get; set; }

	public void CopyFrom(FootInputState other)
	{
		Tick = other.Tick;
		MoveInput = other.MoveInput;
		Jump = other.Jump;
		LookDelta = other.LookDelta;
		Interact = other.Interact;
	}

	public void Reset()
	{
		Tick = 0;
		MoveInput = Vector2.Zero;
		Jump = false;
		LookDelta = Vector2.Zero;
		Interact = false;
	}
}
