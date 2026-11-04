#if UNITY_EDITOR
using EinsteinTelescope.Laser;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools → Einstein Telescope → Laser → Level voltooid-scherm op de monitor.
///
/// Builds "Panel_Complete" on the MonitorUI canvas: when a level is solved it replaces the three panels on the curved
/// monitor with a results screen (title, stars, time, safety incidents, personal best) and two buttons: RETRY and
/// NEXT LEVEL. The level manager then uses this card (a <see cref="LaserResultsCard"/>) instead of the floating one,
/// and the old NEXT LEVEL button on the control pad is removed, so players can only move on by solving the level.
/// Safe to run again: the panel is rebuilt.
/// </summary>
public static class MonitorLevelCompleteSetup
{
    const string PanelName = "Panel_Complete";
    const int W = 3840, H = 1200, MARGIN = 40;

    static readonly Color Gold = new Color(1f, 0.8f, 0.25f, 1f);
    static readonly Color Grey = new Color(0.55f, 0.65f, 0.75f, 1f);
    static readonly Color ButtonColor = new Color(0.1f, 0.16f, 0.24f, 1f);
    static readonly Color NextColor = new Color(0.12f, 0.36f, 0.22f, 1f);

    [MenuItem("Tools/Einstein Telescope/Laser/Level voltooid-scherm op de monitor", priority = 41)]
    static void Build()
    {
        var canvasObject = GameObject.Find("MonitorUI");
        if (!canvasObject)
        {
            Debug.LogError("[MonitorLevelCompleteSetup] Canvas 'MonitorUI' niet gevonden. Draai eerst Tools → ET → Setup Curved Monitor UI.");
            return;
        }

        var manager = Object.FindFirstObjectByType<LaserLevelManager>(FindObjectsInactive.Include);
        if (!manager)
        {
            Debug.LogError("[MonitorLevelCompleteSetup] Geen LaserLevelManager in de scene.");
            return;
        }

        var canvas = canvasObject.transform;
        var old = canvas.Find(PanelName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        // Panel: fills the screen inside the margins, drawn on top of the other panels (last sibling)
        var panelObject = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(panelObject, "Level voltooid-scherm");
        panelObject.transform.SetParent(canvas, false);
        var panel = (RectTransform)panelObject.transform;
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.zero;
        panel.pivot = Vector2.zero;
        panel.anchoredPosition = new Vector2(MARGIN, MARGIN);
        panel.sizeDelta = new Vector2(W - 2 * MARGIN, H - 2 * MARGIN);
        panel.SetAsLastSibling();
        var background = panelObject.GetComponent<Image>();
        background.color = new Color(0.02f, 0.04f, 0.07f, 1f);
        background.raycastTarget = true;   // blocks clicks on anything underneath

        var card = panelObject.AddComponent<LaserResultsCard>();

        Image frame = Img(panel, "Frame", Vector2.zero, panel.sizeDelta - new Vector2(30f, 30f), new Color(Gold.r, Gold.g, Gold.b, 0.5f), rounded);
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = false;
        Image inner = Img(panel, "Inner", Vector2.zero, panel.sizeDelta - new Vector2(46f, 46f), new Color(0.02f, 0.04f, 0.07f, 1f), rounded);
        inner.type = Image.Type.Sliced;
        inner.raycastTarget = false;

        Text title = Label(panel, "Title", new Vector2(0f, 420f), new Vector2(3000f, 150f), "LEVEL COMPLETE", 130, font, Gold);
        title.fontStyle = FontStyle.Bold;
        Text subtitle = Label(panel, "Subtitle", new Vector2(0f, 310f), new Vector2(3000f, 80f), "", 56, font, Grey);

        var starImages = new Image[3];
        for (int i = 0; i < starImages.Length; i++)
        {
            float size = i == 1 ? 230f : 190f;
            starImages[i] = Img(panel, "Star " + (i + 1), new Vector2((i - 1) * 260f, 170f), new Vector2(size, size), Color.white, null);
            starImages[i].raycastTarget = false;
            starImages[i].preserveAspect = true;
        }

        Label(panel, "Time Caption", new Vector2(0f, 20f), new Vector2(1600f, 60f), "COMPLETED IN", 46, font, Grey);
        Text time = Label(panel, "Time", new Vector2(0f, -85f), new Vector2(1600f, 170f), "", 160, font, Color.white);
        time.fontStyle = FontStyle.Bold;

        Text Row(string name, float y, string caption)
        {
            Text left = Label(panel, name + " Label", new Vector2(-40f, y), new Vector2(1100f, 70f), caption, 46, font, Grey);
            left.rectTransform.pivot = new Vector2(1f, 0.5f);
            left.rectTransform.anchoredPosition = new Vector2(-40f, y);
            left.alignment = TextAnchor.MiddleRight;
            Text value = Label(panel, name, new Vector2(40f, y), new Vector2(1700f, 70f), "", 50, font, Color.white);
            value.rectTransform.pivot = new Vector2(0f, 0.5f);
            value.rectTransform.anchoredPosition = new Vector2(40f, y);
            value.alignment = TextAnchor.MiddleLeft;
            return value;
        }

        Text incidents = Row("Incidents", -210f, "SAFETY INCIDENTS");
        Text best = Row("Best", -290f, "PERSONAL BEST");
        Text banner = Label(panel, "Banner", new Vector2(0f, -375f), new Vector2(3400f, 70f), "", 48, font, Gold);

        Button retryButton = MakeButton(panel, "Retry", new Vector2(-520f, -470f), new Vector2(560f, 150f), "RETRY", font, rounded, ButtonColor, out Text retryLabel);
        Button nextButton = MakeButton(panel, "Next", new Vector2(420f, -470f), new Vector2(900f, 150f), "NEXT LEVEL  ►", font, rounded, NextColor, out Text nextLabel);
        AddAction(retryButton, card, LaserResultsButton.ResultsAction.Retry);
        AddAction(nextButton, card, LaserResultsButton.ResultsAction.Next);

        // Everything on the canvas layer, otherwise the UI camera does not draw it
        SetLayerRecursive(panelObject, canvasObject.layer);

        // The three monitor panels vanish while the results screen is open
        var hide = new System.Collections.Generic.List<GameObject>();
        foreach (var name in new[] { "Panel_MirrorCam", "Panel_DPad", "Panel_HUD" })
        {
            var t = canvas.Find(name);
            if (t) hide.Add(t.gameObject);
        }

        var cardSo = new SerializedObject(card);
        cardSo.FindProperty("screen").objectReferenceValue = panelObject;
        cardSo.FindProperty("titleText").objectReferenceValue = title;
        cardSo.FindProperty("subtitleText").objectReferenceValue = subtitle;
        cardSo.FindProperty("timeText").objectReferenceValue = time;
        cardSo.FindProperty("incidentsText").objectReferenceValue = incidents;
        cardSo.FindProperty("bestText").objectReferenceValue = best;
        cardSo.FindProperty("bannerText").objectReferenceValue = banner;
        cardSo.FindProperty("nextButtonText").objectReferenceValue = nextLabel;
        SetObjects(cardSo.FindProperty("starImages"), starImages);
        SetObjects(cardSo.FindProperty("hideWhileOpen"), hide.ToArray());
        cardSo.ApplyModifiedPropertiesWithoutUndo();

        // The manager uses this card; the floating one is switched off
        var managerSo = new SerializedObject(manager);
        var previous = managerSo.FindProperty("resultsCard").objectReferenceValue as LaserResultsCard;
        managerSo.FindProperty("resultsCard").objectReferenceValue = card;
        managerSo.ApplyModifiedProperties();
        if (previous && previous != card)
        {
            Undo.RecordObject(previous.gameObject, "Zwevende resultatenkaart uit");
            previous.gameObject.SetActive(false);
        }

        // The old NEXT LEVEL button on the pad goes: moving on happens on the results screen
        int removed = 0;
        foreach (var b in Object.FindObjectsByType<LaserPadButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b.Action == LaserPadAction.NextLevel)
            {
                Undo.DestroyObjectImmediate(b.gameObject);
                removed++;
            }
        }

        // Start with the panel closed
        panelObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(panelObject.scene);
        Selection.activeGameObject = panelObject;
        Debug.Log("[MonitorLevelCompleteSetup] Panel_Complete gebouwd, resultsCard van de level manager omgezet, " + removed +
                  " NEXT LEVEL-knop(pen) van het pad verwijderd. Sla de scene op.");
    }

