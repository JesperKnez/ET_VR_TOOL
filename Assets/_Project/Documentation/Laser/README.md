# Einstein Telescope Laser Kit

Realistic laser + mirror alignment for the Unity VR template (URP, Unity 6).

## Quick start
1. Menu **Tools > Einstein Telescope > Laser > Build Demo Interferometer In Scene**.
2. Press Play. In VR (or the XR Device Simulator) point a controller at the **Laser Control Pad** console and
   click its D-pad. Keyboard (does not clash with the simulator): arrow keys tilt the selected mirror,
   `,` / `.` = previous / next mirror, `/` = fine/coarse steps, Backspace = reset mirror, L = shutter,
   K = switch laser 532 nm (green) / 1064 nm (infrared, invisible), I = IR viewer, P = print AI-tutor report,
   N = next level, H = open / close the briefing screen, U = undo last move, T = alignment beeps on / off.
   VR: hold GRIP and push the thumbstick to tilt the selected mirror (A / X = fine, B / Y = next mirror);
   the template's thumbstick locomotion pauses while you steer.
   The IR viewer only changes something for an infrared beam: press K (or the LASER button) first, then I.
3. Goal: both returning beams centred on the "Output Port Detector" (its light turns green and the
   objective completes after 2 s). The next level loads 5 s later.

## Feedback
- The selected mirror gets a glowing ring and a big name tag; every optic, iris, detector and the laser has a
  floating name tag that turns towards the player (`LaserOpticLabel`).
- UNDO on the pad (or U) takes back the last move: a burst of taps or one hold on the same mirror counts as one move,
  and RESET MIRROR can be undone too. A new level clears the history.
- Sound (`LaserAudioFeedback`, all sounds generated in code, no audio files): click per step, tick per button,
  motor hum while a mirror keeps moving, warning buzz for stray light and clipping, a ding when an iris or the
  detector lines up and a chime when the level is done. Alignment beeps get higher and faster as the beam nears the
  centre of the next target (toggle in the inspector or with T). Controllers vibrate along with the sounds.

## Seeing what you steer
- The light coming off the selected mirror, and everything after it, is drawn in cyan (the colour of the selection
  ring). A visual aid only; switch it off on `LaserSelectedBeamHighlighter`.
- The **Mirror Camera Screen** left of the pad shows a live camera feed from the selected mirror: a real camera on the
  mirror surface looking along the reflected beam (16 deg field of view, 512 x 288, every 2nd frame). The crosshair is
  where the beam goes: put the next iris or mirror on it. Without a beam on the mirror it looks straight out of it.
- The briefing screen floats to the right (about 72 deg from straight ahead, 2 m away), clear of the beams and the
  monitors, so HELP can stay open while you steer. Move it via "Briefing Screen (floating)" in the demo if needed.

## Results card and beam cameras
- When a level is completed a floating results card shows the time, stars (3 star models), safety incidents with a
  breakdown, and your personal best. Bests are saved between sessions with PlayerPrefs (`LaserBestTimes`); reset them
  with **Tools > Einstein Telescope > Laser > Levels > Reset Personal Bests**. RETRY replays the level, NEXT LEVEL
  continues (PLAY AGAIN after level 3), CLOSE hides the card. With the card, the next level waits for the player.
- A second screen above the monitor shows live "beam camera" close-ups of every iris of the level (the output
  detector in level 1): the spot in false colour on the iris face, the hole, the 1 cm goal zone and the position in mm.
  It also shows the beam when the iris plate blocks it (BLOCKED BY IRIS), so you can see which way to steer.
- The goal is a distance: every beam within 1 cm of the centre (8 mm right + 8 mm up = 11.3 mm is not centred).
  The monitor and cameras show that distance. Irises have a 4 cm opening, so a beam at the goal edge still passes
  ~99%; the output detector needs 30% of the laser power (a perfect alignment delivers ~49%).

## Briefing screen
Every level starts with a big floating screen (1.4 x 0.9 m, 1.6 m in front of the player) with three pages:
OBJECTIVE, CONTROLS and THE PHYSICS (simple maths: angle in = angle out, a tilt of θ turns the beam by 2θ, Δx ≈ 2·θ·L).
Click the tabs, BACK / NEXT, START or X with the controller ray. The level timer starts when the screen is closed;
HELP on the pad (or H) opens it again without resetting the timer. Edit the texts on each level's `LaserLevel`
component (Objective Text, Controls Tip, Physics Text) and the shared controls text on `LaserBriefingPanel`.

