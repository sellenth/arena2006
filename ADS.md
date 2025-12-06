## ADS (Aim Down Sights) Task List

High-level goals:
- Add an ADS-capable input pipeline (client input → network → player state) that supports Aim hold.
- Drive camera FOV/sensitivity changes and viewmodel pose blending for ADS-capable weapons.
- Support multiple sight modes (none/ironsight/red-dot/scope) with hip vs ADS spread/recoil differences.
- Provide a CoD4-style full-screen scope overlay (no PiP glass) and hide/adjust the crosshair while aiming.

### Input & Networking
- [ ] Add `Aim` action to input map (RMB) and propagate a bool through `PlayerInputState`, `NetworkSerializer`, and `ServerNetworkManager`.
- [ ] Add an ADS replicated flag/int in `PlayerCharacter` snapshots so remote proxies can pose weapons correctly.

### Data Model
- [ ] Create an `AdsConfig` resource (or equivalent) referenced by `WeaponDefinition` (or overridden by optic attachments) with:
  - Mode (None/Ironsight/RedDot/Scope), enter/exit times, target FOV, sensitivity scale, hip-move slowdown, hip vs ADS spread/recoil, camera/viewmodel offset, optional scope overlay texture/opacity.
- [ ] Set melee/weapons-without-ADS to Mode=None; define sensible defaults for AK/SMG/sniper (sniper uses Scope).
- [ ] Allow optic attachments to override `AdsConfig` (FOV/overlay/red-dot).

### Player State & Movement
- [ ] In `PlayerCharacter`, track an ADS blend (0→1) driven by Aim input + weapon allows ADS; cancel on weapon swap/reload if desired.
- [ ] Apply movement slowdown while aiming; block sprint while Aim is held.
- [ ] Replicate ADS blend/flag to remote players (interpolation friendly).

### Camera & Look
- [ ] Extend `PlayerLookController` to support ADS FOV override and sensitivity scaling (`lerp(BaseFov, AdsFov, blend)`, `MouseSensitivity * mix(1, SensScale, blend)`).
- [ ] Suppress head bob/tilt while fully scoped; smooth transitions via blend.

### Weapon Handling
- [ ] Let `WeaponController` query the equipped weapon’s `AdsConfig` each frame and expose the current ADS blend.
- [ ] Use blend to lerp spread/recoil between hip and ADS values; apply hip-move slowdown hook.
- [ ] Ensure non-ADS weapons keep blend at 0.

### Viewmodel & Presentation
- [ ] Add Hip/Ads anchor nodes to viewmodel scenes; in `WeaponView`, lerp the viewmodel transform between those using ADS blend (fallback to small offset if anchors missing).
- [ ] For Scope mode, add a full-screen overlay Control (texture + black vignette) driven by blend; no PiP glass.
- [ ] Hide crosshair when ADS blend is high; optionally shrink for ironsight/red-dot.
- [ ] Optional: play ADS enter/exit anim/foley on viewmodel.

### Testing & Tuning
- [ ] Verify local + remote player ADS replication (pose matches, no jitter).
- [ ] Check FOV/sensitivity transitions and sprint cancel.
- [ ] Tune spread/recoil values per weapon; tune overlay assets for sniper scope.

