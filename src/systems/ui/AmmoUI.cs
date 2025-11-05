using Godot;
using System;

public partial class AmmoUI : Control
{
    private Label? _weaponLabel;
    private Label? _clipLabel;
    private Label? _reloadLabel;

    public override void _Ready()
    {
        _weaponLabel = GetNodeOrNull<Label>("WeaponLabel");
        _clipLabel = GetNodeOrNull<Label>("ClipLabel");
        _reloadLabel = GetNodeOrNull<Label>("ReloadLabel");
        UpdateAmmo(WeaponType.RocketLauncher, 0, 0, false, 0.0);
    }

    public void UpdateAmmo(WeaponType weapon, int clip, int clipCapacity, bool reloading, double reloadMs)
    {
        if (_weaponLabel != null)
        {
            _weaponLabel.Text = weapon switch
            {
                WeaponType.MachineGun => "Machine Gun",
                WeaponType.RocketLauncher => "Rocket Launcher",
                _ => weapon.ToString(),
            };
        }

        if (_clipLabel != null)
        {
            _clipLabel.Text = $"{Math.Max(0, clip)}/{Math.Max(0, clipCapacity)}";
            _clipLabel.Modulate = reloading ? new Color(0.65f, 0.65f, 0.65f, 1f) : Colors.White;
        }

        if (_reloadLabel != null)
        {
            if (reloading)
            {
                double seconds = Math.Max(0.0, reloadMs / 1000.0);
                _reloadLabel.Visible = true;
                _reloadLabel.Text = seconds >= 0.1 ? $"Reloading {seconds:0.0}s" : "Reloading...";
            }
            else
            {
                _reloadLabel.Visible = false;
            }
        }
    }
}
