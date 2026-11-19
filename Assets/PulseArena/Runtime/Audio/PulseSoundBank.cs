using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulseArena
{
    /// Maps event keys to clip variations. Built by PulseAssetsBuilder.BuildSoundBank.
    public class PulseSoundBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string Key;
            public AudioClip[] Clips;
            public float Volume = 1f;
            /// Max simultaneous voices of this key.
            public int MaxVoices = 4;
            public bool Loop;
            /// +/- random pitch (0.04 = +/-4 percent).
            public float PitchVariation = .04f;
            /// Random volume reduction (0.08 = 92..100 percent).
            public float VolumeVariation = .08f;
            /// Minimum seconds between two plays of this key.
            public float Cooldown;
            /// Higher priority sounds may steal voices from lower ones when the pool is full.
            public int Priority;
        }
        public List<Entry> Entries = new List<Entry>();
    }
}
