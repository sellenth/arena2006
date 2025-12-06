using Godot;

public partial class HealthBarUI : Control
{
	private ProgressBar _healthBar;
	
	public override void _Ready()
	{
		// Find child nodes
		_healthBar = GetNode<ProgressBar>("HealthBar");
		
		// Set initial values
		_healthBar.MinValue = 0;
		_healthBar.MaxValue = 100;
		_healthBar.Value = 100;
		
		UpdateHealthDisplay(100, 100);
	}
	
	public void UpdateHealth(float currentHealth, float maxHealth)
	{
		UpdateHealthDisplay(currentHealth, maxHealth);
	}
	
	private void UpdateHealthDisplay(float currentHealth, float maxHealth)
	{
		if (_healthBar != null)
		{
			_healthBar.MaxValue = maxHealth;
			_healthBar.Value = currentHealth;
		}
	}
}