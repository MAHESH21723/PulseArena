using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Unity.Netcode;

namespace PulseArena.Editor
{
    /// Generates everything the world needs from the CC0 kits: atlas materials, arena piece
    /// prefabs, character/weapon/pickup prefabs, animator controllers, the baked scene arena and
    /// its lighting setup. Driven by PulseBuilder.Setup() and PulseBuilder.BakeLighting().
    public static class PulseWorldBuilder
    {
        const string Root = "Assets/PulseArena";
        const string WorldDir = Root + "/Resources/World";
        const string MatDir = Root + "/Resources/Materials/World";
        const string Station = Root + "/ThirdParty/Kenney_SpaceStationKit/FBX/";
        const string Blasters = Root + "/ThirdParty/Kenney_BlasterKit/FBX/";
        const string Essentials = Root + "/ThirdParty/Quaternius_SciFiEssentials/";
        const string Astro = Root + "/ThirdParty/Quaternius_UltimateSpaceKit/";

        // Kit asset names. Change here if you want other models.
        const string CarbineFbx = Essentials + "FBX/Gun_Rifle.fbx";
        const string ScatterFbx = Blasters + "blaster-o.fbx"; // shortest and widest of the 18 blasters
        const string ShieldFbx = Essentials + "FBX/Prop_HealthPack.fbx";
        const string AmmoFbx = Essentials + "FBX/Prop_Ammo.fbx";
        const string HumanFbx = Astro + "FBX/Astronaut_RaeTheRedPanda.fbx";
        const string BotFbx = Astro + "FBX/Astronaut_FinnTheFrog.fbx";

        public const float PilotHeight = 1.8f;
        const float CarbineLength = .6f, ScatterLength = .5f;

        enum Col { None, Box, Ramp }
        struct Spec
        {
            public string key, fbx; public bool top; public Col col;
            public Spec(string k, string f, bool t, Col c) { key = k; fbx = f; top = t; col = c; }
        }
        // Piece keys must match ArenaLayout.PieceKeys. "top" = pivot at the top-centre (floor tiles sit with their top at y=0).
        static readonly Spec[] Pieces =
        {
            new Spec("floor", "floor", true, Col.None), new Spec("floor-detail", "floor-detail", true, Col.None),
            new Spec("floor-panel", "floor-panel", true, Col.None), new Spec("floor-solid", "floor", true, Col.Box),
            new Spec("wall", "wall", false, Col.Box), new Spec("wall-window", "wall-window", false, Col.Box),
            new Spec("wall-banner", "wall-banner", false, Col.Box), new Spec("wall-pillar", "wall-pillar", false, Col.Box),
            new Spec("wall-detail", "wall-detail", false, Col.None),
            new Spec("container", "container", false, Col.Box), new Spec("container-wide", "container-wide", false, Col.Box),
            new Spec("container-tall", "container-tall", false, Col.Box), new Spec("container-flat", "container-flat", false, Col.Box),
            new Spec("table-large", "table-large", false, Col.Box),
            new Spec("stairs-ramp", "stairs-ramp", false, Col.Ramp),
            new Spec("computer-wide", "computer-wide", false, Col.None), new Spec("display-wall", "display-wall", false, Col.None),
        };

        // ------------------------------------------------------------------ entry points