    // ----- helpers ------------------------------------------------------------------------------------

    static RectTransform Rect(RectTransform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static Image Img(RectTransform parent, string name, Vector2 position, Vector2 size, Color color, Sprite sprite)
    {
        var image = Rect(parent, name, position, size).gameObject.AddComponent<Image>();
        image.color = color;
        if (sprite) { image.sprite = sprite; image.type = Image.Type.Sliced; }
        return image;
    }

    static Text Label(RectTransform parent, string name, Vector2 position, Vector2 size, string value, int fontSize, Font font, Color color)
    {
        var text = Rect(parent, name, position, size).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    static Button MakeButton(RectTransform parent, string name, Vector2 position, Vector2 size, string label, Font font, Sprite sprite,
        Color normal, out Text labelText)
    {
        Image image = Img(parent, name, position, size, Color.white, sprite);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = normal;
        colors.highlightedColor = new Color(0.2f, 0.5f, 0.35f, 1f);
        colors.pressedColor = new Color(0.25f, 0.85f, 0.45f, 1f);
        colors.selectedColor = normal;
        colors.fadeDuration = 0.05f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        labelText = Label((RectTransform)image.transform, "Label", Vector2.zero, size, label, 70, font, Color.white);
        labelText.fontStyle = FontStyle.Bold;
        return button;
    }

    static void AddAction(Button button, LaserResultsCard card, LaserResultsButton.ResultsAction action)
    {
        var results = button.gameObject.AddComponent<LaserResultsButton>();
        var so = new SerializedObject(results);
        so.FindProperty("action").enumValueIndex = (int)action;
        so.FindProperty("card").objectReferenceValue = card;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetObjects(SerializedProperty array, Object[] objects)
    {
        array.arraySize = objects.Length;
        for (int i = 0; i < objects.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }
}
#endif
