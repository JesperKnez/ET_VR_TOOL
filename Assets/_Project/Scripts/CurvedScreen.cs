using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Goes on the curved screen object (with a MeshCollider).
/// Converts a UV hit on the screen into a UI raycast on the hidden canvas
/// that uiCamera renders into the RenderTexture.
/// </summary>
[RequireComponent(typeof(MeshCollider))]
public class CurvedScreen : MonoBehaviour
{
    [Tooltip("Camera that renders the canvas into the RenderTexture")]
    public Camera uiCamera;

    [Tooltip("GraphicRaycaster on the hidden canvas (Screen Space - Camera)")]
    public GraphicRaycaster raycaster;

    [Tooltip("Enable if clicks come out mirrored")]
    public bool flipX;
    public bool flipY;

    static readonly List<RaycastResult> results = new();

    /// <summary>Returns the UI element that was hit (or null) and fills data.position / pointerCurrentRaycast.</summary>
    public GameObject RaycastUI(Vector2 uv, PointerEventData data)
    {
        if (flipX) uv.x = 1f - uv.x;
        if (flipY) uv.y = 1f - uv.y;

        if (!uiCamera || !raycaster || !uiCamera.targetTexture)
        {
            data.pointerCurrentRaycast = default;
            return null;
        }

        var rt = uiCamera.targetTexture;
        data.position = new Vector2(uv.x * rt.width, uv.y * rt.height);

        results.Clear();
        raycaster.Raycast(data, results); // already sorted, topmost element first

        data.pointerCurrentRaycast = results.Count > 0 ? results[0] : default;
        return results.Count > 0 ? results[0].gameObject : null;
    }
}
