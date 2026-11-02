using UnityEngine;

namespace ETVRTool.CraneLevel
{
    /// <summary>
    /// Marker: a carried load touching this object (or its children) counts as a collision.
    /// Put it on towers, walls, cabinets, etc. Not on the floor or the pipe supports.
    /// </summary>
    public class CraneObstacle : MonoBehaviour { }
}
