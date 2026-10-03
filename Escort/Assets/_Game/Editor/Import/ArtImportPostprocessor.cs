using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>
    /// Import rules for generated art. Characters and the UAL animation libraries share one Humanoid rig (GDD §2);
    /// materials are never imported (CharacterPrefabBuilder assigns palette/toon materials).
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        public const string CharactersDir = "Assets/_Game/Art/Characters/";
        public const string AnimationsDir = "Assets/_Game/Art/Animations/";
        public const string PropsDir = "Assets/_Game/Art/Props/";
        public const string EnvironmentDir = "Assets/_Game/Art/Environment/";
        /// <summary>The opening's street kits (tools/blender/opening_*.py via OpeningAssetsBuilder).</summary>
        public const string OpeningDir = "Assets/_Game/Art/Opening/";

        static readonly HashSet<string> ExtraLoops = new HashSet<string>
        {
            "Sword_Idle", "Pistol_Idle_Loop", "Pistol_Aim_Neutral", "Pistol_Aim_Up", "Pistol_Aim_Down",
            "Idle_No_Loop", "Idle_FoldArms_Loop", "Idle_Shield_Loop",
        };

        void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            bool isChar = assetPath.StartsWith(CharactersDir);
            bool isAnim = assetPath.StartsWith(AnimationsDir);
            bool opening = assetPath.StartsWith(OpeningDir);
            bool isProp = assetPath.StartsWith(PropsDir) || assetPath.StartsWith(EnvironmentDir) || opening;
            if (!isChar && !isAnim && !isProp) return;
            // Animated parts (wheels, wings, the hand-held phone) need clean local axes: no -90° X on the roots.
            mi.bakeAxisConversion = opening;

            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            // Embedded materials keep their source names so builders can AddRemap them to palette toon materials.
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            mi.materialSearch = ModelImporterMaterialSearch.Everywhere;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = isProp; // props are combined/batched by builders
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;

            if (isChar || isAnim)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.optimizeGameObjects = false; // props attach to exposed hand bones
                mi.importAnimation = isAnim;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.addCollider = false;
            }
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/_Game/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            string n = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool bed = n.Contains("_loop") || n.Contains("_pad");
            bool scene = n.StartsWith("syn_open_"); // the opening's song/street/truck: stereo, decoded up front
            var st = ai.defaultSampleSettings;
            st.compressionFormat = AudioCompressionFormat.Vorbis;
            st.quality = bed ? 0.55f : scene ? 0.8f : 0.7f;
            st.loadType = bed ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            ai.defaultSampleSettings = st;
            ai.forceToMono = !bed && !scene;
        }

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith("Assets/_Game/Resources/UI/"))
            {
                // UI sprites: crisp (no mips), uncompressed, 9-slice borders for panels and bars.
                var ui = (TextureImporter)assetImporter;
                ui.textureType = TextureImporterType.Sprite;
                ui.spriteImportMode = SpriteImportMode.Single;
                ui.alphaIsTransparency = true;
                ui.mipmapEnabled = false;
                ui.textureCompression = TextureImporterCompression.Uncompressed;
                ui.filterMode = FilterMode.Bilinear;
                string n = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                float b = n.StartsWith("panel") ? 20f : n switch { "bar" => 10f, "keycap" => 14f, "slot" => 22f, "card" => 24f, _ => 0f };
                ui.spriteBorder = new Vector4(b, b, b, b);
                // Tiled fills (the demo stage's floor grid, locked-entry hatching) need repeat wrapping and a full-rect mesh.
                if (n == "grid_disc") ui.mipmapEnabled = true; // a floor seen at an angle: mips keep its lines from shimmering
                if (n == "grid" || n == "hatch")
                {
                    ui.wrapMode = TextureWrapMode.Repeat;
                    var settings = new TextureImporterSettings();
                    ui.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    ui.SetTextureSettings(settings);
                }
                return;
            }
            if (assetPath.StartsWith("Assets/_Game/Resources/Icons/") || assetPath.StartsWith("Assets/_Game/Art/UI/"))
            {
                var si = (TextureImporter)assetImporter;
                si.textureType = TextureImporterType.Sprite;
                si.spriteImportMode = SpriteImportMode.Single;
                si.alphaIsTransparency = true;
                si.mipmapEnabled = true;
                si.maxTextureSize = 256;
                si.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (!assetPath.StartsWith("Assets/_Game/Art/")) return;
            var ti = (TextureImporter)assetImporter;
            bool opening = assetPath.StartsWith(OpeningDir);
            bool env = assetPath.StartsWith(EnvironmentDir) || assetPath.StartsWith(PropsDir) || opening;
            bool chars = assetPath.StartsWith(CharactersDir);
            if (!env && !chars) return;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.mipmapEnabled = true;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = ti.DoesSourceTextureHaveAlpha();
            ti.anisoLevel = opening ? 8 : 2; // the opening's road and facades are seen at grazing angles
            // Cutout foliage: keep alpha coverage stable across mips so leaves don't vanish at distance.
            string f = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (f.Contains("Leaf") || f.Contains("Leaves") || f.Contains("Flowers") || f.Contains("Vine"))
            {
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.5f;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(AnimationsDir)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                string clean = c.takeName.Split('|').Last();
                c.name = clean;
                c.loopTime = clean.EndsWith("_Loop") || ExtraLoops.Contains(clean);
                c.loopPose = c.loopTime;
                // In-place playback: gameplay moves characters, never root motion (determinism).
                // Rotation is based on body orientation (the UAL mannequin's armature faces -Z; "original" would turn
                // every retargeted body around), height on the feet so different proportions stay grounded.
                c.lockRootRotation = true;
                c.keepOriginalOrientation = false;
                c.rotationOffset = 0f;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = false;
                c.heightFromFeet = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }
    }
}
