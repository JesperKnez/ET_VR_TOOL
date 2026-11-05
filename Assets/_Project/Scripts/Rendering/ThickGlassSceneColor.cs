using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tells the XR/ThickGlass shader per camera whether URP has an Opaque Texture.
/// Without that texture (Performance URP Config, i.e. a standalone Quest build) it enables
/// _THICKGLASS_NO_SCENE_COLOR: the glass is then simply blended transparently
/// instead of reading the background from the opaque texture (which would be empty → black window).
/// No component needed: registers itself on load, in Play Mode and in the editor.
/// </summary>
public static class ThickGlassSceneColor
{
    const string KEYWORD = "_THICKGLASS_NO_SCENE_COLOR";
    static GlobalKeyword keyword;

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        keyword = GlobalKeyword.Create(KEYWORD);
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;   // never register twice
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        bool hasOpaqueTexture;
        if (camera.TryGetComponent(out UniversalAdditionalCameraData data))
            hasOpaqueTexture = data.requiresColorTexture;   // camera setting, or the one from the URP Asset
        else
            hasOpaqueTexture = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
                               && urp.supportsCameraOpaqueTexture;

        Shader.SetKeyword(keyword, !hasOpaqueTexture);
    }
}
