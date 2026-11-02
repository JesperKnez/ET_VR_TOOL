using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Op het gebogen scherm-object (met MeshCollider).
/// Zet een UV-hit op het scherm om naar een UI-raycast op de verborgen canvas
/// die door uiCamera naar de RenderTexture wordt gerenderd.
/// </summary>
[RequireComponent(typeof(MeshCollider))]
public class CurvedScreen : MonoBehaviour
{
    [Tooltip("Camera die de canvas naar de RenderTexture rendert")]
    public Camera uiCamera;

    [Tooltip("GraphicRaycaster op de verborgen canvas (Screen Space - Camera)")]
    public GraphicRaycaster raycaster;

    [Tooltip("Aanzetten als klikken gespiegeld uitkomen")]
    public bool flipX;
    public bool flipY;

    static readonly List<RaycastResult> results = new();

    /// <summary>Geeft het geraakte UI-element terug (of null) en vult data.position / pointerCurrentRaycast.</summary>
    public GameObject RaycastUI(Vector2 uv, PointerEventData data)
    {
        if (flipX) uv.x = 1f - uv.x;
        if (flipY) uv.y = 1f - uv.y;

        var rt = uiCamera.targetTexture;
        data.position = new Vector2(uv.x * rt.width, uv.y * rt.height);

        results.Clear();
        raycaster.Raycast(data, results); // al gesorteerd, bovenste element eerst

        data.pointerCurrentRaycast = results.Count > 0 ? results[0] : default;
        return results.Count > 0 ? results[0].gameObject : null;
    }
}
