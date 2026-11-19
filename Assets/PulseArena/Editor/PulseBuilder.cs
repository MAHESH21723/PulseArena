using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using TMPro;

namespace PulseArena.Editor
{
    public static class PulseBuilder
    {
        const string Root="Assets/PulseArena";
        const string ScenePath=Root+"/Scenes/PulseArena.unity";
        [MenuItem("Pulse Arena/1. Import UI Resources")]
        public static void ImportUI(){TMP_PackageResourceImporter.ImportResources(true,false,false);}
        [MenuItem("Pulse Arena/2. Generate Game")]
        public static void Setup()
        {
            foreach(string p in new[]{"Scenes","Prefabs","Resources/Materials","Resources/Fonts","Resources/Brand","Resources/Materials/World","Resources/World"})Directory.CreateDirectory(Root+"/"+p);
            AssetDatabase.Refresh();
            // Bright stylized palette: light sand floor, off-white teal walls, saturated glow accents.
            Material("Floor",new Color(.88f,.84f,.74f),0,.12f);
            Material("Wall",new Color(.80f,.90f,.92f),0,.15f);
            Material("Steel",new Color(.52f,.60f,.66f),0,.35f);
            Material("Armor",new Color(.92f,.95f,.97f),0,.3f);
            Material("Cyan",ArenaArt.Cyan,1.6f,.4f);Material("Amber",ArenaArt.Amber,1.6f,.4f);
            Material("Seam",new Color(.25f,.68f,.74f),.35f,.2f);Material("Skyline",new Color(.66f,.76f,.88f),0,.05f);
            PulseWorldBuilder.BuildAssets();
            string original="Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
            if(!AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(original))throw new Exception("Import UI Resources first, then generate the game.");
            if(!File.Exists(Root+"/Resources/Fonts/PulseFont.asset"))AssetDatabase.CopyAsset(original,Root+"/Resources/Fonts/PulseFont.asset");

            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var cameraGO=new GameObject("Pilot Camera",typeof(Camera),typeof(AudioListener),typeof(UniversalAdditionalCameraData));cameraGO.tag="MainCamera";
            var camera=cameraGO.GetComponent<Camera>();camera.nearClipPlane=.06f;camera.farClipPlane=160;camera.backgroundColor=PulseWorldBuilder.HorizonColor;camera.clearFlags=CameraClearFlags.Skybox;
            cameraGO.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var sun=new GameObject("Key light",typeof(Light));sun.transform.rotation=Quaternion.Euler(48,-32,0);var light=sun.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.color=new Color(1f,.95f,.82f);light.shadows=LightShadows.Soft;light.shadowStrength=.8f;
            PulseWorldBuilder.ConfigureLighting(light,camera.backgroundColor);
            var volumeGO=new GameObject("Atmosphere",typeof(Volume));var volume=volumeGO.GetComponent<Volume>();volume.isGlobal=true;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.35f);bloom.threshold.Override(1.05f);bloom.scatter.Override(.6f);var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.Neutral);
            var grade=profile.Add<ColorAdjustments>(true);grade.contrast.Override(18f);grade.saturation.Override(24f);grade.postExposure.Override(-.05f);
            var vignette=profile.Add<Vignette>(true);vignette.intensity.Override(.12f);vignette.smoothness.Override(.5f);
            string volumePath=Root+"/Resources/Atmosphere.asset";SaveAsset(profile,volumePath);foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);volume.sharedProfile=profile;

            var player=new GameObject("Pilot",typeof(NetworkObject),typeof(NetworkTransform),typeof(CharacterController),typeof(ArenaPlayer));
            var controller=player.GetComponent<CharacterController>();controller.height=1.95f;controller.radius=.34f;controller.center=Vector3.up*.975f;controller.stepOffset=.3f;controller.skinWidth=.04f;
            var nt=player.GetComponent<NetworkTransform>();nt.Interpolate=true;nt.SyncScaleX=nt.SyncScaleY=nt.SyncScaleZ=false;nt.SyncRotAngleX=nt.SyncRotAngleZ=false;
            var playerPrefab=PrefabUtility.SaveAsPrefabAsset(player,Root+"/Prefabs/Pilot.prefab");UnityEngine.Object.DestroyImmediate(player);
            var match=new GameObject("Match",typeof(NetworkObject),typeof(ArenaMatch));var matchPrefab=PrefabUtility.SaveAsPrefabAsset(match,Root+"/Prefabs/Match.prefab");UnityEngine.Object.DestroyImmediate(match);
            var prefabs=ScriptableObject.CreateInstance<NetworkPrefabsList>();prefabs.Add(new NetworkPrefab{Prefab=playerPrefab});prefabs.Add(new NetworkPrefab{Prefab=matchPrefab});
            foreach(string pickup in new[]{"Pickup_Shield","Pickup_Ammo"}){var pp=PulseWorldBuilder.LoadWorldPrefab(pickup);if(pp)prefabs.Add(new NetworkPrefab{Prefab=pp});else Debug.LogWarning("Missing "+pickup+" prefab; pickups will not spawn.");}
            SaveAsset(prefabs,Root+"/Prefabs/NetworkPrefabs.asset");
            var networkGO=new GameObject("Network",typeof(NetworkManager),typeof(UnityTransport));var network=networkGO.GetComponent<NetworkManager>();
            network.NetworkConfig=new NetworkConfig{NetworkTransport=networkGO.GetComponent<UnityTransport>(),PlayerPrefab=playerPrefab,TickRate=30,EnableSceneManagement=false,ConnectionApproval=true,ProtocolVersion=1};
            network.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
            PulseWorldBuilder.BuildArenaInScene();PulseWorldBuilder.AddProbes();
            var appGO=new GameObject("Pulse Arena",typeof(PulseApp));var app=appGO.GetComponent<PulseApp>();app.Network=network;app.View=camera;app.PlayerPrefab=playerPrefab;app.MatchPrefab=matchPrefab;
            EditorSceneManager.SaveScene(scene,ScenePath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            Configure();ConfigureLook();Icon();AssetDatabase.SaveAssets();Debug.Log("PULSE_SETUP_OK");
        }
        /// Menu "Pulse Arena/3. Bake Lighting": run after Generate Game. Bakes lightmaps, light probes and the reflection probe, then saves the scene.
        [MenuItem("Pulse Arena/3. Bake Lighting")]
        public static void BakeLighting()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            if(!Lightmapping.TryGetLightingSettings(out var settings))throw new Exception("The scene has no lighting settings. Run Generate Game first.");
            if(Environment.GetEnvironmentVariable("PULSE_BAKE_CPU")=="1")settings.lightmapper=LightingSettings.Lightmapper.ProgressiveCPU;
            bool ok=Lightmapping.Bake();
            if(!ok||LightmapSettings.lightmaps.Length==0)
            {
                Debug.LogWarning("GPU lightmapper did not produce lightmaps; retrying with the CPU lightmapper.");
                settings.lightmapper=LightingSettings.Lightmapper.ProgressiveCPU;ok=Lightmapping.Bake();
            }
            if(!ok)throw new Exception("Lightmapping.Bake failed.");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("PULSE_BAKE_OK lightmaps="+LightmapSettings.lightmaps.Length+" lightmapper="+settings.lightmapper);
        }
        static void SaveAsset(UnityEngine.Object asset,string path)
        {if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path))AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(asset,path);}
        static void Material(string name,Color color,float emission,float smoothness)
        {
            string path=Root+"/Resources/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.color=color;material.SetFloat("_Smoothness",smoothness);material.SetFloat("_Metallic",0);
            if(emission>0){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",color*emission);material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;}EditorUtility.SetDirty(material);
        }
        public static void Configure()
        {
            foreach(string name in new[]{"Floor","Wall","Steel","Armor","Skyline"})
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Resources/Materials/"+name+".mat");
                if(material){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",material.color*.06f);EditorUtility.SetDirty(material);}
            }
            PlayerSettings.companyName="kagidhakappal";PlayerSettings.productName="Pulse Arena";PlayerSettings.bundleVersion="0.3.0";ApplyBrandIcons();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.kagidhakappal.pulsearena");
            PlayerSettings.Android.bundleVersionCode=4;PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)36;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission=true;PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait=false;PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;PlayerSettings.allowedAutorotateToLandscapeLeft=true;PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
            var serialized=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);var input=serialized.FindProperty("activeInputHandler");if(input!=null){input.intValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();}
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if(pipeline){pipeline.renderScale=1;pipeline.msaaSampleCount=2;pipeline.shadowDistance=45;pipeline.supportsCameraDepthTexture=false;pipeline.supportsCameraOpaqueTexture=false;GraphicsSettings.defaultRenderPipeline=pipeline;for(int i=0;i<QualitySettings.names.Length;i++){QualitySettings.SetQualityLevel(i);QualitySettings.renderPipeline=pipeline;}EditorUtility.SetDirty(pipeline);}
        }
        /// Applies Branding/AppIcon.png (+ AppIconForeground.png / AppIconBackground.png for adaptive icons) to Android. No-op when files are missing.
        static void ApplyBrandIcons()
        {
            try
            {
                string dir=Root+"/Branding/";
                Texture2D Load(string file)
                {
                    string path=dir+file;if(!File.Exists(path))return null;
                    if(AssetImporter.GetAtPath(path) is TextureImporter ti)
                    {
                        if(ti.textureType!=TextureImporterType.Default||ti.textureCompression!=TextureImporterCompression.Uncompressed||ti.mipmapEnabled||!ti.alphaIsTransparency||ti.npotScale!=TextureImporterNPOTScale.None)
                        {ti.textureType=TextureImporterType.Default;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.mipmapEnabled=false;ti.alphaIsTransparency=true;ti.npotScale=TextureImporterNPOTScale.None;ti.isReadable=false;ti.SaveAndReimport();}
                    }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                var icon=Load("AppIcon.png");if(!icon)return;
                var target=NamedBuildTarget.Android;
                // Unity 6.6 Android uses adaptive icons only (legacy/round kinds are obsolete).
                var fg=Load("AppIconForeground.png");var bg=Load("AppIconBackground.png");
                var adaptive=PlayerSettings.GetPlatformIcons(target,UnityEditor.Android.AndroidPlatformIconKind.Adaptive);
                foreach(var p in adaptive){p.SetTexture(bg?bg:icon,0);p.SetTexture(fg?fg:icon,1);}
                PlayerSettings.SetPlatformIcons(target,UnityEditor.Android.AndroidPlatformIconKind.Adaptive,adaptive);
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown,new[]{icon},IconKind.Application);
            }
            catch(Exception e){Debug.LogWarning("ApplyBrandIcons skipped: "+e.Message);}
        }
        /// Render pipeline asset tweaks for the bright look (soft shadows, 2 cascades, HDR for bloom).
        static void ConfigureLook()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");if(!pipeline)return;
            var so=new SerializedObject(pipeline);
            void SetInt(string n,int v){var p=so.FindProperty(n);if(p!=null)p.intValue=v;}
            void SetBool(string n,bool v){var p=so.FindProperty(n);if(p!=null)p.boolValue=v;}
            SetBool("m_SupportsHDR",true);SetBool("m_SoftShadowsSupported",true);SetInt("m_SoftShadowQuality",1);SetInt("m_ShadowCascadeCount",2);SetInt("m_MainLightShadowmapResolution",1024);
            var split=so.FindProperty("m_Cascade2Split");if(split!=null)split.floatValue=.2f;
            so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);
        }
        static void Icon()
        {
            int size=512;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                var p=new Vector2((x-256)/256f,(y-256)/256f);float radius=p.magnitude;
                Color color=Color.Lerp(new Color(.018f,.037f,.067f),new Color(.06f,.2f,.25f),Mathf.Clamp01(1-radius));
                bool ring=radius>.64f&&radius<.7f;bool bolt=(p.y>-.42f&&p.y<.42f&&Mathf.Abs(p.x+p.y*.55f)<.12f)||(p.y>-.05f&&p.y<.08f&&Mathf.Abs(p.x)<.27f);
                if(ring||bolt)color=ArenaArt.Cyan;texture.SetPixel(x,y,color);
            }
            texture.Apply();string path=Root+"/Resources/Brand/PulseIcon.png";File.WriteAllBytes(path,texture.EncodeToPNG());AssetDatabase.ImportAsset(path);var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(!File.Exists(Root+"/Branding/AppIcon.png"))PlayerSettings.SetIcons(NamedBuildTarget.Unknown,new[]{icon},IconKind.Application);UnityEngine.Object.DestroyImmediate(texture);
        }
        [MenuItem("Pulse Arena/Build Windows Test Player")]
        public static void Windows()
        {Configure();AssetDatabase.SaveAssets();Build(BuildTarget.StandaloneWindows64,"Builds/Windows/PulseArena.exe",BuildOptions.Development);}
        [MenuItem("Pulse Arena/Build Android Test APK")]
        public static void AndroidTest()
        {Configure();EditorUserBuildSettings.buildAppBundle=false;PlayerSettings.Android.useCustomKeystore=false;Build(BuildTarget.Android,"Builds/Android/PulseArena-test.apk",BuildOptions.Development);}
        /// Sideload APK for GitHub releases: non-development (no watermark), signed with the local debug key.
        [MenuItem("Pulse Arena/Build Release APK (GitHub)")]
        public static void AndroidReleaseApk()
        {Configure();EditorUserBuildSettings.buildAppBundle=false;PlayerSettings.Android.useCustomKeystore=false;Build(BuildTarget.Android,"Builds/Android/PulseArena-v"+PlayerSettings.bundleVersion+".apk",BuildOptions.None);}
        [MenuItem("Pulse Arena/Build Play Store AAB")]
        public static void AndroidRelease()
        {
            Configure();string path=Environment.GetEnvironmentVariable("PULSE_KEYSTORE");string alias=Environment.GetEnvironmentVariable("PULSE_KEY_ALIAS");string pass=Environment.GetEnvironmentVariable("PULSE_KEYSTORE_PASSWORD");string keypass=Environment.GetEnvironmentVariable("PULSE_KEY_PASSWORD");string id=Environment.GetEnvironmentVariable("PULSE_APPLICATION_ID");
            if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)||string.IsNullOrWhiteSpace(alias)||string.IsNullOrWhiteSpace(pass)||string.IsNullOrWhiteSpace(keypass)||string.IsNullOrWhiteSpace(id))throw new BuildFailedException("Set PULSE_APPLICATION_ID and all four PULSE_KEY* environment variables. Use your own upload key; no test key is accepted for this release command.");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,id);PlayerSettings.Android.useCustomKeystore=true;PlayerSettings.Android.keystoreName=path;PlayerSettings.Android.keyaliasName=alias;PlayerSettings.Android.keystorePass=pass;PlayerSettings.Android.keyaliasPass=keypass;EditorUserBuildSettings.buildAppBundle=true;
            try{Build(BuildTarget.Android,"Builds/Android/PulseArena-release.aab",BuildOptions.None);}finally{PlayerSettings.Android.keystorePass="";PlayerSettings.Android.keyaliasPass="";}
        }
        static void Build(BuildTarget target,string path,BuildOptions options)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},target=target,locationPathName=path,options=options});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Pulse Arena build failed: "+report.summary.result);
            Debug.Log("PULSE_BUILD_OK "+Path.GetFullPath(path)+" "+report.summary.totalSize+" bytes");
        }
    }
}

