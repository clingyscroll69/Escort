using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS.EditorTools
{
    /// <summary>Resources/FX materials used by Vfx and gameplay markers (sand, dust, sparks, rings, the Oath Glyph, fire).</summary>
    public static class FxMaterialBuilder
    {
        const string Dir = "Assets/_Game/Resources/FX";

        [MenuItem("Tools/HS/Build/FX Materials")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            Make("FX_Soft", "T_Soft", Color.white, additive: false);
            Make("FX_Spark", "T_Spark", new Color(1.6f, 1.3f, 0.8f), additive: true);
            Make("FX_Ring", "T_Ring", new Color(1f, 0.7f, 0.25f, 0.85f), additive: false, pulse: 5f);
            Make("FX_RingDanger", "T_Ring", new Color(1f, 0.25f, 0.15f, 0.8f), additive: false, pulse: 7f);
            Make("FX_OathGlyph", "T_OathGlyph", new Color(1.2f, 0.95f, 0.55f, 0.9f), additive: true);
            Make("FX_Flame", "T_Flame", new Color(2.2f, 1.1f, 0.35f), additive: true);
            Make("FX_Smoke", "T_Soft", new Color(0.3f, 0.28f, 0.27f, 0.55f), additive: false);
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"fx materials\"}");
        }

        static void Make(string name, string tex, Color color, bool additive, float pulse = 0f)
        {
            var path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("HS/FX"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("HS/FX");
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/_Game/Art/FX/{tex}.png"));
            m.SetColor("_Color", color);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Pulse", pulse);
            m.renderQueue = 3000;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
    }
}
