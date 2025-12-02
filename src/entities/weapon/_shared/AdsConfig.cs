using Godot;

public enum AdsMode
{
	None = 0,
	Ironsight = 1,
	RedDot = 2,
	Scope = 3
}

public partial class AdsConfig : Resource
{
	[Export] public AdsMode Mode { get; set; } = AdsMode.None;

	[ExportGroup("Timing")]
	[Export] public float EnterTimeSec { get; set; } = 0.15f;
	[Export] public float ExitTimeSec { get; set; } = 0.12f;

	[ExportGroup("Camera & Handling")]
	[Export] public float TargetFov { get; set; } = 55f;
	[Export] public float SensitivityScale { get; set; } = 0.85f;
	[Export] public float MoveSpeedScale { get; set; } = 0.85f;

	[ExportGroup("Spread & Recoil Scaling")]
	[Export] public float HipSpreadDegrees { get; set; } = 2.5f;
	[Export] public float AdsSpreadDegrees { get; set; } = 1.0f;
	[Export] public float HipRecoilScale { get; set; } = 1.0f;
	[Export] public float AdsRecoilScale { get; set; } = 0.7f;

	[ExportGroup("Viewmodel")]
	[Export] public Transform3D ViewOffset { get; set; } = Transform3D.Identity;

	[ExportGroup("Scope Overlay")]
	[Export] public Texture2D? ScopeOverlay { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float ScopeOverlayOpacity { get; set; } = 1.0f;
}
