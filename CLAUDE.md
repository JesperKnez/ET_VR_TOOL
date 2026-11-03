# ET_VR_TOOL: context voor Claude Code

Antwoord in het Nederlands. Code, namen en comments blijven Engels.

## Wat is dit project

VR-ervaring over de **Einstein Telescope** voor **middelbare scholieren**. Doel is ze enthousiast maken voor techniek, het is geen operatortraining. Het project hoort bij het XR-groepsproject (10 EC) van de opleiding Applied Data Science & AI (Zuyd Hogeschool), Track B "Applied Engineering for the Einstein Telescope".

- De omgeving is een **letterlijke nabouw van ETpathfinder** (de cleanroom-hal in Maastricht), gebaseerd op het ETpathfinder Design Report en foto's van de echte hal.
- De speler blijft in de **control room** en kijkt door de ramen naar de hal. De hal is puur decor.
- Gameplay: met knoppen op een curved monitor beweeg je spiegels om een laser uit te lijnen ("dartboard"-stijl). Er zijn levels van oplopende moeilijkheid.
- Oplevering (week 9, begin november 2026): werkend prototype, videodemo en rapport met usabilitytest.

## Team en eigenaarschap

| Wie | Onderdeel |
|---|---|
| **Jesper** | Repo-eigenaar, beheert de hoofdscene `Room_Scene`, bouwt de omgeving (Blender), belichting, control room, curved monitor-UI |
| **Carlo** | Laser/Michelson-interferometer: optics, levels, gameplay-scripts (`_Project/Scripts/Runtime/`) |
| **David** | Torens (vacuümtorens), oorspronkelijk gebouwd met ProBuilder en `ETpfTowerBuilder.cs` |

- Iedereen werkt op een **eigen branch** (bijv. `david/tower`, `controlroom`) en in een **eigen testscene**.
- **Alleen Jesper bewerkt `Room_Scene`.** Wijzigingen komen binnen via pull requests.
- Prefab-wijzigingen gaan via Prefab Mode of `Overrides → Apply`, niet als losse scene-overrides.

## Techstack

- **Unity 6.6** (6000.6.6f1), **URP**, Windows-editor (DX11)
- **OpenXR** met de **Oculus OpenXR**-runtime. Testen gebeurt op een **Meta Quest** via **Air Link** (of Quest Link) in Play Mode.
  - Interaction profiles: Oculus Touch en Meta Quest Touch Plus.
- **XR Interaction Toolkit 3.6.1** (samples: Starter Assets en Hands Interaction Demo 3.5.1, XR Device Simulator 3.6.1), met `XR Origin Hands (XR Rig)` en de XR Device Simulator. XR Hands 1.9.0, OpenXR 1.18.0, AR Foundation 6.6.2 (staat er nog, wordt niet gebruikt).
- Input System, ProBuilder, Splines en FBX Exporter (van Davids werk)
- Git + **Git LFS** voor `.fbx`, `.blend` en textures. Repo: `ET_VR_TOOL` op GitHub.
- **Quality per platform:** Standalone (pc, ook Air Link) gebruikt *Ultra* → `Quality URP Config` (Opaque Texture **aan**). Android (Quest-build) gebruikt *Low* → `Performance URP Config` (Opaque Texture **uit**). Shaders die `_CameraOpaqueTexture` lezen werken dus wel via Link, maar niet in een losse Quest-build.

## Mappenstructuur

Al het eigen werk staat in `Assets/_Project/`:

```
_Project/
  Scenes/      Room_Scene (hoofdscene), Laser_Scene (Carlo), Tower_Scene (David)
  Scripts/
    CurvedScreen.cs, CurvedScreenPointer.cs        ← curved monitor (Jesper)
    Runtime/   Core, Optics, Control, Gameplay, Feedback   ← laser (Carlo)
    Props2/    SignalTower                         ← control room-props (Jesper)
    Rendering/ ThickGlassSceneColor, TowerTransparency   ← glas-fallback, doorzichtige torens (Jesper)
    Debug/     LaserGamepadControls   ← PS5/Xbox-controller voor het laserspel; maakt zichzelf aan bij Play (editor/dev builds)
    Editor/    LaserSetupMenu, LaserLevelTexts, Models/   (Carlo)
               ETpfTowerBuilder                            (David)
               MonitorUISetup, CreatePanelLights, InstructionBoardSetup, XRCleanup, TowerMaterialSetup, HallLookSetup, ControlRoomWallSetup, FloorMarkingSetup, HallPropsLayout   (Jesper)
    Tower/     CircleSpline, RoundedSquareSpline
  Prefabs/     Laser/, Tower/, Room.prefab
  Models/      Laser/ (gegenereerde .asset-meshes), Tower/, Room/ (Room, Desk, ETpf_Tower_*, props), Props2/ (ET_*-props),
               ETP_*-kits voor de hal: Clutter/, Equipment/, Vacuum/, Pipe/, Strut/, ETP_CableKit/, Ceiling/, Crane/
  Materials/   Laser/, Tower/, Room/ (wanden, vloeren, glas), Props2/ (ET2_*-materialen), MonitorUI_Screen.mat
  Textures/    Room/ (cleanroom-textures), MonitorUI_RT.renderTexture
  Shaders/     LaserBeam.shader, ThickGlass.shader, SG_Glass.shadergraph
  Documentation/Laser/README.md   ← uitleg van Carlo's laserkit
```

Namespaces: `EinsteinTelescope.Laser` (laser), `EinsteinTelescope.Props.Editor` (instructiebord-menu). Jespers oudere scripts (`CurvedScreen*`, `MonitorUISetup`, `CreatePanelLights`, `SignalTower`) staan in de global namespace.

