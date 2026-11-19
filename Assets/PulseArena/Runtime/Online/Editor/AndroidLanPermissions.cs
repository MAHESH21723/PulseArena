#if UNITY_EDITOR && UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace PulseArena.EditorTools
{
    // Adds the permissions LAN discovery needs to the generated Android manifest, so no
    // custom AndroidManifest.xml has to be maintained by hand.
    public sealed class AndroidLanPermissions : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;
        static readonly string[] Permissions =
        {
            "android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE", "android.permission.CHANGE_WIFI_MULTICAST_STATE"
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(file)) return;
            var doc = new XmlDocument(); doc.Load(file);
            const string ns = "http://schemas.android.com/apk/res/android";
            var root = doc.DocumentElement;
            foreach (var perm in Permissions)
            {
                bool has = false;
                foreach (XmlNode n in root.SelectNodes("uses-permission"))
                    if (n.Attributes["android:name"] != null && n.Attributes["android:name"].Value == perm) has = true;
                if (has) continue;
                var el = doc.CreateElement("uses-permission");
                el.SetAttribute("name", ns, perm);
                root.AppendChild(el);
            }
            doc.Save(file);
        }
    }
}
#endif
