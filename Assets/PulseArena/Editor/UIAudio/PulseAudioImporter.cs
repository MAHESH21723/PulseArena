using UnityEditor;
using UnityEngine;

namespace PulseArena
{
    /// Short SFX: mono, Vorbis ~70%, decompressed on load and preloaded (no hitch on first shot).
    public class PulseAudioImporter : AssetPostprocessor
    {
        static bool Ours(string path) =>
            path.Contains("/ThirdParty/Audio/") || path.Contains("/ThirdParty/Kenney_SciFiSounds/") || path.Contains("/ThirdParty/Kenney_ImpactSounds/") ||
            path.Contains("/ThirdParty/Kenney_InterfaceSounds/") || path.Contains("/ThirdParty/Kenney_DigitalAudio/");

        void OnPreprocessAudio()
        {
            if (!Ours(assetPath)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .7f;
            settings.preloadAudioData = true;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
