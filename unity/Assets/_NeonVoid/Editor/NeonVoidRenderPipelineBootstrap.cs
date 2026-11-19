#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonVoid.EditorTools
{
    /// <summary>
    /// Creates the project's URP renderer + pipeline assets and assigns them to
    /// Graphics/Quality settings. Run this BEFORE generating any scene: materials
    /// created while no URP asset is assigned fall back to Built-in shaders and are
    /// saved that way, which does not repair itself on a later switch.
    /// Idempotent; safe to re-run. Rendering quality values here are not approved
    /// art settings, only a valid pipeline so URP shaders resolve.
    /// </summary>
    public static class NeonVoidRenderPipelineBootstrap
    {
        private const string AssetFolder = "Assets/_NeonVoid/Content/Rendering";
        private const string RendererPath = AssetFolder + "/NV_UniversalRenderer.asset";
        private const string PipelinePath = AssetFolder + "/NV_UniversalRenderPipeline.asset";

        [MenuItem("Tools/Neon Void/Setup URP Pipeline Asset")]
        public static void Setup()
        {
            Directory.CreateDirectory(AssetFolder);
            AssetDatabase.Refresh();

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipeline;

            int previousLevel = QualitySettings.GetQualityLevel();
            int levelCount = QualitySettings.names.Length;
            for (int i = 0; i < levelCount; ++i)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(previousLevel, false);

            // AssetDatabase.SaveAssets() alone does NOT flush ProjectSettings/*.asset in
            // batch mode: the in-memory assignment is lost when the Editor quits. Mark the
            // settings objects dirty and force a project save so the change reaches disk.
            EditorUtility.SetDirty(GraphicsSettings.GetGraphicsSettings());
            EditorUtility.SetDirty(QualitySettings.GetQualitySettings());
            AssetDatabase.SaveAssets();
            EditorApplication.ExecuteMenuItem("File/Save Project");
            AssetDatabase.Refresh();

            bool ok = GraphicsSettings.defaultRenderPipeline == pipeline;
            Debug.Log($"[NeonVoid] URP bootstrap: pipeline={PipelinePath} assigned={ok} qualityLevels={levelCount}");
        }
    }
}
#endif
