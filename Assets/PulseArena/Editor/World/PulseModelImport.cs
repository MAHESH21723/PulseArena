using UnityEditor;
using UnityEngine;

namespace PulseArena.Editor
{
    /// Import settings for the CC0 kits under Assets/PulseArena/ThirdParty. Done in code so the
    /// settings are reproducible and apply to a fresh clone without hand-edited .meta files.
    public sealed class PulseModelImport : AssetPostprocessor
    {
        const string ThirdParty = "Assets/PulseArena/ThirdParty/";
        const string StationKit = ThirdParty + "Kenney_SpaceStationKit/";
        const string BlasterKit = ThirdParty + "Kenney_BlasterKit/";
        const string EssentialsKit = ThirdParty + "Quaternius_SciFiEssentials/";
        const string AstronautKit = ThirdParty + "Quaternius_UltimateSpaceKit/";

        // Bump when the rules below change so Unity reimports affected assets.
        public override uint GetVersion() => 3;

        static bool IsKitModel(string path) => path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
            && (path.StartsWith(StationKit) || path.StartsWith(BlasterKit) || path.StartsWith(EssentialsKit) || path.StartsWith(AstronautKit));
        static bool IsAstronaut(string path) => path.StartsWith(AstronautKit);

        void OnPreprocessModel()
        {
            if (!IsKitModel(assetPath)) return;
            var m = (ModelImporter)assetImporter;
            bool astronaut = IsAstronaut(assetPath);

            // Kenney station pieces are 1 m at File Scale 0.01; import at x3 so tiles and walls are 3 m.
            m.globalScale = assetPath.StartsWith(StationKit) ? 3f : 1f;
            m.useFileScale = true;
            m.importCameras = false;
            m.importLights = false;
            m.importVisibility = false;
            m.importBlendShapes = false;
            m.addCollider = false;                       // BoxColliders are added to the prefabs
            m.isReadable = false;
            m.meshCompression = ModelImporterMeshCompression.Medium;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.None; // no normal maps are used
            m.weldVertices = true;
            m.optimizeMeshPolygons = true;
            m.optimizeMeshVertices = true;
            m.generateSecondaryUV = assetPath.StartsWith(StationKit); // arena pieces are lightmapped
            m.materialImportMode = ModelImporterMaterialImportMode.None; // atlas materials are assigned by PulseWorldBuilder

            if (astronaut)
            {
                m.importAnimation = true;
                m.animationType = ModelImporterAnimationType.Generic;
                m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                m.animationCompression = ModelImporterAnimationCompression.Optimal;
                m.optimizeGameObjects = false;           // the hand mount and bones must stay addressable
                m.skinWeights = ModelImporterSkinWeights.Standard; // 4 bones; the prefab limits quality to 2
            }
            else
            {
                m.importAnimation = false;
                m.animationType = ModelImporterAnimationType.None;
            }
        }

        // Clip loop flags: locomotion and idles loop; one-shots (Death, HitReact, Shoot, Jump...) do not.
        void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!IsAstronaut(assetPath)) return;
            string n = clip.name;
            int bar = n.LastIndexOf('|'); if (bar >= 0) n = n.Substring(bar + 1);
            bool loop = (n.StartsWith("Idle") || n.StartsWith("Walk") || n.StartsWith("Run")) && !n.Contains("Shoot");
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loop;
            s.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, s);
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ThirdParty) || !assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) return;
            bool kenney = assetPath.StartsWith(StationKit) || assetPath.StartsWith(BlasterKit);
            bool atlas = kenney || assetPath.StartsWith(AstronautKit + "Atlas") || assetPath.Contains("Quaternius_SciFiEssentials/Textures/");
            if (!atlas) return;

            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.alphaSource = TextureImporterAlphaSource.None;
            t.alphaIsTransparency = false;
            t.isReadable = false;
            t.maxTextureSize = 1024;
            t.npotScale = TextureImporterNPOTScale.None;
            if (kenney)
            {
                // Flat colour palettes: point sampling without mips keeps swatches crisp and bleed-free.
                t.filterMode = FilterMode.Point;
                t.mipmapEnabled = false;
            }
            else
            {
                t.filterMode = FilterMode.Bilinear;
                t.mipmapEnabled = true;
                t.anisoLevel = 1;
            }
            t.wrapMode = TextureWrapMode.Clamp;
            t.textureCompression = TextureImporterCompression.Compressed;

            // 512 palettes can afford 4x4 blocks; the bigger atlases (downscaled to 1024) use 6x6.
            var android = t.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = kenney || assetPath.Contains("Atlas.png") ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            t.SetPlatformTextureSettings(android);
        }
    }
}
