# Rhythm Level sample

A complete Rhythm-Heaven-style level built directly on the CLAP Router toolbox — no demo multiplexer, just the pieces you'd use in a real game:

- `RhythmCurveGenerator` → a 2D-curve pattern (pulse frequency + mutation) with rests and a 1/16-run cap, on the 1/16 grid.
- `RhythmLevelComposer` → the audible guide sheet + its anticipation `RhythmSequence`.
- `LiveSongScheduler` → streams the guide, quantizes taps to the grid with pre/post tolerance, and regenerates a fresh sheet each time one completes.
- `RhythmSheetVisualizer` → the scrolling sheet with tolerance bands and green/red hit flashes.

`RhythmLevelSample` is ~180 lines and is the whole game loop. It also shows the **device model**: a plugin is described by a `ClapDeviceDefinition` asset you drag onto a field — no hard-coded paths.

## Setup

1. **Import** this sample (Package Manager → [Zori] CLAP Router → Samples → Rhythm Level → Import).
2. Open (or create) a scene, then run **`CLAP Router → Setup Rhythm Level Sample`**. It creates a `RhythmLevel` GameObject with `MusicRouterHost`, `RhythmLevelSample`, and `RhythmSheetVisualizer` wired together, and assigns the included **Six Sines** device. If no Six Sines binary is present for your platform, it offers to **download** it.
3. If you skipped the prompt, run **`CLAP Router → Download Six Sines (this platform)`** — it fetches the official MIT release (~19 MB), extracts the `.clap` into `Claps/<platform>/`, and points the device at it. (Manual alternative: select the Six Sines device and **Browse…** to a `.clap` you built — see `Claps/README.md`.)
4. Make sure the `MusicRouterHost` can find the `clap-ipc` host binary and the staged `clap_ipc_client` native lib (same as any other scene in this package).
5. **Play**, and press **Space** on the beat.

The auto-download fetches only the editor's current platform. For a shipped build, add each target's `.clap` (see `Claps/README.md`) — macOS binaries require macOS tooling to unpack.

Hits inside tolerance sound on the grid on the *play* instrument; the *guide* instrument plays the sheet continuously on a separate track; out-of-tolerance taps are silent (they still flash red).

## Making it your own

- Swap instruments by dragging a different `ClapDeviceDefinition` onto **Guide Instrument** / **Play Instrument** (create one via `Assets → Create → CLAP Router → CLAP Device`).
- Tune `bpm`, `bars`, `restChance`, `maxSixteenthRun`, and the `pre/postTolerance` (fractions of a 1/16) on the sample component.
- Read `docs/rhythm-levels.md` and `docs/quantization.md` in the package repo for the model behind each piece.
