# Weapon System Plan

Data-driven, network-safe weapon architecture that supports world pickups and player inventory, keeps all assets per-weapon in `src/entities/weapon/<weapon_name>`, and avoids hitscan by using projectiles and short ray probes only for close-range melee validation.

## Goals and Principles
- One definition file per weapon drives behavior (no hard-coded branching).
- Server authoritative for damage/ammo; clients may predict animations/FX.
- Weapons exist both as in-world interactables and as equipped inventory items.
- All per-weapon assets (models, projectile scenes, sounds, scripts) live together.
- Modular: recoil, attachments, and projectiles are pluggable resources.

## Directory Layout
- `src/entities/weapon/_shared/` — base scripts/resources (definitions, inventory, controller, recoil, attachment, pickup, projectile pool).
- `src/entities/weapon/<weapon>/` — per-weapon assets:
  - `weapon_definition.tres` (WeaponDefinition resource)
  - `world.tscn` (pickup in the world)
  - `view.tscn` (first-person/third-person view model)
  - `projectile.tscn` + script (e.g., `RocketProjectile.cs`)
  - `fire.anim`, `reload.anim`, audio, textures, models
  - Optional: `attachments/<name>.tres` and meshes/materials
- `docs/weapons/` — design notes or balancing sheets (optional, mirrors `weapon_plan.md` details per weapon).

## Core Data Types (Resources)
- `WeaponDefinition`:
  - `WeaponType Id`
  - `string DisplayName`
  - `PackedScene WorldScene`, `PackedScene ViewScene`
  - `PackedScene ProjectileScene`
  - `Transform3D ProjectileSpawn` (offset/origin in weapon space)
  - `int MagazineSize`
  - `int MaxReserveAmmo`
  - `float FireCooldownSec`
  - `float ReloadDurationSec`
  - `float Damage` (base per projectile; explosion radius handled by projectile)
  - `RecoilProfile Recoil`
  - `AttachmentSlotDefinition[] AttachmentSlots`
  - `AudioSet FireAudio`, `AudioSet ReloadAudio`, `AudioSet DryFireAudio`
  - `MuzzleFxSet MuzzleFx`
  - `float EquipTimeSec`, `float UnequipTimeSec`
  - Flags: `bool IsAutomatic`, `bool ConsumeAmmoPerShot`, `bool AllowHoldToFire`
- `ProjectileDefinition` (for projectiles that need tuning separately):
  - `float Speed`, `float GravityScale`, `float LifetimeSec`
  - `float Radius` (for collision sweeps), `float ExplosionRadius` (if applicable)
  - `float SelfDamageScale`, `float Knockback`
  - `AudioSet FlightAudio`, `AudioSet ImpactAudio`, `DecalSet ImpactDecal`
- `RecoilProfile`:
  - `Vector2 Kick` (pitch/yaw per shot), `float RecoveryRate`
  - `Curve SpreadCurve` (per-shot spread growth), `float MaxSpread`
  - Optional pattern list for deterministic recoil.
- `AttachmentDefinition` / `AttachmentSlotDefinition`:
  - Slot id (`Muzzle`, `Optic`, `Underbarrel`, `Skin`)
  - Allowed attachment ids; hooks to modify stats (multipliers/additive deltas).

## Runtime Objects
- `WeaponInstance`: Holds a `WeaponDefinition`, current ammo (mag + reserve), attachments, and timers; lives in player inventory and on pickups.
- `WeaponInventory`: Owned by `PlayerCharacter`; manages equipped slot, quick-swap, ammo consolidation, and exposes events for UI and animation (equip, fire, reload, dry fire).
- `WeaponController`: Per-player component that reads input (only when weapons enabled), owns fire/reload state machine, and talks to network authority to request actions.
- `WeaponPickup` (world): Scene with collision/interaction prompt; carries a `WeaponInstance` snapshot; on pickup merges ammo or swaps weapon; optionally respawns via a `WeaponSpawner`.
- `ProjectilePool`: Pool per projectile type to reuse nodes (already present for rockets; generalize to all).
- `AttachmentBehaviour`: Optional node/script to apply stat changes or visuals when attachment is equipped.

## Network and Authority
- Server authoritative for firing, ammo, damage, reload completion, and projectile spawning.
- Client prediction:
  - Local: start fire/reload animations and spawn client-visual projectile with predicted trajectory; server confirmation snaps ammo and projectile if different.
  - Remote: receive replicated fire events (weapon id, shot number, seed) to drive muzzle FX and audio.
