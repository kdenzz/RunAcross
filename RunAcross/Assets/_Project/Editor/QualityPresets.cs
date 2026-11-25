using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RunAcross.EditorTools
{
    /// <summary>
    /// Three quality levels (Low / Medium / High, indices match <see cref="Core.GraphicsQuality"/>), each
    /// with its own URP asset copied from PC_RPAsset. GraphicsApplier switches between them at runtime.
    /// </summary>
    public static class QualityPresets
    {
        const string Source = "Assets/Settings/PC_RPAsset.asset";
        const string Dir = "Assets/_Project/Settings";

        struct Preset
        {
            public string name;
            public float renderScale;
            public int msaa;
            public bool shadows, softShadows;
            public int shadowRes;
            public float shadowDistance;
        }

        static readonly Preset[] Presets =
        {
            new Preset { name = "Low", renderScale = 0.75f, msaa = 1, shadows = false, softShadows = false, shadowRes = 512, shadowDistance = 15f },
            new Preset { name = "Medium", renderScale = 1f, msaa = 2, shadows = true, softShadows = true, shadowRes = 1024, shadowDistance = 22f },
            new Preset { name = "High", renderScale = 1f, msaa = 4, shadows = true, softShadows = true, shadowRes = 2048, shadowDistance = 30f },
        };

        [MenuItem("RunAcross/Configure Quality Presets")]
        public static void Configure()
        {
            var assets = new UniversalRenderPipelineAsset[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                var p = Presets[i];
                string path = $"{Dir}/RP_{p.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path) == null)
                    AssetDatabase.CopyAsset(Source, path);
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                var so = new SerializedObject(rp);
                Set(so, "m_RenderScale", p.renderScale);
                Set(so, "m_MSAA", p.msaa);
                Set(so, "m_SupportsHDR", true);
                Set(so, "m_MainLightShadowsSupported", p.shadows);
                Set(so, "m_AdditionalLightShadowsSupported", false);
                Set(so, "m_SoftShadowsSupported", p.softShadows);
                Set(so, "m_MainLightShadowmapResolution", p.shadowRes);
                Set(so, "m_ShadowDistance", p.shadowDistance);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rp);
                assets[i] = rp;
            }
            AssetDatabase.SaveAssets();

            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var qso = new SerializedObject(qs);
            var levels = qso.FindProperty("m_QualitySettings");
            levels.arraySize = Presets.Length; // new entries copy the last one (PC), which is a sane base
            for (int i = 0; i < Presets.Length; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("name").stringValue = Presets[i].name;
                level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                level.FindPropertyRelative("vSyncCount").intValue = 1;
                level.FindPropertyRelative("antiAliasing").intValue = 0; // URP owns MSAA
                // the template's levels were per-platform (Mobile excluded Standalone); ours apply everywhere
                level.FindPropertyRelative("excludedTargetPlatforms").arraySize = 0;
            }
            var perPlatform = qso.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < perPlatform.arraySize; i++)
                perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = Presets.Length - 1;
            qso.FindProperty("m_CurrentQuality").intValue = Presets.Length - 1;
            qso.ApplyModifiedPropertiesWithoutUndo();

            GraphicsSettings.defaultRenderPipeline = assets[Presets.Length - 1];
            QualitySettings.SetQualityLevel(Presets.Length - 1, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[RunAcross] Quality presets: Low / Medium / High");
        }

        static void Set(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[RunAcross] URP asset has no " + prop); return; }
            if (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum) p.intValue = (int)v;
            else p.floatValue = v;
        }

        static void Set(SerializedObject so, string prop, bool v)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[RunAcross] URP asset has no " + prop); return; }
            p.boolValue = v;
        }
    }
}
