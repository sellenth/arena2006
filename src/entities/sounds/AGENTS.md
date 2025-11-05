Audio System Cheatsheet

- Overview: 3D, event‑driven SFX with small, focused owners. No global audio manager. Legacy systems removed.

- Engine (car): Car.cs uses EngineAudioService (looped engine layers, gear pops). Keep here; don’t re‑add RealisticEngineAudio/EngineAudioManager.

- Checkpoints: Checkpoint.cs plays a 3D one‑shot on RaceManager.CheckpointPassed (indexing starts at 0). Debounced to prevent spam.

- Rockets: 
  - Fire SFX: QuakeController (local), ProjectileManager (remote spawn).
  - Flight loop with Doppler: RocketProjectile (attached to projectile).
  - Explosion SFX: RocketProjectile (local and remote via manager destroy).

- Assets (fallbacks):
  - Fire: `res://sounds/firing.mp3`
  - Explosion: `res://sounds/explosion.mp3`
  - Flight loop (optional): `res://sounds/rocket_loop.ogg`

- Bus: ExplosionSFX bus auto‑created with Low‑Pass. Cutoff set by camera distance per explosion.

- One‑shot pattern: Create AudioStreamPlayer3D → AddChild → set GlobalPosition → Play → Finished → QueueFree.

- Remote vs local: Local SFX play at source immediately; remote SFX play in ProjectileManager (spawn/destroy) and via CheckpointPassed.

- Extending: Expose AudioStream + volume/maxDistance as exports on the owning node. Provide a sensible fallback path under `res://sounds/`.

- Gotchas: Add to tree before setting GlobalPosition; avoid double‑playing by gating on authoritative signals; keep indices 0..N‑1.
