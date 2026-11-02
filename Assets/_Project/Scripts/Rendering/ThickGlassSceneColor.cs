using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Vertelt de shader XR/ThickGlass per camera of URP een Opaque Texture heeft.
/// Zonder die texture (Performance URP Config, dus een losse Quest-build) zet hij
/// _THICKGLASS_NO_SCENE_COLOR aan: het glas wordt dan gewoon doorzichtig geblend
/// in plaats van de achtergrond uit de opaque texture te lezen (die is dan leeg → zwart raam).
/// Geen component nodig: registreert zichzelf bij het laden, in Play Mode en in de editor.
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
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;   // nooit dubbel registreren
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        bool hasOpaqueTexture;
        if (camera.TryGetComponent(out UniversalAdditionalCameraData data))
            hasOpaqueTexture = data.requiresColorTexture;   // camera-instelling, of die van de URP Asset
        else
            hasOpaqueTexture = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
                               && urp.supportsCameraOpaqueTexture;

        Shader.SetKeyword(keyword, !hasOpaqueTexture);
    }
}
