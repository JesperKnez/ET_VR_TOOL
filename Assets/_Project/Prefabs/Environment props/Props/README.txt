ETPATHFINDER PROP PACK  (1 unit = 1 m, built from the photos in "Pictures David")

UNITY IMPORT (do this once for every FBX in the pack)
  Model tab -> tick "Bake Axis Conversion", Apply. Otherwise the root comes in rotated -90 on X.
  Glass / transparent materials (Glass_Clear, Glass_Viewport, StorageBox plastics): set Surface Type = Transparent.
  Emissive materials (LED_*, Lamp_*, LED_Panel) glow once you enable Emission on the extracted material.
  Metals look dark without reflections: add a Reflection Probe in the room and bake it.

PIVOTS
  Floor props: pivot at floor level.  Wall props: pivot on the wall surface, prop sticks out along +Z.
  Linear kit pieces (struts, pipes, elbows, wire tray): pivot at the start, running along +Z,
  so they snap end-to-end like the cable kit. Elbows turn right; rotate around their own Z for up/left/down.
  Ceiling tiles: 0.6 x 0.6 m, pivot at the centre of the tile's top. Place on a 0.6 m grid.

FOLDERS
  Crane/      OverheadCrane - 12 m span, 16 m runway, two 2 t hoists.
              Rig: Bridge (move along Z = runway), Hoist_A/B (move along X), Rope_A/B (scale Y = rope length,
              default 1.56), Hook_A/B (move along Y; keep it at -RopeScale).
  Vacuum/     BeamTube 1 m / 2 m (620 mm), Bellows 0.5 m, TubeStand_Blue (tube centre at 1.10 m),
              BlankFlange CF100/160/250, Port CF100/160, FeedthroughPlate (D-sub connectors), DomedDoor,
              Viewport_CF100, TurboPump. Beam tubes, bellows and flanges share the same flange size.
  Strut/      Strut 0.5/1/2 m, StrutPost 1/2.5 m (with base plate + red cap), Bracket90, WallArm 0.45 m,
              EndCap_Red, WireMeshTray 1 m.
  Pipe/       Pipe D60 and D110 (1 m), Elbow90 D60/D110, ClampFlange D60/D110, PipeWallBracket D60/D110
              (pipe centre 0.25 m from the wall), PipeFloorStand, PneumaticValve_D60, InsulatedPipe_D160.
  Equipment/  ControlBox_Wall - every button, lamp, selector, key switch and the e-stop is its own child
              object with its pivot at the door face: move it a few mm along +Z (into the box) to "press".
              ControlCabinets_Floor (3 cabinets on a stainless frame), OpticalTable (with cover box),
              Rack19_GlassDoor.
  Room/       CleanroomEnclosure_8x4m (the large white box in the hall, on legs).
  Clutter/    StepLadder, ToolTrolley, ScaffoldTower, WireShelving, StorageBox_Pink, StorageBox_Clear,
              TrafficCone, FireExtinguisher (with wall bracket), PalletTrolley.
  Ceiling/    CeilingTile_Plain, _LED, _SwirlDiffuser, _HEPAGrille.

Brand names and logos from the photos are left off on purpose.
