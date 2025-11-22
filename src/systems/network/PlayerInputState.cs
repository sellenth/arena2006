using Godot;

public partial class PlayerInputState : RefCounted
{
        public int Tick { get; set; }
        public Vector2 MoveInput { get; set; } = Vector2.Zero;
        public bool Jump { get; set; }
        public bool Sprint { get; set; }
        public bool Crouch { get; set; }
        public bool PrimaryFire { get; set; }
        public bool PrimaryFireJustPressed { get; set; }
        public bool Reload { get; set; }
	public float ViewYaw { get; set; } = float.NaN;
	public float ViewPitch { get; set; } = float.NaN;
	public bool Interact { get; set; }

	public void CopyFrom(PlayerInputState other)
	{
                Tick = other.Tick;
                MoveInput = other.MoveInput;
                Jump = other.Jump;
                Sprint = other.Sprint;
                Crouch = other.Crouch;
                PrimaryFire = other.PrimaryFire;
                PrimaryFireJustPressed = other.PrimaryFireJustPressed;
                Reload = other.Reload;
		ViewYaw = other.ViewYaw;
		ViewPitch = other.ViewPitch;
		Interact = other.Interact;
	}

	public void Reset()
	{
                Tick = 0;
                MoveInput = Vector2.Zero;
                Jump = false;
                Sprint = false;
                Crouch = false;
                PrimaryFire = false;
                PrimaryFireJustPressed = false;
                Reload = false;
		ViewYaw = float.NaN;
		ViewPitch = float.NaN;
		Interact = false;
	}
}
