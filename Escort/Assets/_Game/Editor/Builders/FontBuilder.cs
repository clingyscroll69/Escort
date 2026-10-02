using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace HS.EditorTools
{
    /// <summary>TMP font assets for the UI: Share Tech Mono (OFL) for the System window. Dynamic, ASCII pre-baked.</summary>
    public static class FontBuilder
    {
        const string Src = "Assets/_Game/Art/Fonts/ShareTechMono-Regular.ttf";
        const string Dst = "Assets/_Game/Resources/UI/Fonts/ShareTechMono SDF.asset";

        [MenuItem("Tools/HS/Build/UI Fonts")]
        public static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(Src);
            if (font == null)
            {
                HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":false,\"msg\":\"font missing\"}");
                return;
            }
            System.IO.Directory.CreateDirectory("Assets/_Game/Resources/UI/Fonts");
            AssetDatabase.DeleteAsset(Dst);
            var fa = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = "ShareTechMono SDF";
            AssetDatabase.CreateAsset(fa, Dst);
            fa.atlasTextures[0].name = "ShareTechMono SDF Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = "ShareTechMono SDF Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            fa.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~—’“”…·•«»×", out _);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"fonts\"}");
        }
    }
}
