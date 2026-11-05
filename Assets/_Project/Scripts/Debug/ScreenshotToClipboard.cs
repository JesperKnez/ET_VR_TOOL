using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Screenshot to the clipboard, without UI and without controllers, for sharing.
/// Creates itself on Play (editor and development builds only), so nothing is needed in the scene.
///   F10   screenshot of the Game view, exactly as you see it now (with the simulator overlays)
///   F8    clean picture (1920 x 1080) in the player's view direction, without controllers and overlays
///   F9    360° picture (equirectangular, 4096 x 2048) from the player's position, also without controllers
/// The image is then on the clipboard (paste into Slack, Discord, Word...) and saved as a PNG in the Screenshots
/// folder next to Assets. Only the world is rendered: the XR Device Simulator overlay, the controllers, hands,
/// rays and tooltips are left out.
/// Note: a cubemap render gets no post-processing (bloom, tonemapping), so the colours differ slightly
/// from what you see in Play Mode.
/// </summary>
public class ScreenshotToClipboard : MonoBehaviour
{
    const string RIG_NAME = "XR Origin Hands (XR Rig)";
    const int CUBE_SIZE = 2048;
    const int EQUIRECT_WIDTH = 4096;
    const int FLAT_WIDTH = 1920;
    const int FLAT_HEIGHT = 1080;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("Screenshot To Clipboard");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenshotToClipboard>();
    }
#endif

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f9Key.wasPressedThisFrame) Capture(true);
        if (keyboard.f8Key.wasPressedThisFrame) Capture(false);
        if (keyboard.f10Key.wasPressedThisFrame) StartCoroutine(CaptureGameView());
    }

    /// <summary>Exactly what you see in the Game view now (including the simulator overlays), at the size of the Game window.</summary>
    System.Collections.IEnumerator CaptureGameView()
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
        Save(texture, "view");
        Destroy(texture);
    }

    static void Capture(bool is360)
    {
        Camera main = Camera.main;
        if (!main) { UnityEngine.Debug.LogWarning("[Screenshot] No Main Camera."); return; }

        var hidden = HideRig(main);
        var cameraObject = new GameObject("Screenshot Camera");
        try
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.CopyFrom(main);
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = null;
            camera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);

            Texture2D texture = is360 ? Render360(camera) : RenderFlat(camera);
            Save(texture, is360 ? "360" : "flat");
            Destroy(texture);
        }
        finally
        {
            Destroy(cameraObject);
            Restore(hidden);
        }
    }

    static Texture2D Render360(Camera camera)
    {
        var cube = new RenderTexture(CUBE_SIZE, CUBE_SIZE, 24) { dimension = TextureDimension.Cube };
        var equirect = new RenderTexture(EQUIRECT_WIDTH, EQUIRECT_WIDTH / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        camera.RenderToCubemap(cube, 63);
        cube.ConvertToEquirect(equirect, Camera.MonoOrStereoscopicEye.Mono);
        Texture2D texture = ReadBack(equirect);
        cube.Release(); Destroy(cube);
        equirect.Release(); Destroy(equirect);
        return texture;
    }

    static Texture2D RenderFlat(Camera camera)
    {
        camera.aspect = (float)FLAT_WIDTH / FLAT_HEIGHT;
        if (camera.fieldOfView < 1f) camera.fieldOfView = 75f;
        var target = new RenderTexture(FLAT_WIDTH, FLAT_HEIGHT, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = null;
        Texture2D texture = ReadBack(target);
        target.Release(); Destroy(target);
        return texture;
    }

    static Texture2D ReadBack(RenderTexture source)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = source;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        return texture;
    }

    static void Save(Texture2D texture, string kind)
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"{kind}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());

        bool copied = CopyToClipboard(path);
        UnityEngine.Debug.Log($"[Screenshot] {(kind == "360" ? "360° picture" : "Picture")} saved: {path}" + (copied ? " (copied to the clipboard)" : " (clipboard not available on this platform)"));
    }

    /// <summary>Windows: puts the PNG file on the clipboard as an image, via PowerShell (needs an STA thread).</summary>
    static bool CopyToClipboard(string path)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            string escaped = path.Replace("'", "''");
            string script = "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
                            $"$i = [System.Drawing.Image]::FromFile('{escaped}'); [System.Windows.Forms.Clipboard]::SetImage($i); $i.Dispose()";
            Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -STA -WindowStyle Hidden -Command \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return true;
        }
        catch (System.Exception exception)
        {
            UnityEngine.Debug.LogWarning("[Screenshot] Clipboard failed: " + exception.Message);
        }
#endif
        return false;
    }

    // ------------------------------------------------------------------ hide controllers, hands, rays and tooltips

    struct Hidden
    {
        public List<Renderer> renderers;
        public List<Canvas> canvases;
    }

    static Hidden HideRig(Camera main)
    {
        var rig = GameObject.Find(RIG_NAME);
        Transform root = rig ? rig.transform : main.transform.root;

        var hidden = new Hidden { renderers = new List<Renderer>(), canvases = new List<Canvas>() };
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer.forceRenderingOff) continue;
            renderer.forceRenderingOff = true;
            hidden.renderers.Add(renderer);
        }

        foreach (var canvas in root.GetComponentsInChildren<Canvas>(false))
        {
            if (!canvas.enabled) continue;
            canvas.enabled = false;
            hidden.canvases.Add(canvas);
        }

        return hidden;
    }

    static void Restore(Hidden hidden)
    {
        foreach (var renderer in hidden.renderers) if (renderer) renderer.forceRenderingOff = false;
        foreach (var canvas in hidden.canvases) if (canvas) canvas.enabled = true;
    }
}
