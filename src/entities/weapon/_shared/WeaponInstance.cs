using Godot;
using System;
using System.Collections.Generic;

public partial class WeaponInstance : RefCounted
{
	public WeaponDefinition Definition { get; }
	public int Magazine { get; private set; }
	public int Reserve { get; private set; }
	public bool IsReloading { get; private set; }
	public double ReloadEndTimeMs { get; private set; }
	public IReadOnlyDictionary<AttachmentSlot, AttachmentDefinition> Attachments => _attachments;

	private readonly Dictionary<AttachmentSlot, AttachmentDefinition> _attachments = new();

	public WeaponInstance(WeaponDefinition definition, int initialMagazine = -1, int initialReserve = -1)
	{
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		Magazine = initialMagazine >= 0 ? Mathf.Clamp(initialMagazine, 0, definition.MagazineSize) : definition.MagazineSize;
		Reserve = initialReserve >= 0 ? Mathf.Clamp(initialReserve, 0, definition.MaxReserveAmmo) : definition.MaxReserveAmmo;
	}

	public bool HasAmmoInMagazine => Magazine > 0;
	public bool CanReload => Definition != null && Magazine < Definition.MagazineSize && Reserve > 0 && !IsReloading;

	public void AddAmmo(int amount)
	{
		if (amount <= 0 || Definition == null)
			return;
		Reserve = Mathf.Clamp(Reserve + amount, 0, Definition.MaxReserveAmmo);
	}

	public bool ConsumeRound()
	{
		if (!Definition.ConsumeAmmoPerShot)
			return true;
		if (Magazine <= 0)
			return false;
		Magazine = Mathf.Max(0, Magazine - 1);
		return true;
	}

	public int ReloadFromReserve()
	{
		if (!CanReload || Definition == null)
			return 0;
		var missing = Mathf.Max(Definition.MagazineSize - Magazine, 0);
		var moved = Mathf.Clamp(missing, 0, Reserve);
		Magazine += moved;
		Reserve -= moved;
		return moved;
	}

	public void BeginReload(double nowMs, double durationMs)
	{
		IsReloading = true;
		ReloadEndTimeMs = nowMs + durationMs;
	}

	public void CompleteReload()
	{
		if (Definition == null)
		{
			IsReloading = false;
			return;
		}

		ReloadFromReserve();
		IsReloading = false;
		ReloadEndTimeMs = 0;
	}

	public void CancelReload()
	{
		IsReloading = false;
		ReloadEndTimeMs = 0;
	}

	public void SetAttachment(AttachmentDefinition attachment)
	{
		if (attachment == null)
			return;
		_attachments[attachment.Slot] = attachment;
	}
}
