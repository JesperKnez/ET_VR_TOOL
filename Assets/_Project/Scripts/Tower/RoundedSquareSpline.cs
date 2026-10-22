using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

// Put this on the same GameObject as the Spline Container (and Spline Extrude).
// Right-click the component header > "Build" (or change a value in play mode).
[RequireComponent(typeof(SplineContainer))]
public class RoundedSquareSpline : MonoBehaviour
{
    public float width = 1f;          // size along X
    public float depth = 1f;          // size along Z
    public float cornerRadius = 0.04f; // keep this at least 2x the Spline Extrude radius

    [ContextMenu("Build")]
    public void Build()
    {
        var spline = GetComponent<SplineContainer>().Spline;
        spline.Clear();

        float hw = width * 0.5f, hd = depth * 0.5f;
        float r = Mathf.Min(cornerRadius, Mathf.Min(hw, hd));
        float k = 0.5523f * r; // handle length for a quarter circle

        // Two knots per side, one at each end of the straight part.
        Vector3[] pos =
        {
            new Vector3(-hw + r, 0, -hd), new Vector3(hw - r, 0, -hd),
            new Vector3(hw, 0, -hd + r),  new Vector3(hw, 0, hd - r),
            new Vector3(hw - r, 0, hd),   new Vector3(-hw + r, 0, hd),
            new Vector3(-hw, 0, hd - r),  new Vector3(-hw, 0, -hd + r)
        };
        Vector3[] dir =
        {
            Vector3.right, Vector3.right,
            Vector3.forward, Vector3.forward,
            Vector3.left, Vector3.left,
            Vector3.back, Vector3.back
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
