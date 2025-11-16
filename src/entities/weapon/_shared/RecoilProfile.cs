using Godot;

public partial class RecoilProfile : Resource
{
	[Export] public Vector2 Kick { get; set; } = new Vector2(2.5f, 0.8f);
	[Export] public float RecoveryRate { get; set; } = 10.0f;
	[Export] public Curve SpreadCurve { get; set; }
	[Export] public float MaxSpreadDegrees { get; set; } = 6.0f;
	[Export] public Godot.Collections.Array<Vector2> Pattern { get; set; } = new();

	public float EvaluateSpread(int shotIndex)
	{
		if (SpreadCurve == null)
		{
			return MaxSpreadDegrees;
		}

		var t = Mathf.Clamp(shotIndex <= 0 ? 0f : shotIndex / 10.0f, 0f, 1f);
		return Mathf.Clamp(SpreadCurve.Sample(t), 0f, MaxSpreadDegrees);
	}
}
