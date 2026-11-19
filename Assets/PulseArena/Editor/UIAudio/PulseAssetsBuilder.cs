using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PulseArena
{
    // Batch entry points:
    //   Unity -batchmode -quit -projectPath . -executeMethod PulseArena.PulseAssetsBuilder.BuildFonts
    //   Unity -batchmode -quit -projectPath . -executeMethod PulseArena.PulseAssetsBuilder.BuildSoundBank
    public static class PulseAssetsBuilder
    {
        const string Root = "Assets/PulseArena";
        const string FontDir = Root + "/ThirdParty/Fonts/Play/";
        const string Glyphs = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~•·×";

        [MenuItem("Pulse Arena/Build Fonts")]
        public static void BuildFonts()
        {
            Directory.CreateDirectory(Root + "/Resources/Fonts");
            AssetDatabase.Refresh();
            var bold = MakeFont(FontDir + "Play-Bold.ttf", Root + "/Resources/Fonts/PulseFontBold.asset", "PulseFontBold");
            var regular = MakeFont(FontDir + "Play-Regular.ttf", Root + "/Resources/Fonts/PulseFont.asset", "PulseFont");
            var table = regular.fontWeightTable;
            if (table != null && table.Length > 7)
            {
                table[7].regularTypeface = bold; // FontStyles.Bold resolves to the real bold face
                // fontWeightTable returns the live array; editing the element in place is enough.
            }
            EditorUtility.SetDirty(regular);
            AssetDatabase.SaveAssets();
            Debug.Log("PULSE_FONTS_BUILT");
        }

        static TMP_FontAsset MakeFont(string ttf, string output, string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (!font) throw new FileNotFoundException("Missing font " + ttf);
            if (File.Exists(output)) AssetDatabase.DeleteAsset(output);
            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = name;
            AssetDatabase.CreateAsset(asset, output);
            if (asset.material) { asset.material.name = name + " Material"; AssetDatabase.AddObjectToAsset(asset.material, asset); }
            foreach (var tex in asset.atlasTextures)
            {
                tex.name = name + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, asset);
            }
            asset.TryAddCharacters(Glyphs, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.Log(name + " missing glyphs: " + missing);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // ---- sound bank
        // All gameplay sounds are layered/mastered clips in ThirdParty/Audio/Mixed (see CREDITS.md there).
        const string Mixed = Root + "/ThirdParty/Audio/Mixed/";

        struct Spec
        {
            public string Key; public string[] Files; public float Volume; public int Voices; public bool Loop;
            public float Pitch, VolVar, Cooldown; public int Priority;
            public Spec(string key, float volume, int voices, string[] files, float pitch = .04f, float volVar = .08f, float cooldown = 0f, int priority = 0, bool loop = false)
            { Key = key; Files = files; Volume = volume; Voices = voices; Loop = loop; Pitch = pitch; VolVar = volVar; Cooldown = cooldown; Priority = priority; }
        }

        /// prefix_1.wav .. prefix_count.wav
        static string[] Set(string prefix, int count)
        {
            var list = new List<string>();
            for (int i = 1; i <= count; i++) list.Add(Mixed + prefix + "_" + i + ".wav");
            return list.ToArray();
        }
        static string[] One(string name) => new[] { Mixed + name + ".wav" };
        static string[] Join(params string[][] sets) { var l = new List<string>(); foreach (var s in sets) l.AddRange(s); return l.ToArray(); }

        // Rebuild with: Unity -batchmode -quit -projectPath . -executeMethod PulseArena.PulseAssetsBuilder.BuildSoundBank
        [MenuItem("Pulse Arena/Build Sound Bank")]
        public static void BuildSoundBank()
        {
            // Import (applies PulseAudioImporter settings) every mixed sound.
            foreach (var file in Directory.GetFiles(Mixed, "*.wav")) AssetDatabase.ImportAsset(file.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);

            var specs = new List<Spec>
            {
                // weapons
                new Spec("shot_carbine", 1f, 4, Set("shot_carbine", 4), .03f, .05f, .02f, 3),
                new Spec("shot_secondary", 1f, 3, Set("shot_scatter", 4), .03f, .05f, .02f, 4),
                new Spec("reload", .8f, 2, Set("reload", 3), .02f, .05f, 0f, 2),
                new Spec("swap", .8f, 2, Set("swap", 3), .04f, .06f, 0f, 2),
                new Spec("empty", .8f, 1, Set("empty", 3), .05f, .1f, .12f, 2),
                new Spec("low_ammo", .8f, 1, Set("low_ammo", 1), 0f, 0f, .5f, 4),
                new Spec("impact_wall", .7f, 4, Set("impact_wall", 4), .08f, .15f, .02f, 1),
                new Spec("impact_shield", .7f, 3, Set("impact_shield", 3), .06f, .12f, .02f, 1),
                // combat feedback
                new Spec("hit_confirm", .9f, 2, Set("hit_confirm", 3), .02f, 0f, .03f, 6),
                new Spec("hit_crit", 1f, 2, Set("hit_crit", 2), .02f, 0f, .03f, 7),
                new Spec("kill_confirm", 1f, 2, Set("kill_confirm", 2), .02f, 0f, .1f, 9),
                new Spec("kill", 1f, 2, Set("kill_confirm", 2), .02f, 0f, .1f, 9),
                new Spec("hit_taken", 1f, 3, Set("hurt", 3), .03f, .05f, .04f, 8),
                new Spec("hurt", 1f, 3, Set("hurt", 3), .03f, .05f, .04f, 8),
                new Spec("shield_break", 1f, 2, Set("shield_break", 2), .02f, 0f, .25f, 8),
                new Spec("death", 1f, 1, Set("death", 2), .02f, 0f, 0f, 10),
                new Spec("explosion", .9f, 3, Set("explosion", 2), .05f, .1f, .05f, 5),
                new Spec("respawn", .8f, 2, Set("respawn", 2), .02f, .05f, 0f, 3),
                // movement / pickups
                new Spec("footstep", .45f, 4, Set("footstep", 6), .07f, .2f, .05f, 0),
                new Spec("pickup_shield", .85f, 2, Set("pickup_shield", 2), .02f, .05f, 0f, 4),
                new Spec("pickup_ammo", .85f, 2, Set("pickup_ammo", 2), .03f, .05f, 0f, 4),
                new Spec("pickup", .85f, 2, Join(Set("pickup_shield", 2), Set("pickup_ammo", 2)), .03f, .05f, 0f, 4),
                // match flow
                new Spec("countdown", .9f, 2, Set("countdown", 2), 0f, 0f, 0f, 5),
                new Spec("countdown_go", 1f, 1, One("countdown_go"), 0f, 0f, 0f, 5),
                new Spec("match_win", 1f, 1, One("match_win"), 0f, 0f, 0f, 10),
                new Spec("match_lose", 1f, 1, One("match_lose"), 0f, 0f, 0f, 10),
                new Spec("match_end", 1f, 1, Set("match_end", 2), 0f, 0f, 0f, 10),
                new Spec("low_shield", .6f, 1, One("low_shield_loop"), 0f, 0f, 0f, 3, true),
                // UI
                new Spec("ui_click", .9f, 3, Set("ui_click", 3), .03f, .05f, .03f, 5),
                new Spec("ui_confirm", .9f, 2, Set("ui_confirm", 2), .02f, .04f, .05f, 5),
                new Spec("ui_back", .9f, 2, Set("ui_back", 2), .02f, .04f, .05f, 5),
                new Spec("ui_hover", .6f, 2, Set("ui_hover", 2), .05f, .1f, .04f, 4),
                new Spec("ui_error", .9f, 1, One("ui_error"), 0f, 0f, .1f, 5),
                new Spec("ui_select", .9f, 1, One("ui_select"), .02f, .04f, .04f, 5),
            };

            Directory.CreateDirectory(Root + "/Resources/Audio");
            const string path = Root + "/Resources/Audio/PulseSoundBank.asset";
            var bank = AssetDatabase.LoadAssetAtPath<PulseSoundBank>(path);
            if (!bank) { bank = ScriptableObject.CreateInstance<PulseSoundBank>(); AssetDatabase.CreateAsset(bank, path); }
            bank.Entries.Clear();
            foreach (var spec in specs)
            {
                var clips = new List<AudioClip>();
                foreach (var file in spec.Files)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
                    if (clip) clips.Add(clip); else Debug.LogWarning("PulseSoundBank: missing " + file);
                }
                if (clips.Count == 0) continue;
                bank.Entries.Add(new PulseSoundBank.Entry
                {
                    Key = spec.Key, Clips = clips.ToArray(), Volume = spec.Volume, MaxVoices = spec.Voices, Loop = spec.Loop,
                    PitchVariation = spec.Pitch, VolumeVariation = spec.VolVar, Cooldown = spec.Cooldown, Priority = spec.Priority
                });
            }
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log("PULSE_SOUNDBANK_BUILT entries=" + bank.Entries.Count);
        }
    }
}