## Levels (same 1 cm accuracy everywhere, only the mirrors change)
| Level | Mirrors to steer | Checkpoints |
| --- | --- | --- |
| 1 Michelson | End mirror X, End mirror Y | output detector |
| 2 Beam walking | Steering mirror 1 + 2, End mirror X + Y | Iris A, Iris B, output detector |
| 3 Folded arms (GEO600 style) | Steering mirror 1 + 2, Fold mirror X + Y, End mirror X + Y | Iris A, B, X, Y, output detector |

The monitor names the first iris the beam has not passed yet and says which mirror to use. NEXT LEVEL on the pad
(or N) skips ahead. Choose the level that loads first with **Tools > Einstein Telescope > Laser > Levels > Start At
Level 1/2/3** (this also shows that level's optics in the Scene view). Each level is a child object with a
`LaserLevel` component; `LaserLevelManager` (on Laser Game Logic) switches them.

## Sizes (real scale, floor at y = 0)
- Beam height 1.30 m. Test mass 62 cm wide x 30 cm thick (Einstein Telescope HF design), beam splitter 50 cm.
- Control pad centre ~0.98 m, monitor centre 1.25 m: readable when standing (eyes ~1.6-1.7 m).
- XR Device Simulator: the template's XR Origin uses Camera Y Offset 1.1176 m (a seated height). Q / E move the
  simulated headset up and down. To test standing, set XR Origin > Camera Y Offset to ~1.65. A real headset uses
  your real height (Tracking Origin Mode = Floor).
- The demo builds its own floor at y = 0. If the template floor flickers through it, disable the template floor.

## Prefabs (Tools > Einstein Telescope > Laser > Create Materials and Prefabs)
Laser Source (rear emission beacon: red = beam on, green = shutter closed), Test Mass, Steering Mirror (25 cm, suspended) (suspended, Einstein-Telescope style), Beam Splitter (suspended), QPD Detector (status light on the back),
Alignment Iris, Beam Dump, Control Room Monitor (live beam-spot screen with quadrant bars and a text read-out: status, what to do next,
received power, beam positions in mm, laser state),
Laser Control Pad (placeholder console with a clickable D-pad; world-space UI for the XR controller rays).
Each suspended optic has a `Suspension Frame` child: delete it when the optic goes inside a vacuum tower.
Models are generated by the kit (Models/ folder); prefabs from an older kit version are rebuilt automatically.

## For teammates wiring the control room
Call these from any button / poke / knob event (no code needed, pick them in the inspector):

| Component | Methods |
| --- | --- |
| `MirrorControlPanel` | `YawLeft()` `YawRight()` `PitchUp()` `PitchDown()` `SelectNext()` `SelectPrevious()` `SelectMirror(int)` `ToggleFineMode()` `BeginHoldYawLeft()`... + `StopHoldMove()` `SetSelectedYawNormalized(float 0..1)` `ResetSelectedToStart()` |
| `MirrorMount` (one mirror) | same as above, plus `SetCommandedAngles(yaw, pitch)` |
| `LaserEmitter` | `OpenShutter()` `CloseShutter()` `ToggleShutter()` `SetWavelength(float nm)` |
| `LaserControlPad` | `ToggleLaserWavelength()` (532 / 1064 nm) |
| `InfraredViewerToggle` | `Toggle()` `SetViewerActive(bool)` |
| `LaserMonitorReadout` | read-only: `State`, `StatusLine`, `HintLine` (reuse the texts on other screens or for audio cues) |
| `LaserLevelManager` | `NextLevel()` `PreviousLevel()` `RestartLevel()` `LoadLevel(int)` `ToggleBriefing()`; events `OnLevelStarted(int)`, `OnAllLevelsCompleted` |
| `MirrorControlPanel` (extra) | `Undo()`, `UndoCount`, `ClearUndo()` |
| `LaserAudioFeedback` | `ToggleAlignmentTone()`, `SetAlignmentTone(bool)`, `SetHaptics(bool)` |
| `LaserBestTimes` | `TryGet(key, out seconds, out stars)` `Submit(...)` `Clear(key)` `FormatTime(s)` |
| `LaserResultsCard` | `Show(LaserLevelResult)` `Retry()` `Next()` `Close()`; events `OnRetry`, `OnNext`, `OnClosed` |
| `LaserAlignmentObjective` | `StartObjective()` and `BuildStatusReport()` (text for the LLM tutor) |

Useful events: `LaserTarget.OnAligned`, `LaserSafetyMonitor.OnWarningMessage(string)`,
`LaserSafetyMonitor.OnStrayLightStarted`, `MirrorMount.OnActuatorLimitReached`,
`MirrorControlPanel.OnSelectedReadoutChanged(string)`.

Put the control-room glass on the built-in **Ignore Raycast** layer (or any layer not in the laser's **Hit Layers**), otherwise the beam stops on the glass.

The full step-by-step guide is in the shared setup document.
