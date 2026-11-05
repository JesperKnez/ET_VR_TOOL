ETP CABLE KIT - modular cables & trays for Unity (1 unit = 1 m)

HOW THE PIECES WORK
- Every piece has its pivot at its START and leaves along local +Z (forward).
- To chain: put the next piece's pivot at the previous piece's END position and match its end direction.
- Bends only exist "turning right" / "going up". Rotate the piece around its own Z axis to change direction:
    Bend90:      Z rot 0 = right, 90 = down, 180 = left, 270 = up   (check once in the Scene view)
    Bend90_Up:   Z rot 0 = up, 180 = down
- Straights can be stretched with Scale Z (Straight_1m at Scale Z = 3.5 is a 3.5 m cable).

END POSITIONS (local, relative to the pivot)       END DIRECTION
  Straight_1m            (0, 0, 1)                   +Z
  Straight_0.25m         (0, 0, 0.25)                +Z
  Bend90_R0.15           (0.15, 0, 0.15)             +X
  Bend45_R0.15           (0.044, 0, 0.106)           45 deg between +Z and +X
  Bend90_Up_R0.15        (0, 0.15, 0.15)             +Y
  Sag_1m_0.15            (0, 0, 1)   hangs 0.15 m    +Z
  Sag_2m_0.35            (0, 0, 2)   hangs 0.35 m    +Z
  Offset_1m_Side0.25     (0.25, 0, 1)                +Z
  Offset_1m_Up0.25       (0, 0.25, 1)                +Z
  Drop_1m_R0.15          (0, -1, 0.15)               -Y  (tray edge down to floor)

TYPES
  Single        one 16 mm black power cable (swap material for other colours)
  Bundle6       6 x 10 mm mixed (black/red/yellow-green/blue/white)
  Bundle12Thin  12 x 6 mm thin signal wires, mostly orange (like the wall box)

TRAY
  Tray_Straight_1m       300 mm wide galvanised tray, bottom at Y=0, runs along +Z
  Tray_Bend90_R0.30      flat 90 deg corner, end at (0.30, 0, 0.30) facing +X
  Tray_Bend90_Up_R0.30   vertical bend, end at (0, 0.30, 0.30) facing +Y
  Tray_WallBracket       pivot on the wall, arm sticks out along +Z; tray bottom rests at Y=0.
                         Rotate the tray 90 deg on Y to run along the wall, centre it ~0.17 m from the wall.
  Strut_Post_1m          41 mm strut post, base at Y=0, Scale Y for other heights

TIP: For long free-form runs, Unity's Splines package (Spline Extrude component) can also draw
a cable along any curve you click out - use this kit for the neat, repeatable bits.
