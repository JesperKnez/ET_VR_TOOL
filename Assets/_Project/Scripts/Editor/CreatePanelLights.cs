using UnityEditor;
using UnityEngine;

// Maakt de 117 plafondlampen uit XR Room.blend (collectie "PanelLights") na
// als baked Rectangle (area) lights in Unity.
// Blender -> Unity: (x, y, z) -> (-x, z, -y), zelfde conversie als de FBX-export.
public static class CreatePanelLights
{
    const float Spacing = 2.4f;
    const float StartX = -15.6f;   // Blender X van kolom 0
    const float StartY = -9.6f;    // Blender Y van rij 0
    const float Height = 7.95f;    // Blender Z (net onder het plafond van 8 m)
    const int Columns = 14;
    const int Rows = 9;
    const int ShortColumnsFrom = 11; // kolommen 11-13 hebben maar 6 rijen (boven de controlekamer)
    const int ShortRows = 6;

    [MenuItem("Tools/ET/Create Panel Lights")]
    static void Create()
    {
        var existing = GameObject.Find("PanelLights");
        if (existing != null &&
            !EditorUtility.DisplayDialog("PanelLights bestaat al",
                "Er staat al een PanelLights-object in de scene. Vervangen?", "Vervangen", "Annuleren"))
            return;
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var root = new GameObject("PanelLights");
        Undo.RegisterCreatedObjectUndo(root, "Create Panel Lights");
        GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.ContributeGI);

        int count = 0;
        for (int i = 0; i < Columns; i++)
        {
            int rows = i >= ShortColumnsFrom ? ShortRows : Rows;
            for (int j = 0; j < rows; j++)
            {
                float bx = StartX + i * Spacing;
                float by = StartY + j * Spacing;
                AddLight(root.transform, $"PanelLight_{i:00}_{j}", bx, by, Height, HallIntensity);
                count++;
            }
        }

        // 4 lampen in de controlekamer (Blender-coordinaten, plafond binnen op 5.8 m)
        var cr = new GameObject("ControlRoomLights");
        cr.transform.SetParent(root.transform, false);
        float[,] crPos = { { 11.35f, 5.2f }, { 14.95f, 5.2f }, { 11.35f, 8.8f }, { 14.95f, 8.8f } };
        for (int k = 0; k < 4; k++)
        {
            AddLight(cr.transform, $"ControlRoomLight_{k}", crPos[k, 0], crPos[k, 1], 5.75f, ControlRoomIntensity);
            count++;
        }

        Selection.activeGameObject = root;
        Debug.Log($"[CreatePanelLights] {count} baked area lights aangemaakt.");
    }

    const float HallIntensity = 70f;        // bijstellen na de eerste bake
    const float ControlRoomIntensity = 40f; // kleine ruimte, minder nodig
    const float Temperature = 5500f;        // neutraal-koel wit, zoals de LED-panelen in ETpathfinder

    static void AddLight(Transform parent, string name, float bx, float by, float bz, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(-bx, bz, -by);          // Blender -> Unity
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);      // naar beneden schijnen

        var light = go.AddComponent<Light>();
        light.type = LightType.Rectangle;
        light.areaSize = new Vector2(0.6f, 0.6f);                         // 600x600 LED-panelen
        light.lightmapBakeType = LightmapBakeType.Baked;
        light.color = Color.white;
        light.useColorTemperature = true;
        light.colorTemperature = Temperature;
        light.intensity = intensity;
        light.range = 15f;
    }
}
