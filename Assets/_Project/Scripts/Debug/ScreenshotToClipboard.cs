using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Screenshot naar het klembord, zonder UI en zonder controllers, om te delen.
/// Maakt zichzelf aan bij Play (alleen in de editor en in development builds), dus niets in de scene nodig.
///   F10   screenshot van de Game view, precies zoals je hem nu ziet (met overlays van de simulator)
///   F8    schone foto (1920 x 1080) in de kijkrichting van de speler, zonder controllers en overlays
///   F9    360° foto (equirectangular, 4096 x 2048) vanaf de plek van de speler, ook zonder controllers
/// Het beeld staat daarna op het klembord (plakken in Slack, Discord, Word...) en als PNG in de map Screenshots
/// naast Assets. Alleen de wereld wordt gerenderd: de overlay van de XR Device Simulator, de controllers, handen,
/// rays en tooltips doen niet mee.
/// Let op: een cubemap-render krijgt geen post-processing (bloom, tonemapping), dus de kleuren verschillen een
/// beetje van wat je in Play Mode ziet.
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

    /// <summary>Precies wat je nu in de Game view ziet (inclusief overlays van de simulator), op de grootte van het Game-venster.</summary>
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
        if (!main) { UnityEngine.Debug.LogWarning("[Screenshot] Geen Main Camera."); return; }

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
        UnityEngine.Debug.Log($"[Screenshot] {(kind == "360" ? "360°-foto" : "Foto")} opgeslagen: {path}" + (copied ? " (staat op het klembord)" : " (klembord niet beschikbaar op dit platform)"));
    }

    /// <summary>Windows: het PNG-bestand als afbeelding op het klembord, via PowerShell (heeft een STA-thread nodig).</summary>
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
            UnityEngine.Debug.LogWarning("[Screenshot] Klembord mislukt: " + exception.Message);
        }
#endif
        return false;
    }

    // ------------------------------------------------------------------ controllers, handen, rays en tooltips verbergen

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
