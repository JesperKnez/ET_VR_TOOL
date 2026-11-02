// Pipe Kit snapping tool. Put this file in any folder called "Editor".
// Sockets are empty child objects named "Socket_<Type>_<Id>" (e.g. Socket_Beam_A, Socket_Util_B).
// A socket's blue arrow (forward) points OUT of its piece. Only sockets of the same <Type> snap together.
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PipeSnapper
{
    const float OccupiedDist = 0.01f;

    static string TypeOf(Transform s)
    {
        var parts = s.name.Split('_');
        return parts.Length > 1 ? parts[1] : "";
    }

    static Transform[] SocketsUnder(Transform root) =>
        root.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Socket_")).ToArray();

    // The selected object, or the parent that actually holds the sockets.
    static Transform PieceRoot(Transform t)
    {
        while (t != null && SocketsUnder(t).Length == 0) t = t.parent;
        return t;
    }

    static bool FindPair(Transform piece, out Transform mine, out Transform target)
    {
        mine = target = null;
        var myS = SocketsUnder(piece);
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                        .Where(t => t.name.StartsWith("Socket_")).ToArray();
        var others = all.Where(t => !t.IsChildOf(piece)).ToArray();
        // skip sockets that already have something attached to them
        var free = others.Where(o => !others.Any(x => x != o && TypeOf(x) == TypeOf(o)
                                                   && Vector3.Distance(x.position, o.position) < OccupiedDist)).ToArray();
        float best = float.MaxValue;
        foreach (var m in myS)
            foreach (var o in free)
            {
                if (TypeOf(m) != TypeOf(o)) continue;
                float d = Vector3.Distance(m.position, o.position);
                if (d < best) { best = d; mine = m; target = o; }
            }
        return mine != null;
    }

    [MenuItem("Tools/Pipe Kit/Snap Selected To Nearest Socket _F8")]
    static void Snap()
    {
        foreach (var sel in Selection.transforms)
        {
            var piece = PieceRoot(sel);
            if (piece == null) continue;
            if (!FindPair(piece, out var mine, out var target))
            {
                Debug.LogWarning($"Pipe Kit: no free matching socket found for {piece.name}");
                continue;
            }
            Undo.RecordObject(piece, "Snap pipe");
            // my socket must face the opposite way of the target socket, with the same 'up'
            Quaternion want = Quaternion.LookRotation(-target.forward, target.up);
            piece.rotation = want * Quaternion.Inverse(mine.rotation) * piece.rotation;
            piece.position += target.position - mine.position;
        }
    }

    [MenuItem("Tools/Pipe Kit/Roll Selected 90 Around Its Joint _F9")]
    static void Roll()
    {
        foreach (var sel in Selection.transforms)
        {
            var piece = PieceRoot(sel);
            if (piece == null) continue;
            // the socket of this piece that touches another piece = the joint to roll around
            var mine = SocketsUnder(piece).OrderBy(m =>
                Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                      .Where(t => t.name.StartsWith("Socket_") && !t.IsChildOf(piece))
                      .Select(t => Vector3.Distance(t.position, m.position))
                      .DefaultIfEmpty(float.MaxValue).Min()).FirstOrDefault();
            if (mine == null) continue;
            Undo.RecordObject(piece, "Roll pipe");
            piece.RotateAround(mine.position, mine.forward, 90f);
        }
    }

    // Draw socket arrows in the Scene view so you can see where things connect.
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.InSelectionHierarchy)]
    static void DrawSocket(Transform t, GizmoType type)
    {
        if (!t.name.StartsWith("Socket_")) return;
        Gizmos.color = TypeOf(t) == "Beam" ? new Color(0.1f, 0.6f, 1f) : new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawWireSphere(t.position, TypeOf(t) == "Beam" ? 0.12f : 0.04f);
        Gizmos.DrawRay(t.position, t.forward * (TypeOf(t) == "Beam" ? 0.6f : 0.2f));
    }
}
#endif
