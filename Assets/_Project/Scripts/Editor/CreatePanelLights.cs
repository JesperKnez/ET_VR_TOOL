using UnityEditor;
using UnityEngine;

// Recreates the 117 ceiling lights from XR Room.blend (collection "PanelLights")
// as baked Rectangle (area) lights in Unity.
// Blender -> Unity: (x, y, z) -> (-x, z, -y), same conversion as the FBX export.
public static class CreatePanelLights
{
    const float Spacing = 2.4f;
    const float StartX = -15.6f;   // Blender X of column 0
    const float StartY = -9.6f;    // Blender Y of row 0
    const float Height = 7.95f;    // Blender Z (just below the 8 m ceiling)
    const int Columns = 14;
    const int Rows = 9;
    const int ShortColumnsFrom = 11; // columns 11-13 only have 6 rows (above the control room)
    const int ShortRows = 6;

    [MenuItem("Tools/ET/Create Panel Lights")]
    static void Create()
    {
        var existing = GameObject.Find("PanelLights");
        if (existing != null &&
            !EditorUtility.DisplayDialog("PanelLights already exists",
                "There is already a PanelLights object in the scene. Replace it?", "Replace", "Cancel"))
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

        // 4 lights in the control room (Blender coordinates, inner ceiling at 5.8 m)
        var cr = new GameObject("ControlRoomLights");
        cr.transform.SetParent(root.transform, false);
        float[,] crPos = { { 11.35f, 5.2f }, { 14.95f, 5.2f }, { 11.35f, 8.8f }, { 14.95f, 8.8f } };
        for (int k = 0; k < 4; k++)
        {
            AddLight(cr.transform, $"ControlRoomLight_{k}", crPos[k, 0], crPos[k, 1], 5.75f, ControlRoomIntensity);
            count++;
        }

        Selection.activeGameObject = root;
        Debug.Log($"[CreatePanelLights] Created {count} baked area lights.");
    }

    const float HallIntensity = 70f;        // adjust after the first bake
    const float ControlRoomIntensity = 40f; // small room, needs less
    const float Temperature = 5500f;        // neutral-cool white, like the LED panels in ETpathfinder

    static void AddLight(Transform parent, string name, float bx, float by, float bz, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(-bx, bz, -by);          // Blender -> Unity
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);      // shine downwards

        var light = go.AddComponent<Light>();
        light.type = LightType.Rectangle;
        light.areaSize = new Vector2(0.6f, 0.6f);                         // 600x600 LED panels
        light.lightmapBakeType = LightmapBakeType.Baked;
        light.color = Color.white;
        light.useColorTemperature = true;
        light.colorTemperature = Temperature;
        light.intensity = intensity;
        light.range = 15f;
    }
}
