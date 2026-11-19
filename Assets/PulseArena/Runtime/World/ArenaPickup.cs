using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PulseArena
{
    /// A shield or ammo pickup site. The server decides when a pilot collects it; every peer
    /// shows the model while it is available and a small ring while it recharges.
    public sealed class ArenaPickup : NetworkBehaviour, IPickupSite
    {
        public const float ShieldRespawnSeconds = 15f, AmmoRespawnSeconds = 20f;
        const float PickupRadius = 1.1f, CheckInterval = .1f;

        public PickupKind kind;
        [Tooltip("Hovering, spinning model; hidden while unavailable.")] public Transform visual;
        [Tooltip("Glow ring on the floor; hidden while unavailable.")] public Transform glowRing;
        [Tooltip("Small ring shown only while recharging.")] public Transform rechargeRing;

        readonly NetworkVariable<bool> available = new NetworkVariable<bool>(true);
        static readonly List<ArenaPickup> live = new List<ArenaPickup>();

        float nextCheck, respawnAt, baseY, phase;
        bool subscribed;

        public PickupKind Kind => kind;
        public bool Available => available.Value;
        public Vector3 Position => transform.position;

        public override void OnNetworkSpawn()
        {
            if (!PickupRegistry.Sites.Contains(this)) PickupRegistry.Sites.Add(this);
            if (!live.Contains(this)) live.Add(this);
            available.OnValueChanged += OnAvailableChanged;
            if (visual) baseY = visual.localPosition.y;
            phase = transform.position.x * .37f + transform.position.z * .23f;
            if (IsServer) { available.Value = true; Subscribe(); }
            Apply(available.Value);
        }

        public override void OnNetworkDespawn()
        {
            available.OnValueChanged -= OnAvailableChanged;
            PickupRegistry.Sites.Remove(this);
            live.Remove(this);
            Unsubscribe();
        }

        public override void OnDestroy()
        {
            PickupRegistry.Sites.Remove(this);
            live.Remove(this);
            Unsubscribe();
            base.OnDestroy();
        }

        void Subscribe() { if (subscribed) return; subscribed = true; PulseEvents.PhaseChanged += OnPhase; }
        void Unsubscribe() { if (!subscribed) return; subscribed = false; PulseEvents.PhaseChanged -= OnPhase; }

        // Round start (countdown or play) refills every site.
        void OnPhase(int phaseId)
        {
            if (!IsServer || !IsSpawned) return;
            if (phaseId == 3 || phaseId == 1) available.Value = true;
        }

        void OnAvailableChanged(bool before, bool after) => Apply(after);

        void Apply(bool on)
        {
            if (visual) visual.gameObject.SetActive(on);
            if (glowRing) glowRing.gameObject.SetActive(on);
            if (rechargeRing) rechargeRing.gameObject.SetActive(!on);
        }

        void Update()
        {
            float t = Time.time + phase;
            if (available.Value)
            {
                if (visual)
                {
                    visual.localPosition = new Vector3(0, baseY + Mathf.Sin(t * 2.2f) * .09f, 0);
                    visual.Rotate(0, 85f * Time.deltaTime, 0, Space.Self);
                }
            }
            else if (rechargeRing)
            {
                float s = 1f + Mathf.Sin(t * 3f) * .06f;
                rechargeRing.localScale = new Vector3(s, 1, s);
            }

            if (!IsServer || !IsSpawned) return;
            float now = Time.time;
            if (!available.Value)
            {
                if (now >= respawnAt) available.Value = true;
                return;
            }
            if (now < nextCheck) return;
            nextCheck = now + CheckInterval;

            Vector3 here = transform.position;
            var all = ArenaPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!p || !p.Alive) continue;
                Vector3 d = p.transform.position - here;
                if (Mathf.Abs(d.y) > 1.8f) continue;
                d.y = 0;
                if (d.sqrMagnitude > PickupRadius * PickupRadius) continue;
                if (p.ServerApplyPickup(kind))
                {
                    available.Value = false;
                    respawnAt = now + (kind == PickupKind.Shield ? ShieldRespawnSeconds : AmmoRespawnSeconds);
                    break;
                }
            }
        }

        /// Server only: spawns one pickup per site from ArenaArt.PickupSpots.
        public static void SpawnAll(NetworkManager nm)
        {
            if (!nm || !nm.IsServer) return;
            for (int i = live.Count - 1; i >= 0; i--) if (!live[i]) live.RemoveAt(i);
            if (live.Count > 0) return; // already spawned for this session
            foreach (PickupKind kind in new[] { PickupKind.Shield, PickupKind.Ammo })
            {
                var prefab = Resources.Load<GameObject>(kind == PickupKind.Shield ? "World/Pickup_Shield" : "World/Pickup_Ammo");
                if (!prefab) { Debug.LogWarning("Pickup prefab missing for " + kind + " (run Pulse Arena/2. Generate Game)"); continue; }
                foreach (var spot in ArenaArt.PickupSpots(kind))
                {
                    var go = Instantiate(prefab, spot, Quaternion.identity);
                    go.GetComponent<NetworkObject>().Spawn();
                }
            }
        }
    }
}
