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
            foreach(string p in new[]{"Scenes","Prefabs","Resources/Materials","Resources/Fonts","Resources/Brand"})Directory.CreateDirectory(Root+"/"+p);
            AssetDatabase.Refresh();
            Material("Floor",new Color(.075f,.105f,.14f),0,.23f);
            Material("Wall",new Color(.11f,.19f,.25f),0,.3f);
            Material("Steel",new Color(.23f,.32f,.39f),0,.6f);
            Material("Armor",new Color(.55f,.67f,.72f),0,.45f);
            Material("Cyan",ArenaArt.Cyan,2,.5f);Material("Amber",ArenaArt.Amber,2,.5f);
            Material("Seam",new Color(.13f,.31f,.37f),.3f,.2f);Material("Skyline",new Color(.04f,.08f,.13f),0,.15f);
            string original="Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
            if(!AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(original))throw new Exception("Import UI Resources first, then generate the game.");
            if(!File.Exists(Root+"/Resources/Fonts/PulseFont.asset"))AssetDatabase.CopyAsset(original,Root+"/Resources/Fonts/PulseFont.asset");

            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var cameraGO=new GameObject("Pilot Camera",typeof(Camera),typeof(AudioListener),typeof(UniversalAdditionalCameraData));cameraGO.tag="MainCamera";
            var camera=cameraGO.GetComponent<Camera>();camera.nearClipPlane=.06f;camera.farClipPlane=160;camera.backgroundColor=new Color(.025f,.06f,.1f);camera.clearFlags=CameraClearFlags.SolidColor;
            cameraGO.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var sun=new GameObject("Key light",typeof(Light));sun.transform.rotation=Quaternion.Euler(48,-32,0);var light=sun.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.color=new Color(.71f,.87f,1);light.shadows=LightShadows.Hard;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.46f,.57f);RenderSettings.fog=true;RenderSettings.fogColor=camera.backgroundColor;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.013f;
            var volumeGO=new GameObject("Atmosphere",typeof(Volume));var volume=volumeGO.GetComponent<Volume>();volume.isGlobal=true;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.35f);bloom.threshold.Override(1);var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            string volumePath=Root+"/Resources/Atmosphere.asset";SaveAsset(profile,volumePath);foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);volume.sharedProfile=profile;

            var player=new GameObject("Pilot",typeof(NetworkObject),typeof(NetworkTransform),typeof(CharacterController),typeof(ArenaPlayer));
            var controller=player.GetComponent<CharacterController>();controller.height=1.95f;controller.radius=.34f;controller.center=Vector3.up*.975f;controller.stepOffset=.3f;controller.skinWidth=.04f;
            var nt=player.GetComponent<NetworkTransform>();nt.Interpolate=true;nt.SyncScaleX=nt.SyncScaleY=nt.SyncScaleZ=false;nt.SyncRotAngleX=nt.SyncRotAngleZ=false;
            var playerPrefab=PrefabUtility.SaveAsPrefabAsset(player,Root+"/Prefabs/Pilot.prefab");UnityEngine.Object.DestroyImmediate(player);
            var match=new GameObject("Match",typeof(NetworkObject),typeof(ArenaMatch));var matchPrefab=PrefabUtility.SaveAsPrefabAsset(match,Root+"/Prefabs/Match.prefab");UnityEngine.Object.DestroyImmediate(match);
            var prefabs=ScriptableObject.CreateInstance<NetworkPrefabsList>();prefabs.Add(new NetworkPrefab{Prefab=playerPrefab});prefabs.Add(new NetworkPrefab{Prefab=matchPrefab});SaveAsset(prefabs,Root+"/Prefabs/NetworkPrefabs.asset");
            var networkGO=new GameObject("Network",typeof(NetworkManager),typeof(UnityTransport));var network=networkGO.GetComponent<NetworkManager>();
            network.NetworkConfig=new NetworkConfig{NetworkTransport=networkGO.GetComponent<UnityTransport>(),PlayerPrefab=playerPrefab,TickRate=30,EnableSceneManagement=false,ConnectionApproval=true,ProtocolVersion=1};
            network.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
            var appGO=new GameObject("Pulse Arena",typeof(PulseApp));var app=appGO.GetComponent<PulseApp>();app.Network=network;app.View=camera;app.PlayerPrefab=playerPrefab;app.MatchPrefab=matchPrefab;
            EditorSceneManager.SaveScene(scene,ScenePath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            Configure();Icon();AssetDatabase.SaveAssets();Debug.Log("PULSE_SETUP_OK");
        }
        static void SaveAsset(UnityEngine.Object asset,string path)
        {if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path))AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(asset,path);}
        static void Material(string name,Color color,float emission,float smoothness)
        {
            string path=Root+"/Resources/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.color=color;material.SetFloat("_Smoothness",smoothness);material.SetFloat("_Metallic",.25f);
            if(emission>0){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",color*emission);}EditorUtility.SetDirty(material);
        }
        public static void Configure()
        {
            foreach(string name in new[]{"Floor","Wall","Steel","Armor","Skyline"})
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Resources/Materials/"+name+".mat");
                if(material){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",material.color*.35f);EditorUtility.SetDirty(material);}
            }
            PlayerSettings.companyName="kagidhakappal";PlayerSettings.productName="Pulse Arena";PlayerSettings.bundleVersion="0.1.1";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.kagidhakappal.pulsearena");
            PlayerSettings.Android.bundleVersionCode=2;PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)36;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission=true;PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait=false;PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;PlayerSettings.allowedAutorotateToLandscapeLeft=true;PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
            var serialized=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);var input=serialized.FindProperty("activeInputHandler");if(input!=null){input.intValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();}
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if(pipeline){pipeline.renderScale=1;pipeline.msaaSampleCount=2;pipeline.shadowDistance=45;pipeline.supportsCameraDepthTexture=false;pipeline.supportsCameraOpaqueTexture=false;GraphicsSettings.defaultRenderPipeline=pipeline;for(int i=0;i<QualitySettings.names.Length;i++){QualitySettings.SetQualityLevel(i);QualitySettings.renderPipeline=pipeline;}EditorUtility.SetDirty(pipeline);}
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
            texture.Apply();string path=Root+"/Resources/Brand/PulseIcon.png";File.WriteAllBytes(path,texture.EncodeToPNG());AssetDatabase.ImportAsset(path);var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(path);PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown,new[]{icon});UnityEngine.Object.DestroyImmediate(texture);
        }
        [MenuItem("Pulse Arena/Build Windows Test Player")]
        public static void Windows()
        {Configure();AssetDatabase.SaveAssets();Build(BuildTarget.StandaloneWindows64,"Builds/Windows/PulseArena.exe",BuildOptions.Development);}
        [MenuItem("Pulse Arena/Build Android Test APK")]
        public static void AndroidTest()
        {Configure();EditorUserBuildSettings.buildAppBundle=false;PlayerSettings.Android.useCustomKeystore=false;Build(BuildTarget.Android,"Builds/Android/PulseArena-test.apk",BuildOptions.Development);}
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

