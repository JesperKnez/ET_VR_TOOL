using UnityEngine;

namespace ETVRTool.CraneLevel
{
    /// <summary>Turns a world-space label towards the player (only around the vertical axis).</summary>
    public class FaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 d = transform.position - cam.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
