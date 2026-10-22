using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// The "beam cameras" screen above the control-room monitor: up to four live close-ups of the irises
    /// (or of the output detector when a level has no irises). The level manager tells it what to show.
    /// One view fills the screen, two sit side by side, three or four form a 2 x 2 grid.
    /// </summary>
    public class LaserBeamCameraScreen : MonoBehaviour
    {
        [SerializeField] private LaserBeamCameraView[] views = new LaserBeamCameraView[0];

        [Tooltip("Size of the usable screen area (local units) for the layout.")]
        [SerializeField] private Vector2 area = new Vector2(0.5f, 0.26f);

        [Tooltip("Centre of the usable area (below the header).")]
        [SerializeField] private Vector2 areaCenter = new Vector2(0f, -0.012f);

        [SerializeField] private Text headerText = null;

        public int ActiveViewCount { get; private set; }
        public LaserBeamCameraView[] Views => views;

        /// <summary>Show these targets (max. as many as there are views).</summary>
        public void SetTargets(LaserTarget[] targets, string[] labels)
        {
            int count = Mathf.Min(targets != null ? targets.Length : 0, views.Length);
            ActiveViewCount = count;
            float cellWidth = count <= 1 ? area.x : area.x * 0.5f;
            float cellHeight = count <= 2 ? area.y : area.y * 0.5f;
            float scale = count <= 1 ? 1.7f : count == 2 ? 1.05f : 1f;

            for (int i = 0; i < views.Length; i++)
            {
                LaserBeamCameraView view = views[i];
                if (view == null)
                {
                    continue;
                }

                bool active = i < count;
                view.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                view.SetTarget(targets[i], labels != null && i < labels.Length ? labels[i] : targets[i].name);
                int column = count <= 1 ? 0 : i % 2;
                int row = count <= 2 ? 0 : i / 2;
                float x = count <= 1 ? 0f : (column == 0 ? -0.5f : 0.5f) * cellWidth;
                float y = count <= 2 ? 0f : (row == 0 ? 0.5f : -0.5f) * cellHeight;
                view.transform.localPosition = new Vector3(areaCenter.x + x, areaCenter.y + y, view.transform.localPosition.z);
                view.transform.localScale = Vector3.one * scale;
            }

            if (headerText != null)
            {
                headerText.text = count == 1 && targets[0] != null && targets[0].Type == LaserTarget.TargetType.Photodiode
                    ? "BEAM CAMERA  |  OUTPUT DETECTOR"
                    : "BEAM CAMERAS  |  " + count + (count == 1 ? " IRIS" : " IRISES");
            }
        }
    }
}