- Replication hooks:
  - `WeaponInventory` implements lightweight replication: equipped weapon id, mag count, reserve count, reload state, and fire sequence number.
  - Projectiles spawned server-side broadcast spawn packet (id, owner id, position, velocity, definition id); clients spawn visual proxy and update via occasional transforms if needed (rockets already interpolate).
- Respect `GameModeManager.WeaponsEnabled`; controller ignores fire/reload when disabled.

## Interaction and States
- States: `Idle`, `FiringCooldown`, `Reloading`, `Equipping`, `Inspect`, `SprintingLocked` (optional). State machine lives in `WeaponController`.
- Input flow:
  1) Input → controller checks state, ammo, cooldown, and game-mode enable.
  2) Client sends `FireRequest(weaponId, aimTransform, fireSeq)` to server; local plays anim/sfx immediately.
  3) Server validates (cooldown, ammo, line-of-fire clearance), decrements ammo, spawns projectile, increments fire seq, and replicates to clients.
  4) Reload similar: `StartReload` with animation length from definition; server clamps ammo and updates inventory on completion.
- Melee (future): short capsule sweep, uses `ProjectileDefinition` for hit FX and knockback with near-zero flight time.

## Visuals, Audio, and UI
- `ViewScene` contains camera-relative weapon mesh, muzzle socket, hand IK targets, and anim player. `WorldScene` is the pickup mesh with collision.
- Use `AudioSettingsManager.WeaponsBusName` for all weapon audio.
- UI hooks:
  - `AmmoUI.UpdateAmmo(weaponType, mag, magSize, reloading, reloadMsRemaining)`
  - Hit markers use `WeaponType` to scale damage feedback (already present).
  - Interaction prompt when near a `WeaponPickup`.
- Recoil/spread applied to `PlayerLookController` yaw/pitch; spread affects projectile launch direction.

## Rocket Launcher (First Weapon)
- Definition location: `src/entities/weapon/rocket_launcher/weapon_definition.tres`.
- Stats:
  - `WeaponType.RocketLauncher`
  - Magazine: 1, Reserve: 14 (total 15 rockets at spawn)
  - `FireCooldownSec = 1.0f` (max 1 shot/sec)
  - `ReloadDurationSec = 1.2f` (single rocket reload)
  - Projectile: existing `rocket_projectile.tscn` (speed ~30 m/s), explosion radius 6.0, arm delay configurable (`ArmDelaySec` already in script).
  - Damage: 100 direct, 120 radial falloff peak (tune in `RocketProjectile` or damage handler).
  - Recoil: pitch kick 3–4 degrees per shot, slow recovery.
  - Audio: reuse `rocket_loop.mp3` (flight) and `explosion.mp3`; add `firing.mp3` for launch burst.
  - Visual: muzzle flash sprite + smoke burst on fire; trail already present on projectile.
- Pickup: `world.tscn` with collision + highlight; grants launcher with 15 ammo if player lacks one, otherwise top-off up to reserve cap.

## Implementation Sequence (MVP → polish)
1) **Shared scaffolding**: Add `_shared` scripts/resources (`WeaponDefinition`, `ProjectileDefinition`, `WeaponInstance`, `WeaponInventory`, `WeaponController`, `WeaponPickup`, `ProjectilePool`, `RecoilProfile`).
2) **Player wiring**: Attach `WeaponInventory`/`WeaponController` to `PlayerCharacter`; expose signals for UI; gate by `GameModeManager.WeaponsEnabled`.
3) **Networking**: Add request/confirm RPCs for fire/reload; replicate ammo + fire sequence; integrate with existing `EntityReplicationRegistry` or dedicated RPC channel.
4) **Rocket launcher**: Author `weapon_definition.tres`, hook existing `rocket_projectile.tscn`; build `view.tscn`/`world.tscn`; set initial ammo to 15, 1 shot/sec.
5) **UI/Audio**: Drive `AmmoUI` from inventory events; route audio through Weapons bus; add muzzle FX.
6) **Pooling**: Generalize rocket pool to `ProjectilePool`; add per-weapon pool config.
7) **Attachments (optional)**: Implement slot system and a sample cosmetic skin + optic modifier to prove pipeline.

## Extensibility Notes
- New weapon steps: duplicate folder, author model/animations, create `weapon_definition.tres`, set projectile scene, and register `WeaponType`.
- Attachments modify stats via multipliers (e.g., optic reduces `MaxSpread`, muzzle increases `ExplosionRadius` but reduces `Speed`).
- Future melee: use `ProjectileDefinition` with near-zero lifetime and server-side sweep to keep one system.