### Editor-menu's

| Menu | Script | Van |
|---|---|---|
| `Tools → Einstein Telescope → Laser → …` | `LaserSetupMenu.cs` | Carlo |
| `Tools → Einstein Telescope → Instructiebord → Briefing op bord zetten` | `InstructionBoardSetup.cs` | Jesper |
| `Tools → Einstein Telescope → XR → Restanten Composition Layers opruimen` | `XRCleanup.cs` | Jesper |
| `Tools → Einstein Telescope → Torens → 1/2/3 …` | `TowerMaterialSetup.cs` | Jesper |
| `Tools → Einstein Telescope → Torens → Vloermarkering rond torens en buizen` | `FloorMarkingSetup.cs` | Jesper |
| `Tools → Einstein Telescope → Belichting → Post-processing afstemmen op foto's` | `HallLookSetup.cs` | Jesper |
| `Tools → Einstein Telescope → Control room → Muur: geschilderd stucwerk` | `ControlRoomWallSetup.cs` | Jesper |
| `Tools → Einstein Telescope → Hal → Props plaatsen / Props weghalen` | `HallPropsLayout.cs` | Jesper |
| `Tools → ET → Setup Curved Monitor UI` | `MonitorUISetup.cs` | Jesper |
| `Tools → ET → Create Panel Lights` | `CreatePanelLights.cs` | Jesper |
| `Tools → ETpathfinder Tower Builder` | `ETpfTowerBuilder.cs` | David |

Alles buiten `_Project` komt uit templates of packages. Pas dat niet aan.

## Hoofdscene (`Room_Scene`), globaal

- `XR Origin Hands (XR Rig)`, `XR Device Simulator` (alleen voor testen zonder headset)
- `Laser Demo - Michelson Interferometer`, met `Level 1 - Michelson`, `Level 2 - Beam Walking`, `Level 3 - Folded Arms`, `Laser Game Logic`, Briefing Screen en Results Card
- `Room`: ControlRoom_Root (ControlRoom, ControlRoom_Glass), Hall, Light Panels
- `Lighting`: ControlRoom_Light 0–3, PanelLights, Adaptive Probe Volume, RP_Hall, RP_ControlRoom, Global Volume
- `Desk`, `Monitor`, `Monitor UI Camera`, `MonitorUI`, `TowerConstellation`, `ET_ControlRoom_Props`
- **Lumi**: het team-avatar, een zwevend robotje van ±44 cm in een hoek van de control room, naast het instructiebord

## Belangrijke systemen

### Laser (Carlo)
- Verplaats of roteer de interferometer **alleen als geheel** via de root `Laser Demo - Michelson Interferometer`. Losse optics niet verplaatsen of schalen: spiegelposities, irissen en toleranties zijn in echte meters berekend.
- **Hernoem die root niet.** Editor-menu's vinden hem via `GameObject.Find` op exacte naam.
- `MirrorMount` slaat de lokale rotatie op als baseline, dus parent-transforms zijn veilig.
- Menu: `Tools → Einstein Telescope → Laser → …`. **Gebruik `Rebuild All Prefabs` niet**, want die overschrijft prefabs. De mappaden in `LaserSetupMenu.cs` wijzen nu naar `_Project/{Materials,Prefabs,Models}/Laser` (stonden eerder op `Assets/EinsteinTelescope/Laser`).
- De shadernaam `EinsteinTelescope/LaserBeam` (in `LaserBeam.shader`) wordt via `Shader.Find` gezocht. Niet hernoemen.

### Laser in de torens (Jesper, nov 2026)
- Carlo's opstelling was een compacte testopstelling (armen van 2,5 m in een lab-box). `LaserTowerLayout.cs` (`Tools → Einstein Telescope → Laser → Levels in de torens plaatsen`) verplaatst de **bestaande** level-objecten naar de torens en houdt alle koppelingen intact. Daarna zijn de regels "alleen als geheel verplaatsen" en "Build Demo" niet meer van toepassing: **`Build Demo Interferometer In Scene` niet meer gebruiken**, want die zet de testopstelling terug en verbreekt de koppelingen met de curved monitor.
- Torenlayout (gemeten met `Torens → Torenlayout meten`, wereld x/z; bundelhoogte 1,33 m, armen 90°, alles op 45°-lijnen):

  | Toren | x, z | Rol |
  |---|---|---|
  | T3 | 2,60, −7,70 | injectie: laser, in level 2/3 ook de periscoop van stuurspiegels |
  | T2 | 0, −5,10 | beam splitter, output-detector 0,9 m richting (−x, −z) |
  | T1 / T4 | ∓4,525, −0,575 | middentorens (6,40 m): eindspiegels in level 1/2, iris X/Y in level 3 |
  | T0 / T5 | ∓11,172, 6,072 | eindtorens (15,80 m): eindspiegels in level 3 |

  De doorgaande arm (X) loopt T3 → T2 → T1 → T0, de gereflecteerde arm (Y) T2 → T4 → T5. De Y-arm begint op het raakpunt van de coating, niet in het midden van de BS (4,2 cm verschil).
