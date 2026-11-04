using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Eén per controller. Schiet een ray vanaf rayOrigin; raakt hij een CurvedScreen,
/// dan worden hover / klik / drag (knoppen, toggles, sliders) naar de canvas doorgestuurd.
/// </summary>
public class CurvedScreenPointer : MonoBehaviour
{
    [Tooltip("Aim-transform van de controller (zelfde oorsprong/richting als de XR-ray)")]
    public Transform rayOrigin;

    [Tooltip("Bijv. XRI Right Interaction/UI Press")]
    public InputActionReference pressAction;

    [Tooltip("Layer van het gebogen scherm")]
    public LayerMask screenMask;

    public float maxDistance = 10f;

    PointerEventData data;
    GameObject hovered, pressed, dragTarget;
    Vector2 lastPos;

    // Geen ?. op Unity-objecten (InputActionReference is een ScriptableObject): nep-null wordt dan gemist
    void OnEnable()
    {
        if (pressAction && pressAction.action != null) pressAction.action.Enable();
    }

    void Update()
    {
        var es = EventSystem.current;
        if (es == null || rayOrigin == null || !pressAction || pressAction.action == null) return;
        data ??= new PointerEventData(es) { button = PointerEventData.InputButton.Left };

        // 1. Raycast naar het scherm → UV → UI-element
        GameObject target = null;
        if (Physics.Raycast(rayOrigin.position, rayOrigin.forward, out var hit, maxDistance, screenMask)
            && hit.collider.TryGetComponent(out CurvedScreen screen))
        {
            target = screen.RaycastUI(hit.textureCoord, data);
        }
        else
        {
            data.pointerCurrentRaycast = default;
        }

        data.delta = data.position - lastPos;
        lastPos = data.position;

        // 2. Hover (enter/exit)
        if (target != hovered)
        {
            if (hovered) ExecuteEvents.ExecuteHierarchy(hovered, data, ExecuteEvents.pointerExitHandler);
            if (target) ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerEnterHandler);
            hovered = target;
            data.pointerEnter = target;
        }

        var action = pressAction.action;

        // 3. Indrukken
        if (action.WasPressedThisFrame() && target)
        {
            data.pressPosition = data.position;
            data.pointerPressRaycast = data.pointerCurrentRaycast;
            data.eligibleForClick = true;
            data.dragging = false;

            pressed = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
            if (!pressed) pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            data.pointerPress = pressed;
            data.rawPointerPress = target;

            dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            data.pointerDrag = dragTarget;
            if (dragTarget) ExecuteEvents.Execute(dragTarget, data, ExecuteEvents.initializePotentialDrag);
        }

        // 4. Slepen (sliders, scroll views)
        if (action.IsPressed() && dragTarget)
        {
            if (!data.dragging &&
                (data.position - data.pressPosition).sqrMagnitude > es.pixelDragThreshold * es.pixelDragThreshold)
            {
                ExecuteEvents.Execute(dragTarget, data, ExecuteEvents.beginDragHandler);
                data.dragging = true;
            }
            if (data.dragging) ExecuteEvents.Execute(dragTarget, data, ExecuteEvents.dragHandler);
        }

        // 5. Loslaten → up + click
        if (action.WasReleasedThisFrame())
        {
            if (pressed) ExecuteEvents.Execute(pressed, data, ExecuteEvents.pointerUpHandler);

            var clickHandler = target ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) : null;
            if (pressed && pressed == clickHandler && data.eligibleForClick && !data.dragging)
                ExecuteEvents.Execute(pressed, data, ExecuteEvents.pointerClickHandler);

            if (data.dragging && dragTarget) ExecuteEvents.Execute(dragTarget, data, ExecuteEvents.endDragHandler);

            data.dragging = false;
            data.eligibleForClick = false;
            data.pointerPress = null;
            data.pointerDrag = null;
            pressed = null;
            dragTarget = null;
        }
    }

    void OnDisable()
    {
        if (data != null)
        {
            // Een ingedrukte knop of slider netjes loslaten, anders blijft hij "vast" hangen
            if (pressed) ExecuteEvents.Execute(pressed, data, ExecuteEvents.pointerUpHandler);
            if (data.dragging && dragTarget) ExecuteEvents.Execute(dragTarget, data, ExecuteEvents.endDragHandler);
            if (hovered) ExecuteEvents.ExecuteHierarchy(hovered, data, ExecuteEvents.pointerExitHandler);
            data.dragging = false;
            data.pointerPress = null;
            data.pointerDrag = null;
        }
        hovered = pressed = dragTarget = null;
    }
}
