#if UNITY_EDITOR && PULSE_EOS
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PulseArena.EditorTools
{
    // Before every build, copy EOS credentials from .env into the EOS plugin's config file.
    // The generated file is git-ignored. Field names follow com.playeveryware.eos 6.x
    // (StreamingAssets/EOS/EpicOnlineServicesConfig.json); verify after the first plugin import.
    public sealed class EosConfigFromEnv : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        const string ConfigPath = "Assets/StreamingAssets/EOS/EpicOnlineServicesConfig.json";
        static readonly string[][] Map =
        {
            new[] { "productID", "EOS_PRODUCT_ID" }, new[] { "sandboxID", "EOS_SANDBOX_ID" },
            new[] { "deploymentID", "EOS_DEPLOYMENT_ID" }, new[] { "clientID", "EOS_CLIENT_ID" },
            new[] { "clientSecret", "EOS_CLIENT_SECRET" }, new[] { "encryptionKey", "EOS_ENCRYPTION_KEY" },
        };

        public void OnPreprocessBuild(BuildReport report) { Apply(); }

        [MenuItem("Pulse Arena/Write EOS config from .env")]
        public static void Apply()
        {
            var env = ReadEnv();
            string dir = Path.GetDirectoryName(ConfigPath);
            Directory.CreateDirectory(dir);
            string json = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : "{\n  \"productName\": \"Pulse Arena\"\n}";
            foreach (var m in Map)
            {
                string val; env.TryGetValue(m[1], out val);
                if (string.IsNullOrEmpty(val)) { Debug.LogWarning("EOS: " + m[1] + " is empty in .env"); continue; }
                var re = new Regex("\"" + m[0] + "\"\\s*:\\s*\"[^\"]*\"");
                string entry = "\"" + m[0] + "\": \"" + val.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                if (re.IsMatch(json)) json = re.Replace(json, entry.Replace("$", "$$"));
                else json = json.TrimEnd().TrimEnd('}').TrimEnd() + ",\n  " + entry + "\n}";
            }
            File.WriteAllText(ConfigPath, json);
            AssetDatabase.Refresh();
            Debug.Log("EOS config written to " + ConfigPath + " (git-ignored).");
        }

        static Dictionary<string, string> ReadEnv()
        {
            var d = new Dictionary<string, string>();
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".env");
            if (!File.Exists(path)) return d;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('"');
            }
            return d;
        }
    }
}
#endif
