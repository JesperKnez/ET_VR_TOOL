using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

// Put this on the same GameObject as a Spline Container (and Spline Extrude).
// It builds a closed horizontal circle in the XZ plane, centred on the object.
// Right-click the component header > "Build" after changing the radius.
[RequireComponent(typeof(SplineContainer))]
public class CircleSpline : MonoBehaviour
{
    public float radius = 1.4f;   // radius of the path (the centre of the weld tube)

    [ContextMenu("Build")]
    public void Build()
    {
        var spline = GetComponent<SplineContainer>().Spline;
        spline.Clear();

        float k = 0.5523f * radius; // handle length for a quarter circle

        Vector3[] pos =
        {
            new Vector3(radius, 0, 0),
            new Vector3(0, 0, radius),
            new Vector3(-radius, 0, 0),
            new Vector3(0, 0, -radius)
        };
        Vector3[] dir =
        {
            Vector3.forward,
            Vector3.left,
            Vector3.back,
            Vector3.right
        };

        for (int i = 0; i < pos.Length; i++)
        {
            var knot = new BezierKnot(
                (float3)pos[i],
                new float3(0, 0, -k),
                new float3(0, 0, k),
                Quaternion.LookRotation(dir[i]));
            spline.Add(knot, TangentMode.Broken);
        }
        spline.Closed = true;
    }

    void Reset() => Build();
}