        /// Imports the models with the PulseModelImport rules, then creates materials, prefabs and controllers.
        public static void BuildAssets()
        {
            foreach (string d in new[] { WorldDir, MatDir }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            Reimport();

            // The Kenney palette is very pale; a cool mid-grey tint keeps contrast so emissive strips read.
            var arena = LitMaterial("Arena", Station + "Textures/colormap.png", new Color(.80f, .86f, .93f), .2f);
            var pilot = LitMaterial("Pilot", Astro + "Atlas.png", Color.white, .25f, null, new Color(.10f, .10f, .10f));
            var carbine = LitMaterial("Carbine", Essentials + "Textures/T_Guns_Batch1_BaseColor.png", Color.white, .55f, Essentials + "Textures/T_Guns_Batch1_Emissive.png");
            var blaster = LitMaterial("Scatter", Blasters + "Textures/colormap.png", new Color(1f, .85f, .65f), .35f, null, new Color(.2f, .08f, .01f));
            var props = LitMaterial("Props", Essentials + "Textures/T_Props_Batch2_BaseColor.png", Color.white, .4f, Essentials + "Textures/T_Props_Batch2_Emissive.png");

            foreach (var spec in Pieces) MakePiece(spec, arena);
            MakeWeapon("Weapon_Carbine", CarbineFbx, carbine, CarbineLength);
            MakeWeapon("Weapon_Scatter", ScatterFbx, blaster, ScatterLength);
            string mask = WorldDir + "/PilotUpperBody.mask";
            var humanController = BuildController(WorldDir + "/PilotAnimator.controller", HumanFbx, mask);
            var botController = BuildController(WorldDir + "/PilotAnimatorBot.controller", BotFbx, WorldDir + "/PilotUpperBodyBot.mask");
            MakePilot("PilotHuman", HumanFbx, humanController, pilot);
            MakePilot("PilotBot", BotFbx, botController, pilot);
            MakePickup("Pickup_Shield", PickupKind.Shield, ShieldFbx, props, "Cyan");
            MakePickup("Pickup_Ammo", PickupKind.Ammo, AmmoFbx, props, "Amber");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static GameObject LoadWorldPrefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(WorldDir + "/" + name + ".prefab");

        /// Builds the arena under "Reactor 07 | Arena" in the open scene and marks it static for baking.
        public static GameObject BuildArenaInScene()
        {
            var root = new GameObject(ArenaLayout.RootName);
            ArenaLayout.Build(root.transform, (piece, parent) =>
            {
                var prefab = LoadWorldPrefab("Arena_" + piece);
                return prefab ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent) : null;
            });
            const StaticEditorFlags solid = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            const StaticEditorFlags small = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            const StaticEditorFlags far = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
            foreach (Transform group in root.transform)
            {
                var flags = group.name == "Backdrop" ? far : (group.name == "Decor" || group.name == "Lights" || group.name == "Spawns") ? small : solid;
                foreach (var t in group.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
            return root;
        }

        /// Light probes over the playable area and one baked reflection probe.
        public static void AddProbes()
        {
            var probes = new GameObject("Light Probes").AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            foreach (float y in new[] { 1f, 2.6f })
                for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                    positions.Add(new Vector3(x * 9f, y, z * 9f));
            positions.Add(new Vector3(0, 1f, 0));
            probes.probePositions = positions.ToArray();

            var go = new GameObject("Reflection Probe"); go.transform.position = new Vector3(0, 2.5f, 0);
            var rp = go.AddComponent<ReflectionProbe>();
            rp.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            rp.resolution = 128; rp.size = new Vector3(52, 12, 52); rp.boxProjection = true; rp.intensity = 1f; rp.importance = 1;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
        }

        public static readonly Color HorizonColor = new Color(.80f, .90f, 1f);

        /// Procedural daylight skybox material (built-in Skybox/Procedural shader works in URP).
        static Material SkyMaterial()
        {
            string path = Root + "/Resources/Materials/ArenaSky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Skybox/Procedural");
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_SkyTint", new Color(.45f, .62f, .95f));
            m.SetColor("_GroundColor", new Color(.74f, .70f, .62f));
            m.SetFloat("_SunSize", .04f); m.SetFloat("_SunSizeConvergence", 5f);
            m.SetFloat("_AtmosphereThickness", .85f); m.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// Mixed directional light plus the lighting settings asset (baked, subtractive, mobile friendly).
        public static void ConfigureLighting(Light sun, Color skyColor)
        {
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.Soft;
            // Baked-only cool fill from the opposite side: lifts the shaded faces with blue instead of black.
            var fillGO = new GameObject("Fill light"); fillGO.transform.rotation = Quaternion.Euler(35, 148, 0);
            var fill = fillGO.AddComponent<Light>(); fill.type = LightType.Directional; fill.color = new Color(.62f, .76f, 1f);
            fill.intensity = .55f; fill.shadows = LightShadows.None; fill.lightmapBakeType = LightmapBakeType.Baked;
            RenderSettings.skybox = SkyMaterial(); RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.50f, .62f, .82f);
            RenderSettings.ambientEquatorColor = new Color(.56f, .58f, .58f);
            RenderSettings.ambientGroundColor = new Color(.42f, .37f, .31f);
            RenderSettings.subtractiveShadowColor = new Color(.52f, .60f, .78f);
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = HorizonColor; RenderSettings.fogStartDistance = 60; RenderSettings.fogEndDistance = 150;

            string path = Root + "/Scenes/PulseArenaLighting.lighting";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)) AssetDatabase.DeleteAsset(path);
            var ls = new LightingSettings { name = "PulseArenaLighting" };
            ls.lightmapper = Environment.GetEnvironmentVariable("PULSE_BAKE_CPU") == "1" ? LightingSettings.Lightmapper.ProgressiveCPU : LightingSettings.Lightmapper.ProgressiveGPU;
            ls.bakedGI = true; ls.realtimeGI = false; ls.autoGenerate = false;
            ls.mixedBakeMode = MixedLightingMode.Subtractive;
            ls.lightmapResolution = 10f; ls.lightmapMaxSize = 1024; ls.lightmapPadding = 2;
            ls.indirectScale = 1.1f;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.directSampleCount = 32; ls.indirectSampleCount = 128; ls.environmentSampleCount = 128;
            AssetDatabase.CreateAsset(ls, path);
            Lightmapping.lightingSettings = ls;
        }

