CRANE PIPE-ALIGNMENT LEVEL  (ET_VR_TOOL)

Install
1. Copy this "CraneLevel" folder into your Unity project's Assets folder (Assets/CraneLevel).
2. Wait for Unity to compile, then use the menu:  ET VR Tool > Build Crane Pipe-Alignment Level
3. The scene is saved to Assets/CraneLevel/Scenes/CraneLevel_PipeAlignment.unity and added to Build Settings.

Needs: XR Interaction Toolkit 3.x (+ Starter Assets for the "XR Origin (XR Rig)" prefab), Input System.

Play (VR): stand at the grey control cabinet. Poke the buttons (or point + grip).
Play (Editor, keyboard): W/S trolley, A/D bridge, R/F hook up/down, Q/E turn,
  Space attach/release, C creep speed, X emergency stop, Backspace reset.

Goal: pick up pipes P1, P2, P3 from the blue stands and place each one in its blue
"ghost" between the tower flanges. The ghost turns green when the position is within 6 cm
and the angle within 3 degrees; then press ATTACH/RELEASE. Hitting a tower counts as a collision.
