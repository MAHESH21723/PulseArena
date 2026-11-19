using Unity.Netcode;
using UnityEngine;
using System.Linq;

namespace PulseArena
{
    public class ArenaMatch : NetworkBehaviour
    {
        public static ArenaMatch Instance;
        /// 0 lobby, 1 playing, 2 results, 3 countdown.
        public readonly NetworkVariable<int> Phase = new NetworkVariable<int>(0);
        /// Server time when the round ends (set to StartsAt + RoundSeconds when a round is queued).
        public readonly NetworkVariable<double> EndsAt = new NetworkVariable<double>(0);
        /// Server time when the countdown ends and play begins.
        public readonly NetworkVariable<double> StartsAt = new NetworkVariable<double>(0);
        public const int Goal = 15;
        public const float CountdownSeconds = 3f;
        public const float RoundSeconds = 180f;

        int lastCount = -1;

        public double Now => NetworkManager ? NetworkManager.ServerTime.Time : Time.time;

        /// Seconds left on the round clock (full round length during the countdown).
        public double TimeLeft
        {
            get
            {
                int phase = Phase.Value;
                if (phase == 3) return RoundSeconds;
                if (phase != 1) return 0;
                return System.Math.Max(0, EndsAt.Value - Now);
            }
        }

        /// Seconds until play starts during the countdown (0 otherwise).
        public double CountdownLeft => Phase.Value == 3 ? System.Math.Max(0, StartsAt.Value - Now) : 0;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Phase.OnValueChanged += Changed;
            Changed(-1, Phase.Value);
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= Changed;
            if (Instance == this) Instance = null;
        }

        void Changed(int before, int after)
        {
            if (after == 3) lastCount = 4;
            if (before == 3 && after == 1 && lastCount != 0)
            {
                lastCount = 0;
                PulseEvents.RaiseCountdown(0);
            }
            if (after != 3) lastCount = -1;
            if (PulseApp.Instance) PulseApp.Instance.OnPhase(after);
            PulseEvents.RaisePhaseChanged(after);
        }

        void Update()
        {
            if (!IsSpawned) return;
            int phase = Phase.Value;
            if (phase == 3)
            {
                int n = Mathf.Clamp(Mathf.CeilToInt((float)(StartsAt.Value - Now)), 0, 3);
                if (n >= 1 && n < lastCount)
                {
                    lastCount = n;
                    PulseEvents.RaiseCountdown(n);
                }
            }
            if (!IsServer) return;
            if (phase == 3 && Now >= StartsAt.Value) Phase.Value = 1;
            else if (phase == 1 && Now >= EndsAt.Value) Phase.Value = 2;
        }

        public void StartRound()
        {
            if (!IsServer) return;
            foreach (var player in ArenaPlayer.All.ToArray()) player.ResetForRound();
            StartsAt.Value = Now + CountdownSeconds;
            EndsAt.Value = StartsAt.Value + RoundSeconds;
            Phase.Value = 3;
        }

        public void CheckWinner()
        {
            if (IsServer && Phase.Value == 1 && Players().Any(p => p.Kills.Value >= Goal)) Phase.Value = 2;
        }

        public static ArenaPlayer[] Players() => ArenaPlayer.All.Where(p => p).OrderByDescending(p => p.Kills.Value).ThenBy(p => p.Deaths.Value).ThenBy(p => p.CallSign.Value.ToString()).ToArray();
    }
}