        // ------------------------------------------------------------------ models and materials

        static void Reimport()
        {
            var paths = new List<string>();
            paths.AddRange(Pieces.Select(p => Station + p.fbx + ".fbx"));
            paths.AddRange(new[] { CarbineFbx, ScatterFbx, ShieldFbx, AmmoFbx, HumanFbx, BotFbx,
                Station + "Textures/colormap.png", Blasters + "Textures/colormap.png", Astro + "Atlas.png",
                Essentials + "Textures/T_Guns_Batch1_BaseColor.png", Essentials + "Textures/T_Guns_Batch1_Emissive.png",
                Essentials + "Textures/T_Props_Batch2_BaseColor.png", Essentials + "Textures/T_Props_Batch2_Emissive.png" });
            foreach (var p in paths.Distinct())
            {
                if (File.Exists(p)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                else Debug.LogWarning("Missing kit asset: " + p);
            }
        }

        static Material LitMaterial(string name, string baseMap, Color tint, float smoothness, string emissionMap = null, Color? emission = null)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseMap));
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0);
            m.enableInstancing = true;
            if (emissionMap != null || emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                if (emissionMap != null) m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emissionMap));
                m.SetColor("_EmissionColor", emission ?? Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; // dynamic objects only
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetMaterial(GameObject go, Material m, Transform skip = null)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (skip && r.transform.IsChildOf(skip)) continue;
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }
        }

        static Bounds RendererBounds(GameObject go, Transform skip = null)
        {
            Bounds b = default; bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (skip && r.transform.IsChildOf(skip)) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static GameObject SavePrefab(GameObject root, string name)
        {
            string path = WorldDir + "/" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f) return f; }
            return null;
        }

        // ------------------------------------------------------------------ arena pieces

        static void MakePiece(Spec s, Material mat)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Station + s.fbx + ".fbx");
            if (!fbx) { Debug.LogWarning("Missing arena piece " + s.fbx); return; }
            var root = new GameObject("Arena_" + s.key);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
            SetMaterial(model, mat);
            Bounds b = RendererBounds(model);
            Vector3 off = new Vector3(-b.center.x, s.top ? -b.max.y : -b.min.y, -b.center.z);
            model.transform.localPosition = off;
            var box = new Bounds(b.center + off, b.size);

            if (s.col == Col.Box)
            {
                var c = root.AddComponent<BoxCollider>(); c.center = box.center; c.size = box.size;
            }
            else if (s.col == Col.Ramp)
            {
                // The kit ramp is stepped and rises toward local -Z. A tilted slab through the step noses is the walkable surface.
                float rise = box.size.y, run = box.size.z, hyp = Mathf.Sqrt(rise * rise + run * run), a = Mathf.Atan2(rise, run);
                var slope = new GameObject("Slope"); slope.transform.SetParent(root.transform, false);
                slope.transform.localRotation = Quaternion.Euler(a * Mathf.Rad2Deg, 0, 0);
                slope.transform.localPosition = new Vector3(box.center.x, rise * .5f - .15f * Mathf.Cos(a), box.center.z - .15f * Mathf.Sin(a));
                var c = slope.AddComponent<BoxCollider>(); c.size = new Vector3(box.size.x, .3f, hyp);
            }
            Debug.Log("PULSE piece " + s.key + " size " + box.size.ToString("F2"));
            SavePrefab(root, "Arena_" + s.key);
        }

        // ------------------------------------------------------------------ weapons

        static void MakeWeapon(string name, string fbxPath, Material mat, float length)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (!fbx) { Debug.LogWarning("Missing weapon model " + fbxPath); return; }
            var root = new GameObject(name);
            var holder = new GameObject("Model").transform; holder.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, holder);
            SetMaterial(model, mat);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The barrel is the long axis; it points to whichever end sticks out further from the model origin.
            Bounds b = RendererBounds(model);
            bool alongX = b.size.x > b.size.z;
            float sign = alongX ? (b.max.x > -b.min.x ? 1 : -1) : (b.max.z > -b.min.z ? 1 : -1);
            float yaw = alongX ? (sign > 0 ? -90f : 90f) : (sign > 0 ? 0f : 180f);
            holder.localRotation = Quaternion.Euler(0, yaw, 0);
            b = RendererBounds(model);
            float scale = length / Mathf.Max(.001f, b.size.z);
            holder.localScale = Vector3.one * scale;
            b = RendererBounds(model);
            // Origin sits near the grip: 35% of the length behind it, 65% ahead.
            var shift = new Vector3(-b.center.x, -b.center.y, -.35f * length - b.min.z);
            holder.localPosition = shift;
            float tip = b.max.z + shift.z;

            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0, b.size.y * .1f, tip);
            Debug.Log("PULSE weapon " + name + " yaw " + yaw + " scale " + scale.ToString("F3") + " tip " + tip.ToString("F2"));
            SavePrefab(root, name);
        }

        // ------------------------------------------------------------------ pilots

        static AnimatorController BuildController(string path, string fbxPath, string maskPath)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            AnimationClip Clip(string n) => clips.FirstOrDefault(c => c.name.EndsWith("|" + n) || c.name == n);
            var idle = Clip("Idle_Gun"); var walk = Clip("Walk_Gun"); var run = Clip("Run_Gun");
            var shoot = Clip("Run_Gun_Shoot"); var hit = Clip("HitReact"); var death = Clip("Death");
            if (!idle || !run || !hit || !death)
                Debug.LogError("Animation clips missing in " + fbxPath + ". Found: " + string.Join(", ", clips.Select(c => c.name)));

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Alive", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            var ps = ctrl.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].name == "Alive") ps[i].defaultBool = true;
            ctrl.parameters = ps;

            var sm = ctrl.layers[0].stateMachine;
            var tree = new BlendTree { name = "Move", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            if (idle) tree.AddChild(idle, 0f);
            if (walk) tree.AddChild(walk, 2.7f);
            if (run) tree.AddChild(run, 5.4f);
            var loco = sm.AddState("Locomotion"); loco.motion = tree; sm.defaultState = loco;
            var hitState = sm.AddState("HitReact"); hitState.motion = hit;
            var deathState = sm.AddState("Death"); deathState.motion = death;

            var toDeath = sm.AddAnyStateTransition(deathState);
            toDeath.AddCondition(AnimatorConditionMode.IfNot, 0, "Alive");
            toDeath.hasExitTime = false; toDeath.duration = .08f; toDeath.canTransitionToSelf = false;
            var toHit = sm.AddAnyStateTransition(hitState);
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit"); toHit.AddCondition(AnimatorConditionMode.If, 0, "Alive");
            toHit.hasExitTime = false; toHit.duration = .05f; toHit.canTransitionToSelf = false;
            var hitBack = hitState.AddTransition(loco); hitBack.hasExitTime = true; hitBack.exitTime = .85f; hitBack.duration = .12f;
            var alive = deathState.AddTransition(loco); alive.AddCondition(AnimatorConditionMode.If, 0, "Alive"); alive.hasExitTime = false; alive.duration = .1f;

            // Upper-body layer: firing plays over the locomotion (arms and torso only).
            var mask = new AvatarMask();
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx)
            {
                mask.AddTransformPath(fbx.transform, true);
                for (int i = 0; i < mask.transformCount; i++) mask.SetTransformActive(i, mask.GetTransformPath(i).Contains("Torso"));
            }
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(maskPath)) AssetDatabase.DeleteAsset(maskPath);
            AssetDatabase.CreateAsset(mask, maskPath);

            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            var layer = layers[layers.Length - 1];
            layer.avatarMask = mask; layer.defaultWeight = 1f; layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layers.Length - 1] = layer; ctrl.layers = layers;
            var up = ctrl.layers[ctrl.layers.Length - 1].stateMachine;
            var none = up.AddState("None"); up.defaultState = none;
            if (shoot)
            {
                var fire = up.AddState("Shoot"); fire.motion = shoot;
                var go = up.AddAnyStateTransition(fire);
                go.AddCondition(AnimatorConditionMode.If, 0, "Fire");
                go.hasExitTime = false; go.duration = .04f; go.canTransitionToSelf = true;
                var back = fire.AddTransition(none); back.hasExitTime = true; back.exitTime = .9f; back.duration = .12f;
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static void MakePilot(string name, string fbxPath, AnimatorController ctrl, Material mat)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (!fbx) { Debug.LogWarning("Missing character " + fbxPath); return; }
            var root = new GameObject(name);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rig.name = "Rig";
            rig.transform.localPosition = Vector3.zero; rig.transform.localRotation = Quaternion.identity; rig.transform.localScale = Vector3.one;

            // The FBX ships with a pistol mesh parented to the right hand. Keep its transform as the gun mount, drop the mesh.
            var pistol = FindDeep(rig.transform, "Pistol");
            SetMaterial(rig, mat, pistol);

            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Bounds b = default; bool first = true;
            foreach (var s in skins) { if (first) { b = s.bounds; first = false; } else b.Encapsulate(s.bounds); }
            float height = b.size.y, ground = b.min.y;
            var head = FindDeep(rig.transform, "Head_end");
            if (head)
            {
                float byBone = head.position.y - ground;
                if (Mathf.Abs(byBone - height) > .35f * height) { Debug.LogWarning(name + ": bounds height " + height + " disagrees with head bone height " + byBone + "; using bone height"); height = byBone; }
            }
            if (height < .01f) { Debug.LogError(name + ": could not measure the character"); height = PilotHeight; }
            var hips = FindDeep(rig.transform, "Hips");
            Vector3 centre = hips ? hips.position : b.center;

            // Face +Z: toes (foot end) should lie in front of the ankle.
            var foot = FindDeep(rig.transform, "Foot.L"); var toe = FindDeep(rig.transform, "Foot.L_end");
            float yaw = 0;
            if (foot && toe)
            {
                Vector3 v = toe.position - foot.position; v.y = 0;
                if (v.magnitude > .01f * height && v.z < 0) yaw = 180;
                Debug.Log("PULSE " + name + " toe direction " + v.ToString("F2") + " -> yaw " + yaw);
            }
            float scale = PilotHeight / height;
            var rot = Quaternion.Euler(0, yaw, 0);
            rig.transform.localRotation = rot; rig.transform.localScale = Vector3.one * scale;
            var p = -(rot * (new Vector3(centre.x, 0, centre.z) * scale)); p.y = -ground * scale;
            rig.transform.localPosition = p;
            Debug.Log("PULSE " + name + " raw height " + height.ToString("F2") + " scale " + scale.ToString("F3"));

            foreach (var s in skins)
            {
                s.quality = SkinQuality.Bone2; s.updateWhenOffscreen = false; s.skinnedMotionVectors = false;
                s.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; s.receiveShadows = true;
            }

            var anim = rig.GetComponent<Animator>(); if (!anim) anim = rig.AddComponent<Animator>();
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Pose the rig as in Idle_Gun so the gun mount is oriented the way the character holds a gun.
            var idle = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().FirstOrDefault(c => c.name.EndsWith("|Idle_Gun") || c.name == "Idle_Gun");
            if (idle) idle.SampleAnimation(rig, 0f);
            if (pistol)
            {
                Vector3 axis = Vector3.forward; float sign = 1;
                var mf = pistol.GetComponentInChildren<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    var mb = mf.sharedMesh.bounds; var e = mb.size;
                    int longest = e.x >= e.y ? (e.x >= e.z ? 0 : 2) : (e.y >= e.z ? 1 : 2);
                    axis = longest == 0 ? Vector3.right : longest == 1 ? Vector3.up : Vector3.forward;
                    float hi = Vector3.Dot(mb.max, axis), lo = Vector3.Dot(mb.min, axis);
                    sign = hi > -lo ? 1 : -1;
                    axis = mf.transform.TransformDirection(axis * sign);
                }
                else axis = pistol.forward;
                Vector3 f = axis.normalized;
                if (Vector3.Dot(f, root.transform.forward) < 0) f = -f;
                Vector3 u = Vector3.ProjectOnPlane(Vector3.up, f);
                if (u.sqrMagnitude < .01f) u = Vector3.up;
                var attach = new GameObject("HandAttach").transform;
                attach.SetParent(pistol.parent, false);
                attach.position = pistol.position; attach.rotation = Quaternion.LookRotation(f, u.normalized);
                attach.localScale = Vector3.one;
                UnityEngine.Object.DestroyImmediate(pistol.gameObject);
                Debug.Log("PULSE " + name + " hand mount forward " + f.ToString("F2") + " parent " + attach.parent.name);
            }
            else Debug.LogWarning(name + ": no Pistol node found; no HandAttach created (guns will not show in hand)");
            SavePrefab(root, name);
        }

        // ------------------------------------------------------------------ pickups

        static void MakePickup(string name, PickupKind kind, string fbxPath, Material mat, string ringMaterial)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (!fbx) { Debug.LogWarning("Missing pickup model " + fbxPath); return; }
            var root = new GameObject(name);
            root.AddComponent<NetworkObject>();
            var pickup = root.AddComponent<ArenaPickup>(); pickup.kind = kind;

            var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false); visual.localPosition = new Vector3(0, .9f, 0);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, visual);
            SetMaterial(model, mat);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Bounds b = RendererBounds(model);
            float s = .65f / Mathf.Max(b.size.x, b.size.y, b.size.z, .001f);
            model.transform.localScale = Vector3.one * s;
            b = RendererBounds(model);
            model.transform.position += visual.position - b.center;
            pickup.visual = visual;

            var glow = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Resources/Materials/" + ringMaterial + ".mat");
            var dark = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Resources/Materials/Steel.mat");
            var seam = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Resources/Materials/Seam.mat");
            pickup.glowRing = Ring("GlowRing", root.transform, 1.15f, glow, dark);
            pickup.rechargeRing = Ring("RechargeRing", root.transform, .6f, seam, dark);
            pickup.rechargeRing.gameObject.SetActive(false);
            SavePrefab(root, name);
        }

        // A flat disc inside a slightly smaller dark disc reads as a ring.
        static Transform Ring(string name, Transform parent, float diameter, Material outer, Material inner)
        {
            var ring = new GameObject(name).transform; ring.SetParent(parent, false);
            ring.localPosition = new Vector3(0, .03f, 0);
            Disc("Outer", ring, diameter, 0f, outer);
            Disc("Inner", ring, diameter * .82f, .006f, inner);
            return ring;
        }
        static void Disc(string name, Transform parent, float diameter, float y, Material m)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); d.name = name;
            UnityEngine.Object.DestroyImmediate(d.GetComponent<Collider>());
            d.transform.SetParent(parent, false); d.transform.localPosition = new Vector3(0, y, 0); d.transform.localScale = new Vector3(diameter, .004f, diameter);
            var r = d.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
