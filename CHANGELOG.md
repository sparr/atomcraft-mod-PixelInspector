# Changelog

Broad strokes only. Each release's notes say more, and the commits say most.

## 0.1.0

First release.

- **A magnifier panel.** Hold Alt and the world pixels around the cursor are drawn again, larger, in a square panel. `Alt -` and `Alt =` zoom it.
- **Machines are labelled** with what the pixel does not show: a filter's target material, which gate a gate is, a thermostat's setting, which way each one points, and whether it is switched off.
- **Logic gates as gate shapes**, turned to face the pixel, with their input legs at the 45 degrees the inputs actually sit at.
- **The player's tool highlight is mirrored** into the panel, read from the game's own buffer so it follows any tool.
- **A click that would not reach the world is swallowed**, and the pixel under the cursor is ringed red to say so.
