using Godot;

public partial class CarInputState : RefCounted
{
	public int Tick { get; set; } = 0;
	public float Throttle { get; set; } = 0.0f;
	public float Steer { get; set; } = 0.0f;
	public bool Handbrake { get; set; } = false;
	public bool Brake { get; set; } = false;
	public bool Respawn { get; set; } = false;

	public void CopyFrom(CarInputState other)
	{
		Tick = other.Tick;
		Throttle = other.Throttle;
		Steer = other.Steer;
		Handbrake = other.Handbrake;
		Brake = other.Brake;
		Respawn = other.Respawn;
	}

	public void Reset()
	{
		Tick = 0;
		Throttle = 0.0f;
		Steer = 0.0f;
		Handbrake = false;
		Brake = false;
		Respawn = false;
	}
}