- **Level 3 heet nu "Long arms"**: rechte armen hebben geen vouwspiegels (die staan uit), de eindspiegels staan in de eindtorens en de irissen in de middentorens. Teksten in `LaserLevel` zijn daarop aangepast.
- **Precisie meegeschaald**: per spiegel worden stapgrootte, bereik, beginafwijking en seismische ruis vermenigvuldigd met k = ontwerp-hefboom / nieuwe hefboom (fijne stap level 1 ±0,025°, level 3 ±0,011°). Eén stap verplaatst de vlek dan even ver als in de testopstelling. De tool rekent vanaf Carlo's ontwerpwaarden, dus opnieuw draaien is veilig.
- De monitorcamera's lopen vanzelf mee: `LaserMirrorCamera` zet zijn camera elk frame bij de geselecteerde spiegel (far clip 30 m, de langste arm is 16 m) en de `LaserBeamCameraView`s volgen hun target.
- De straal is fysiek ±1,8 cm breed en vanaf 13–25 m dunner dan een pixel. `Scripts/Rendering/LaserBeamMinWidth.cs` (maakt zichzelf aan bij Play) verbreedt elke straallijn tot minstens 0,1° gezien vanaf `Camera.main`.
- `TowerTransparency.clearInLevels` = {1, 2, 3}, anders zie je de optics in de tanks niet. De lab-box (vloer en muren) staat uit.

### Curved monitor-UI (Jesper)
- Drie oude panelen (mirror camera, D-pad besturing en dartboard-HUD) staan nu samen op één gebogen ultrawide monitor.
- Werking: een verborgen canvas (`MonitorUI`, *Screen Space - Camera*) wordt door de orthografische `Monitor UI Camera` (staat op y = -100) naar `MonitorUI_RT` (3840×1200) gerenderd. Dat is op het object `Monitor Screen` te zien via `MonitorUI_Screen.mat` (URP/Unlit). `CurvedScreen.cs` en `CurvedScreenPointer.cs` vertalen raycast-hits van de controller (mesh-UV) naar UI-raycasts op dat canvas. `MonitorUISetup.cs` (editor) zet RenderTexture, layers, camera en canvas op.
- Layers: `ScreenUI` (canvas, alleen zichtbaar voor de UI-camera; de XR-camera moet hem uitsluiten) en `CurvedScreen` (het scherm-object, voor de `screenMask` van de pointer).
- Panelen op het canvas: `Panel_MirrorCam`, `Panel_DPad`, `Panel_HUD`. Het scherm meet 2,40 × 0,75 m, 1600 px per meter.
- `Monitor Screen` heeft een niet-convexe `MeshCollider` nodig en de FBX moet **Read/Write** aan hebben, anders is `hit.textureCoord` in builds altijd (0,0).
- De dartboard-HUD bestaat uit 3D-meshes onder `Panel_HUD` en wordt door dezelfde UI-camera gerenderd.

### Instructiebord
- De TV aan de muur (`InstructionBoard` / `InstructionBoard_Screen` in `ET_ControlRoom_Props`) toont Carlo's bestaande briefing (`Briefing Screen (floating)`, `LaserBriefingPanel`). Menu: `Tools → Einstein Telescope → Instructiebord → Briefing op bord zetten` zet de briefing precies op het scherm en geeft het scherm de donkere `ET2_BoardScreen_Active`. Opnieuw draaien na verplaatsen van de TV.
- Verder geen eigen logica: de level manager, HELP en de X/START-knoppen openen en sluiten de briefing zoals altijd. (Het oude standby-systeem `InstructionBoard.cs` met onzichtbare knop is verwijderd; `ET2_BoardScreen`/`ET_BoardStandby.png` worden niet meer gebruikt.)

### Signaalzuil
- `SignalTower.cs` op `SignalTower_Wall`/`SignalTower_Base` zet de lenzen (`ET2_Signal_Red/_Amber/_Green`) aan/uit via `_EmissionColor`. Aansturen met `SetState(SignalTower.State.…)`. Herkent de lenzen aan de materiaalnaam, dus die namen niet wijzigen.

### Glas (control room-ramen)
- `Room.fbx` gebruikt `M_GlassThick` (shader `XR/ThickGlass`) voor de ruiten en `M_GlassEdge` (URP Lit, opaque, donker flessengroen ~#1E4A3A, smoothness 0.9) voor de randen.
- `M_Glass` (`SG_Glass.shadergraph`) was het oude glas en is verwijderd. Let op: het FBX-slot `Beam_Glass` van `TowerConstellation` is géén glas. Daarop staan de tankmantels en bundelbuizen (Element 0 in Unity). Het wordt nu door `TowerMaterialSetup` aan `M_Tower_BlastedSteel` gekoppeld. Een leeg slot rendert felroze. Netter is het om in Blender het slot een eigen naam te geven of de vlakken aan `Yellowy_Steel` toe te wijzen. `Room.prefab` had ook een override naar `M_Glass` op één renderer; controleer of daar nu "None" staat.
- `ThickGlass` leest `_CameraOpaqueTexture` (refraction). `ThickGlass` projecteert de verschuiving per oog in world space en is daarmee de "VR-veilige" variant. Zie de valkuil over glas hieronder.
- Opbouw van `ThickGlass`: slab-refraction in world space (lichtweg = `_Thickness / cosθt`), Beer-Lambert-absorptie `exp(-_Absorption * _AbsorptionScale * pathLength)`, Schlick-Fresnel (F0 = 0.04), reflectie via `GlossyEnvironmentReflection` (reflection probes, box projection) en een GGX-highlight van het main light.
- **`_Absorption` is een Vector, geen kleur:** het is hoeveel rood/groen/blauw het glas per meter *opslokt*. Hogere R en B dan G geeft groen glas. (Was eerst een Color-property; de color picker toonde dat als roze en was verwarrend.)
- Huidige richtwaarden op `M_GlassThick`: `_Thickness` 0.03 (moet gelijk zijn aan de gemodelleerde dikte), `_Absorption` (3, 1.5, 2.5, 0), `_AbsorptionScale` ~0.4, `_ReflectionStrength` 0.5–2 (nog aan het tunen, zie openstaande visuele problemen), `_Smoothness` 0.97–1.
- **Refraction staat standaard uit** (vinkje `Refraction` op het materiaal). Zonder refraction werkt `ThickGlass` met twee passes: `ThickGlassTint` (SRPDefaultUnlit, `Blend DstColor Zero`) vermenigvuldigt de achtergrond met de absorptiekleur, en `ThickGlassForward` telt er reflectie en highlight bij op. **Met refraction verdwijnen transparante objecten achter het glas**, want de Opaque Texture wordt vóór de transparante objecten vastgelegd. Zo waren de torens onzichtbaar vanuit de control room zodra `TowerTransparency` ze doorzichtig maakte (2 nov 2026), en hetzelfde geldt voor laserstralen.
- **Fallback zonder Opaque Texture:** `Scripts/Rendering/ThickGlassSceneColor.cs` (statisch, geen component) zet per camera de global keyword `_THICKGLASS_NO_SCENE_COLOR` aan als de camera of URP Asset geen Opaque Texture heeft (bijv. een Quest-build). `ThickGlass` blendt dan gewoon (premultiplied alpha) met reflectie en Fresnel, zonder refraction. De absorptie wordt dan een grijze in plaats van een groene tint.
- `SG_Glass` heeft geen fallback, maar is oud (zie hierboven).
- `ThickGlass` verwacht een massieve mesh (voor- en achterkant, bijv. Solidify in Blender), `_Thickness` gelijk aan de echte dikte. Normals moeten naar buiten wijzen (`Cull Back`).
- Bekende beperking: het glas krijgt één set reflection probes. Met de huidige probe-instellingen weerspiegelt het glas van beide kanten de control room. Dat is bewust, de speler staat binnen.

### Control room-muur
- `Room.prefab` zet op de ControlRoom-renderer slot 1 op `ControlRoom_Wall.mat` (slot 0 is `Floor`). Het FBX-slot `Walls` (`Walls.mat`) wordt daardoor op die renderer niet gebruikt.
- `ControlRoomWallSetup.cs` genereert `Textures/Room/T_Plaster_*` (geschilderd stucwerk: gebroken wit ±77%, latex-mat, zachte spaanvlekken en fijne rollerstructuur, geen naden; tegel 2 × 2 m) en zet ze op `ControlRoom_Wall`. De tiling volgt uit een UV-meting van de muren. Uiterlijk aanpassen in `Plaster()`. Een eerste versie met akoestische panelen had te harde naden en is vervangen. Daarna opnieuw baken.

### Hal aankleden (`HallPropsLayout`, nov 2026)
- Hal volgens `Room.fbx`: x ±17,15, z ±11, vloer 0, plafond 8 m. Control room in de hoek x −17,15..−9,15, z −11..−3, vloer op y ≈ 2,2. Muren in het script: noord = z+, zuid = z−, oost = x+, west = x−.
- Met de hand in de scene gezet (3 nov 2026, niet door het script): `ETP_OverheadCrane` over de hele hal (schaal 1,71 × 2,12, rails langs noord- en zuidmuur op ±6,5–7,5 m), `ETP_MastLift` op (−12,3, 0, 2,9), en `ETP_OpticalTable`, `ETP_ControlCabinets_Floor` en `ETP_ToolTrolley` in de control room. Let op: de kraan is non-uniform geschaald, dus haken en takels zijn mee uitgerekt.
- `Tools → Einstein Telescope → Hal → Props plaatsen` zet alles onder één object `Hall_Props` (per zone een kind): technische rij langs de noordmuur (rekken, ringbandenkast, schakelkasten, 19"-racks, kabelgoot op 3 m met kabels die de kasten in zakken, leidingen D110/D60 op 4,3/4,7 m), west- en oostmuur (racks, bedieningskastje, luchtbehandelingskast, rekken met onderdelen), AED/EHBO naast de control room, opslag van reserveonderdelen in de zuidoosthoek, een werkplek tussen de armen met rolsteiger bij T4, en HEPA-/wervelroosters tussen de lichtpanelen. Opnieuw draaien vervangt `Hall_Props`; bestaande objecten blijven onaangeroerd.
- Botsingscontrole: een raster van 20 cm, opgebouwd uit de driehoeken van `TowerConstellation` en de bounding boxes van alles wat er al staat (behalve de laserdemo, lijnen en objecten > 12 m, zoals hal en kraan). Botst een prop, dan schuift hij tot 1 m op; lukt dat niet, dan wordt hij overgeslagen. Beide staan in de Console.
- Oriëntatie: wandprops krijgen de kant waar het draaipunt tegenaan ligt tegen de muur. Ligt het draaipunt in het midden, dan geldt voorkant = +Z (Blender −Y). Staat iets achterstevoren, zet dan `flip: true` bij die regel. Rechte stukken (leidingen, kabels) worden opgerekt, goten in stukken van 1 m gelegd. Spullen op planken en tafels worden met tijdelijke MeshColliders op het oppervlak gezet.
- Props krijgen ContributeGI + Batching Static en **Receive GI = Light Probes**: de kits hebben geen lightmap-UV's. Daarna opnieuw baken. Het script meldt het totaal aantal tris (Quest-budget).
- Afstanden van de leidingbeugels (`PIPE_D110_FROM_WALL`, `PIPE_D60_FROM_WALL`) zijn geschat uit de beugelmaten. Controleer één keer in de Scene view of de leiding in de klem ligt.
- Niet gebruikt: `Strut/`, de bochten/sag/offset-stukken uit de CableKit, de meeste `Pipe/`-onderdelen, `Ceiling/` LED en Plain (de lichtpanelen bestaan al), `ET_Cameras`, `ET_Zaklamp`, `ET_ServerRack` en `Room/CraneControlStation.fbx` (heeft knoppen als RESTART LEVEL, lijkt bedoeld voor gameplay).

### Belichting
- Gebakken belichting met rechthoekige area lights per plafondpaneel (`CreatePanelLights.cs`), Adaptive Probe Volumes voor dynamische objecten, en reflection probes voor hal en control room.
- Global Volume met Bloom voor gloeiende LEDs en schermen.
- **Kleine emissieve materialen (LEDs, signaalzuil, schermen): `Emission → Global Illumination = None`**, anders "verlichten" ze bij baken de hele ruimte.
- `RP_ControlRoom`: Baked, Importance 2, Box Projection aan, Box Size 7.8 × 3.6 × 7.8 (iets groter dan de kamer zodat het glas er zeker binnen valt), **Blend Distance 0.1**. Bij Blend Distance 1 lag het glas in de blendzone met `RP_Hall` en kreeg het een fletse, witte reflectie. Na elke wijziging opnieuw baken.

## Openstaande visuele problemen (stand 2 nov 2026)

Wordt opgepakt in Claude Code. Eerst materialen en instellingen inspecteren, dan pas aanpassen.

### 1. Hal ziet er flets en mintgroen uit ("tandartspraktijk")
- Door het glas heen heeft de hal een koele cyaan/mintgroene waas, met uitgebeten witte muren en weinig contrast.
- In de laatste screenshot hebben ook de controllers (die *niet* achter het glas zitten) een groenige zweem. Dat wijst op een **globale oorzaak**, niet alleen op het glas. Te controleren:
  - `Global Volume`: is er een Tonemapping-override (Neutral of ACES)? Color Adjustments, White Balance of Split Toning die naar cyaan trekt? Post Exposure?
  - Kleur van de area lights / ControlRoom_Lights en de omgevingsbelichting (Lighting window → Environment).
  - Albedo van hal-materialen (`Hall_Wall`, `Hall_Floor`, `Walls`, `Ceiling`): puur wit overbelicht snel. Richtwaarde ~70–80% grijs.
- Diagnosetest: `ControlRoom_Glass` uitzetten en vergelijken. Blijft de tint, dan ligt het niet aan het glas. `_AbsorptionScale` op 0 zetten is een tweede test.
- Belichting opnieuw baken via Window → Rendering → Lighting → Generate Lighting (bakt ook APV en probes).

### 2. Glas: te weinig reflectie
- Recht van voren reflecteert glas maar ~4% (Fresnel). Omdat de hal veel lichter is dan de control room, valt die reflectie weg. Natuurkundig klopt dat, maar in VR helpt een zichtbare reflectie voor dieptegevoel.
- Opties: `_ReflectionStrength` 1.5–2, `_Smoothness` 1, of eventueel een instelbare F0-property aan `ThickGlass` toevoegen.

### 3. Torens (`TowerConstellation`): materiaal en mogelijk UV's
- Materialen op de renderer: `PolishedSteel`, `WeldTube`, `Yellowy_Steel`, `BoltDecal` (allemaal URP Lit).
- De tanks zien er zwart uit met lange verticale strepen. Waarschijnlijke oorzaken, in deze volgorde:
  1. **Metallic zonder bruikbare reflectie:** metaal heeft geen diffuse kleur en toont alleen wat het weerspiegelt. Staan de torens niet goed binnen `RP_Hall` (box die ze helemaal omvat, Box Projection aan, gebakken), dan worden ze zwart.
  2. **Smoothness te hoog:** een gepolijste cilinder spiegelt de hal en de lampen als lange strepen. Voor geborsteld RVS (zoals echte vacuümtanks): smoothness ~0.5–0.65, metallic 1, base color lichtgrijs (~200,200,200). Eventueel metallic iets lager (0.6–0.8).
  3. **UV's:** de torens zijn in Blender één groot object. Samenvoegen zelf rekt geen UV's uit, maar ontbrekende of niet-uitgevouwen UV's of niet-toegepaste scale wel. Test: tijdelijk een checker-texture als Base Map met metallic 0. Zijn de vierkantjes uitgerekt, dan in Blender Ctrl+A → Scale en U → Cube Projection of Smart UV Project, en opnieuw exporteren.
- Alternatief zonder UV-werk: een **triplanar** URP-shader (world-space projectie) voor generiek metaal op het grote model.
- De Metal011-texture (ambientCG) in `Materials/Tower/`: Unity heeft alleen de losse JPG's nodig (Color, Metalness, Roughness, NormalGL). URP Lit verwacht metallic + smoothness in één map: **Metalness in R, smoothness (= 1 − Roughness) in A**. Die moet dus eerst worden ingepakt. NormalGL is de juiste normal map voor Unity.

### Nieuwe PBR-materialen voor de torens (2 nov 2026)
- Gemaakt met `TowerMaterialSetup.cs`, menu `Tools → Einstein Telescope → Torens`. Drie stappen in volgorde:
  1. **PBR-textures en materialen genereren.** Maakt in `Textures/Tower/` naadloze 1024²-textures (`T_BrushedSteel_*`, `T_BlastedSteel_*`: BaseColor, MetallicSmoothness met metallic in R en smoothness in A, Normal) en in `Materials/Tower/PBR/` de URP Lit-materialen plus een `_Clear`-tweeling per materiaal.
  2. **Koppelen aan TowerConstellation.** Remapt de FBX-slots (`PolishedSteel` → `M_Tower_BrushedSteel`, `Yellowy_Steel` → `M_Tower_BlastedSteel`, `WeldTube` → `M_Tower_Weld`, `BoltDecal` → `M_Tower_Bolts`) via de ModelImporter. Draait material-overrides op de renderers van `TowerConstellation` in de scene terug (die winnen van de FBX-remap; er hing een override die slot 0 op `PolishedSteel` vastzette) en zet `TowerTransparency` op `TowerConstellation`.
  3. **UV's controleren en tiling instellen.** Meet per slot hoeveel meter één UV-eenheid is, meldt ongelijke of ontbrekende UV's in de Console en zet de tiling zo dat één tegel 1 × 1 m is. Controleert ook of een reflection probe binnenin een toren staat.
- **`TowerConstellation` in Blender: ±404k tris en 227k vertices** (2 nov 2026). Dat is te zwaar voor de Quest; streef naar ≤100k voor de torens. Analyse en veilig opschonen met `Tools/Blender/tower_cleanup.py` (repo-root, buiten `Assets`): rapporteert tris per material slot en de zwaarste groepen identieke onderdelen (bouten, balgen), en doet daarna merge by distance, loose/degenerate opruimen, een lichte limited dissolve (0,5°, hoger maakt de ronde tanks lelijk) en recalc normals. Het origineel blijft bewaard als `<mesh>_backup`. Decimeren en vervangen doe je zelf op basis van het rapport.
- Rapport van 2 nov 2026 (404k → 367k tris na cleanup): de 9 **balgen** zijn samen 123k tris (13.696 per stuk), de **lasringen** ±37k (1.024 per ring), 432 kleine beugels/lippen 41k (96 per stuk), en 840 boutjes van 2–5 cm samen ±17k. `Tools/Blender/tower_reduce.py` meet van elke balg het silhouet over de lengte (grootste straal per plak, inclusief eindkragen, vereenvoudigd tot op 3 mm) en van elke lasring de maten, en **bouwt ze opnieuw**. **Het opnieuw opbouwen van de balgen staat uit (`REBUILD_BELLOWS = False`):** de balgen hebben 23 ribbels, dus het profiel heeft ±88 punten nodig en het levert maar ±45% op. Daarnaast werden gladde buisstukken van 1.024 tris als balg gezien en zwaarder gemaakt. Wat wel werkt: lasringen (−23k) en UV's. Resultaat ±344k tris; verder reduceren alleen na meten op de Quest. Een onderdeel telt alleen als het echt rond is: de grootste straal moet binnen 8% van de halve doorsnede liggen. Zonder die controle werden op 2 nov 5 vierkante kastjes van 0,68 m als balg vervangen. Het script als schone low-poly draaivorm: balg 32 segmenten met scherpe V-ribbels, lasring als torus van 48×4. Het werkt op het origineel en op een gedecimeerde versie. Decimate (gekreukte ribbels) en het weghalen van meridianen (fragiel, deed niets) zijn geprobeerd en afgevallen. Daarna maakt het de UV's: bout-decals Reset, de rest Smart UV Project. Het verwijdert ook onderdelen < 3 cm, behalve de bout-decals (`BoltDecal`: vierkantjes van 2 tris met een bout-texture). Dry run: balgen 93k, lasringen 41k → verwachte uitkomst ±265k tris. Eerst draaien met `DRY_RUN = True`.
- **Meting 2 nov 2026: 91–98% van het torenoppervlak heeft géén UV's** (alle UV's op één punt), en de bout-vlakken hebben er helemaal geen. Daardoor leest elke texture één pixel en lijkt alles egaal, zonder detail. Oplossing in Blender: `TowerConstellation` → Edit Mode → alles selecteren → U → Smart UV Project (Island Margin ±0.002), exporteren, daarna stap 3 opnieuw voor de tiling. De bout-vlakken eerst apart: selecteer via het material slot `BoltDecal` → U → Reset (elk vlak krijgt de hele decal), en sluit ze uit van de Smart UV Project-stap. `RP_Hall` staat niet binnenin een toren (2/6 richtingen).
- **Flikkeren onderaan de torens (3 nov 2026):** `TowerConstellation` stond op y = 0, waardoor de onderkant van de voetplaten met de hal-vloer z-fightte. Opgelost door de toren op **y = 0,05** te zetten. Gevolg: de bundelbuizen liggen nu op ±1,38 m, terwijl `LaserTowerLayout` de optics op `BEAM_HEIGHT` 1,33 m zet. De optics moeten dus 5 cm mee omhoog, of de toren moet weer omlaag en de vloer iets lager.
- De platen onder en in de tanks zijn n-gons van 64–128 hoeken (sommige concaaf). Dat gaf hier geen probleem, maar `tower_reduce.py` trianguleert ze nu (stap 5, `TRIANGULATE_NGONS`) om slivers te voorkomen.
- `RP_Hall` staat op (0, 4, 0), precies in het midden van `TowerConstellation` (dat ook op de oorsprong staat). Zit het opnamepunt binnenin een tank, dan weerspiegelt al het metaal de donkere binnenkant en worden de torens zwart. Verplaats in dat geval de probe en compenseer met Box Offset.
- **Doel-look (foto's van de echte hal):** tankmantels zijn champagnekleurig, satijnmat RVS met zachte, brede reflecties en een paar lichte vlekken. Leidingen, flenzen en poortdeksels zijn lichter en neutraal RVS. Lasnaden zijn iets donkerder en warmer. De hal is high-key en heel wit met heldere LED-panelen. Blauwe steunbokken staan onder de leidingen.
- Slots: `Beam_Glass` (tankmantels en bundelbuizen, ondanks de naam) en `Yellowy_Steel` → `M_Tower_BlastedSteel` (champagne, smoothness ±0.5); `PolishedSteel` → `M_Tower_BrushedSteel` (neutraal, ±0.6). Klopt dat niet, wissel dan de twee regels in `Slots` om en draai stap 2 opnieuw.
- Post-processing: `Tools → Einstein Telescope → Belichting → Post-processing afstemmen op foto's` (`HallLookSetup.cs`) zet de Global Volume op Neutral tonemapping (was ACES, dat wit naar grijs drukt), contrast +10, saturation -5 en bloom 0.35 bij threshold 1.
- Opnieuw draaien is veilig: bestaande assets worden bijgewerkt en houden hun GUID. De textures zijn procedureel (ruis in C#), dus aanpassen gaat via de functies `BrushedSteel()`/`BlastedSteel()` in het script, niet in een tekenprogramma.
- `M_Tower_Weld` en `M_Tower_Bolts` zijn kopieën van `WeldTube` en `BoltDecal` (ze houden hun lasrups- en bouttextures), maar met RVS-waarden. Stap 1 kopieert ze telkens opnieuw van die oude materialen, dus pas de oude niet meer aan.
- Geborsteld RVS: de lijnen lopen langs U. Staan ze de verkeerde kant op, draai dan de UV's 90° in Blender. Met tiling in Unity kun je de richting niet draaien.
- Na stap 2: belichting opnieuw baken.

### Vloermarkering (zwart-geel)
- `FloorMarkingSetup.cs` (`Torens → Vloermarkering rond torens en buizen`) meet torens en bundelbuizen uit de mesh van `TowerConstellation` (dezelfde herkenning als `Torenlayout meten`, daarom zijn die helpers in `TowerLayoutProbe` `internal`) en legt een ring om elke toren en twee stroken langs elke buis, van ring tot ring.
- Geen URP Decal Projectors (DBuffer is te duur op de Quest en screen-space decals hebben geen smoothness), maar één platte mesh `Models/Room/FloorMarkings.asset` 2 mm boven de vloer, op het object `FloorMarkings` in de scene. Eén draw call, geen schaduw, wel lightmap-UV's (Contribute GI).
- Materiaal `Materials/Room/M_FloorHazard` (URP Lit, metallic 0, smoothness ±0.75 uit de map, matter op slijtplekken) met procedurele textures `Textures/Room/T_FloorHazard_*`. Eén tegel is 12 × 12 cm; uiterlijk aanpassen in `Hazard()`, maten in de constanten bovenaan.
- Vloerhoogte via een raycast naast de eerste toren; zonder vloer-collider de onderkant van `TowerConstellation` (staat in de Console). Opnieuw draaien is veilig (GUID's blijven). Daarna opnieuw baken.

### Torens doorzichtig maken (`TowerTransparency`)
- `Scripts/Rendering/TowerTransparency.cs` op `TowerConstellation`. Wisselt tijdens een fade naar runtime-kopieën van de `_Clear`-materialen (Surface Type Transparent, Alpha blend, Render Face Both, Preserve Specular uit) en zet `_BaseColor.a` lager. Is de toren weer dicht, dan krijgt hij de opaque materialen terug.
- Aansturen: `SetClear(true/false)` (met fade), `SetClearImmediate(...)`, `ToggleClear()`. Of automatisch: vul `levelManager` in en zet levelnummers (vanaf 1) in `clearInLevels` (standaard level 1).
- De `_Clear`-materialen moeten als asset bestaan en via het component in de scene hangen, anders haalt Unity de transparante shadervariant uit een build.
- Bij alpha clip (bouten) gaat `_Cutoff` mee omlaag met de alpha.
- Beperkingen: het hele `TowerConstellation`-model gaat tegelijk doorzichtig (het is één object). Transparant met ZWrite uit kan binnen één mesh kleine sorteerfoutjes geven. De gebakken schaduw van de tanks blijft liggen.

## Openstaande punten (opruimen als er tijd is)

- `Models/Props2/ET_ControlRoom_Props.fbx` is een nieuwere, **ongebruikte** kopie van `Models/Room/ET_ControlRoom_Props.fbx` (de scene gebruikt die in `Room/`). Kies er één.
- Tower-prefabs: `Tower_Scene` gebruikt `ETpf_Tower 1.prefab` (34 MB). `ETpf_Tower.prefab` (65 MB) en `Tower (1).prefab` worden door geen enkele scene gebruikt.
- In `Materials/Tower/` staan downloadresten van de Metal011-texture (`.blend`, `.usdc`, `.mtlx`, `.tres`) die Unity niet nodig heeft.
- `Materials/Room/Material.mat` en `Light_Frame 1.mat` hebben standaardnamen uit een FBX-import. Controleren of ze nog gebruikt worden.
- De XR Hands-sample is van 1.8.1 naar 1.9.0 gegaan (map `Samples/XR Hands/1.9.0/`). De oude map is verwijderd en de nieuwe moet nog gecommit worden.

## Valkuilen (al eens tegengekomen)

- **`??` en `?.` op Unity-objecten (alles wat van `UnityEngine.Object` afleidt) slaan Unity's eigen null-check over.** Een vernietigd of in de Inspector leeg gelaten object geldt dan als "niet null". Bij `GetOrAdd`-patronen gaat dat echt mis: gebruik `if (!x) x = AddComponent<T>();`. Bij een aanroep als `panel?.YawLeft()` op een veld dat altijd gevuld is, is het in de praktijk onschuldig. Gewone C#-objecten en events (`AnyStepped?.Invoke`) zijn geen probleem. Carlo's laser-scripts gebruiken het op die onschuldige manier; laat dat zo.
- **Glas en screen-space refraction veroorzaken motion sickness in VR**, want het verschilt per oog en beweegt mee met het hoofd. Het oude `SG_Glass` deed precies dat (Normal Vector → Swizzle → + Screen Position → Scene Color). Maak glas zichtbaar met Fresnel, reflecties via een reflection probe, en randen of vuil. Refraction alleen in world space, per oog geprojecteerd (zoals `ThickGlass`), en nooit met golvende normal maps.
- **Metallic materialen worden zwart zonder reflection probe.** Controleer bij donker metaal eerst of het object binnen een gebakken probe valt.
- **Reflection probe Blend Distance** kan een object op de grens van twee probes de "verkeerde" reflectie geven (zie `RP_ControlRoom` hierboven).
- **OpenXR Composition Layers geeft een stroom NullReferenceExceptions** (`OpenXRLayerProvider.SetActiveLayersForHandlers`) zodra er een echte headset aan hangt. Dit is een bekende Unity-bug. Composition Layers werd binnengehaald door de packages *Unity OpenXR Android XR* en *Unity OpenXR Meta*; die zijn daarom verwijderd. **Voeg ze niet opnieuw toe**, tenzij er echt mixed reality (passthrough of AR Foundation) nodig is.
- Na het verwijderen van die packages kunnen er nog **"missing script"-componenten** of lege feature-verwijzingen in `Assets/XR/Settings/` achterblijven. Gevonden: 28 verweesde features in `OpenXRPackageSettings.asset`, een `CompositionLayer` + `CompositionOutline` op `Global Volume` en een verborgen object `Default Scene Layer` in `Room_Scene`. Opruimen met `Tools → Einstein Telescope → XR → Restanten Composition Layers opruimen` en daarna de scene opslaan. De map `Assets/CompositionLayers/` (achtergebleven instellingen) is verwijderd.
- **Testen met een gamepad:** `LaserGamepadControls` (DualSense of Xbox): linker stick = vloeiend kantelen, D-pad = losse stap, L1/R1 = spiegel kiezen, ✕ = fijn/grof, □ = reset, △ = undo, ○ = sluiter, Options = volgend level, Create = briefing. De XR Device Simulator zelf heeft alleen toetsenbord- en muisbindingen. Draait Steam, zet dan Steam Input voor PlayStation-controllers uit, anders onderschept Steam de DualSense.
- **XR Device Simulator uitzetten bij testen met de echte headset.** Met "Remove Other HMD Devices" aan verbergt hij de Quest.
- Orthografische camera's mogen alleen naar een RenderTexture renderen (mirror camera, UI-camera), nooit de XR-camera zijn.

## Blender → Unity-pipeline

- Modelleren in Blender, exporteren als **FBX** (niet `.blend` in Unity).
- Exportinstellingen: Forward **-Z**, Up **Y**, Apply Scalings: FBX All, **Apply Transform AAN**, Apply Modifiers AAN.
- In Unity: **Bake Axis Conversion UIT**. Nooit beide tegelijk, anders draait het object 90° extra.
- Pas eerst scale toe (Ctrl+A) en zorg dat normals naar buiten wijzen (Overlays → Face Orientation: alles blauw; rood herstellen met Shift+N). Hiërarchieën met geroteerde empties eerst platslaan.
- Materiaalnamen in de FBX moeten exact overeenkomen met de Unity-materialen (let op spaties en underscores), zodat remapping werkt.
- Glas: randvlakken krijgen een eigen material slot (`GlassEdge`). Selecteer voor- en achterkant, Ctrl+I, Assign.
- Props delen zoveel mogelijk één palette-materiaal, voor minder draw calls. Dit moet draaien op een Quest, dus performance telt.

## Werkafspraken voor Claude Code

- Werk altijd op een **branch**, nooit direct op `main`.
- **Negeer `Library/`, `Temp/`, `Logs/`, `UserSettings/` en `obj/`.** Dat is cache.
- **Pas `.unity`- en `.prefab`-YAML niet met de hand aan**, tenzij daar expliciet om gevraagd wordt. Lezen en inspecteren mag wel. Scenewerk gebeurt in de Unity Editor.
- **Maak of verander nooit `.meta`-bestanden of GUIDs.** Laat Unity die genereren. Een asset zonder `.meta` niet committen.
- Lever bij een nieuw `.mat`-bestand geen vervanging van een bestaand materiaal (nieuwe GUID betekent kapotte koppelingen). Pas het bestaande materiaal aan.
- Grote ProBuilder-prefabs (bijv. `ETpf_Tower.prefab`, ±65 MB) liever naar FBX omzetten dan verder laten groeien.
- Unity kan open staan: zeg erbij wanneer een scene of asset herladen moet worden. Errors uit de Console geeft de gebruiker door als tekst of screenshot.
- Vibecoding (AI-geschreven code) is toegestaan in dit vak, maar houd scripts leesbaar zodat het team ze begrijpt.
- Op deze Windows-machine is **geen Python** geïnstalleerd (de `python`-alias opent de Microsoft Store). Gebruik Git Bash-tools (`grep`, `sed`, `awk`) of PowerShell.
- Ontbrekende scripts opsporen zonder Unity: verzamel alle `guid:`-regels uit `*.meta` in `Assets/` en `Library/PackageCache/`, en vergelijk die met de `m_Script: {… guid: …}`-regels in een scene of asset.
