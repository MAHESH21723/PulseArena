using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Collections;

namespace PulseArena
{
    [RequireComponent(typeof(CharacterController), typeof(NetworkObject))]
    public class ArenaPlayer : NetworkBehaviour
    {
        public static ArenaPlayer Local;
        /// Every spawned pilot (humans and bots) on this peer.
        public static readonly List<ArenaPlayer> All = new List<ArenaPlayer>();

        /// Shield points (0 = eliminated).
        public readonly NetworkVariable<int> Health = new NetworkVariable<int>(100);
        /// Magazine of the CURRENT weapon (mirror kept by the server for existing UI code).
        public readonly NetworkVariable<int> Ammo = new NetworkVariable<int>(24);
        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Deaths = new NetworkVariable<int>(0);
        public readonly NetworkVariable<FixedString32Bytes> CallSign = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<bool> Bot = new NetworkVariable<bool>(false);
        /// Server time when the current reload finishes (0 = not reloading).
        public readonly NetworkVariable<double> ReloadUntil = new NetworkVariable<double>(0);
        public readonly NetworkVariable<double> RespawnAt = new NetworkVariable<double>(0);

        /// Current weapon as an int (WeaponId).
        public readonly NetworkVariable<int> Weapon = new NetworkVariable<int>(0);
        /// Server time when the current weapon swap finishes (0 = not swapping).
        public readonly NetworkVariable<double> SwapUntil = new NetworkVariable<double>(0);
        public readonly NetworkVariable<int> MagCarbine = new NetworkVariable<int>(24);
        public readonly NetworkVariable<int> MagScatter = new NetworkVariable<int>(6);
        public readonly NetworkVariable<int> ReserveCarbine = new NetworkVariable<int>(StartReserveCarbine);
        public readonly NetworkVariable<int> ReserveScatter = new NetworkVariable<int>(StartReserveScatter);

        public const int StartReserveCarbine = 96, StartReserveScatter = 18;
        public const int ReserveCapCarbine = 120;
        public const float SwapSeconds = .35f;
        const ulong NoObject = ulong.MaxValue;

        public bool InitializeAsBot;
        public int BotIndex;
        public Transform Model { get; private set; }

        CharacterController controller;
        Transform worldMuzzle;
        bool muzzleSearched;
        Vector2 move;
        float yaw, pitch, vertical, sendAt, nextShot, lastInput;
        bool fire, sprint;
        double immuneUntil;
        BotBrain brain;
        int spawnCount;

        // Scratch buffers so shooting never allocates.
        static readonly ArenaPlayer[] hitTargets = new ArenaPlayer[16];
        static readonly int[] hitDamage = new int[16];
        static readonly Vector3[] pelletEnds = new Vector3[WeaponDef.Get((int)WeaponId.Scatter).Pellets];

        public double Now => NetworkManager ? NetworkManager.ServerTime.Time : Time.time;
        public bool Alive => Health.Value > 0;
        public bool Reloading => ReloadUntil.Value > Now;
        public bool Swapping => SwapUntil.Value > Now;
        public Vector3 Eye => transform.position + Vector3.up * 1.65f;

        public WeaponId CurrentWeapon => (WeaponId)Mathf.Clamp(Weapon.Value, 0, WeaponDef.All.Length - 1);
        public WeaponDef CurrentDef => WeaponDef.Get(Weapon.Value);
        public int MagazineOf(WeaponId id) => MagVar((int)id).Value;
        public int ReserveOf(WeaponId id) => ReserveVar((int)id).Value;
        public int CurrentReserve => ReserveOf(CurrentWeapon);

        /// Where a shot from this pilot visually leaves the gun, as seen by other peers.
        public Vector3 WorldMuzzlePosition
        {
            get
            {
                if (!muzzleSearched && Model)
                {
                    muzzleSearched = true;
                    foreach (var t in Model.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Muzzle") { worldMuzzle = t; break; }
                    }
                }
                return worldMuzzle ? worldMuzzle.position : transform.TransformPoint(new Vector3(.32f, 1.13f, .85f));
            }
        }

        NetworkVariable<int> MagVar(int w) => w == (int)WeaponId.Scatter ? MagScatter : MagCarbine;
        NetworkVariable<int> ReserveVar(int w) => w == (int)WeaponId.Scatter ? ReserveScatter : ReserveCarbine;
        static int ReserveCap(int w) => w == (int)WeaponId.Scatter ? WeaponDef.Get(w).MagSize * 3 : ReserveCapCarbine;

        void SetMag(int w, int value)
        {
            MagVar(w).Value = value;
            if (w == Weapon.Value) Ammo.Value = value;
        }

        void Awake() { controller = GetComponent<CharacterController>(); }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) All.Add(this);
            if (IsServer)
            {
                Bot.Value = InitializeAsBot;
                if (InitializeAsBot) brain = new BotBrain(this);
                CallSign.Value = new FixedString32Bytes(InitializeAsBot ? new[] { "VECTOR", "ION", "NOVA" }[BotIndex % 3] : "PILOT " + OwnerClientId);
                ResetForRound();
            }
            Model = ArenaArt.Robot(transform, Bot.Value ? "Amber" : "Cyan");
            if (IsOwner && !Bot.Value)
            {
                Local = this;
                Model.gameObject.SetActive(false);
                SetNameServerRpc(PulseApp.Instance.PlayerName);
                PulseApp.Instance.AttachCamera(this);
            }
            Health.OnValueChanged += OnHealth;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Health.OnValueChanged -= OnHealth;
            if (Local == this) Local = null;
        }

        void OnHealth(int before, int after)
        {
            if (Local != this) return;
            if (after < before && PulseApp.Instance) PulseApp.Instance.DamageFlash = 1;
        }

        [ServerRpc]
        void SetNameServerRpc(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "PILOT";
            var clean = System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9 _-]", "");
            CallSign.Value = new FixedString32Bytes(clean.Substring(0, Mathf.Min(clean.Length, 16)));
        }

        void Update()
        {
            if (!IsSpawned) return;
            int phase = ArenaMatch.Instance ? ArenaMatch.Instance.Phase.Value : 0;
            bool playing = phase == 1;
            bool inRound = phase == 1 || phase == 3;
            if (controller.enabled != Alive) controller.enabled = Alive;
            // Keep a defeated pilot visible ~2 s so the death animation plays, then hide until respawn.
            if (Model && Local != this) Model.gameObject.SetActive(Alive || RespawnAt.Value - Now > 1.0);

            if (IsOwner && !Bot.Value && !PulseSmoke.SuppressPlayerInput && Time.unscaledTime >= sendAt)
            {
                sendAt = Time.unscaledTime + .033f;
                var input = PulseApp.Instance.Controls;
                bool enabled = playing && Alive && !PulseApp.Instance.Paused;
                InputServerRpc(enabled ? input.Move : Vector2.zero, input.Yaw, input.Pitch, enabled && input.Fire, enabled && input.Sprint);
            }

            if (IsServer && inRound) ServerTick(playing);
        }

        void ServerTick(bool playing)
        {
            if (!Alive)
            {
                if (playing && Now >= RespawnAt.Value) Respawn();
                return;
            }

            // Reload / swap completion.
            if (ReloadUntil.Value > 0 && Now >= ReloadUntil.Value) FinishReload();
            if (SwapUntil.Value > 0 && Now >= SwapUntil.Value) SwapUntil.Value = 0;

            if (Bot.Value)
            {
                if (playing && brain != null)
                {
                    var c = brain.Think(Time.deltaTime);
                    move = Vector2.ClampMagnitude(c.Move, 1);
                    yaw = Mathf.Repeat(c.Yaw, 360);
                    pitch = Mathf.Clamp(c.Pitch, -75, 75);
                    fire = c.Fire;
                    sprint = c.Sprint;
                    if (c.Weapon >= 0 && c.Weapon != Weapon.Value) BeginSwap(c.Weapon);
                    if (c.Reload) BeginReload();
                }
            }
            else if (Time.unscaledTime - lastInput > .35f)
            {
                move = Vector2.zero;
                fire = false;
            }

            // Countdown: pilots may look around but not move or fire.
            if (!playing)
            {
                move = Vector2.zero;
                fire = false;
                sprint = false;
            }

            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 direction = transform.right * move.x + transform.forward * move.y;
            vertical = controller.isGrounded ? -2 : Mathf.Max(vertical - 22 * Time.deltaTime, -30);
            controller.Move((direction * (sprint ? 7.8f : 5.4f) + Vector3.up * vertical) * Time.deltaTime);

            if (!playing) return;
            if (fire && !sprint) TryShoot();
            int w = Weapon.Value;
            if (MagVar(w).Value == 0 && ReserveVar(w).Value > 0 && !Reloading && !Swapping) BeginReload();
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        void InputServerRpc(Vector2 value, float y, float p, bool shooting, bool running)
        {
            if (Bot.Value || !Finite(value.x) || !Finite(value.y) || !Finite(y) || !Finite(p)) return;
            move = Vector2.ClampMagnitude(value, 1);
            yaw = Mathf.Repeat(y, 360);
            pitch = Mathf.Clamp(p, -75, 75);
            fire = shooting;
            sprint = running;
            lastInput = Time.unscaledTime;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        public void TestInput(Vector2 v, float y, float p, bool f)
        {
            if (Debug.isDebugBuild || Application.isEditor) InputServerRpc(v, y, p, f, false);
        }

        // ---------- Reload / swap ----------

        [ServerRpc]
        public void ReloadServerRpc()
        {
            if (Alive && ArenaMatch.Instance && ArenaMatch.Instance.Phase.Value == 1) BeginReload();
        }

        void BeginReload()
        {
            int w = Weapon.Value;
            var def = WeaponDef.Get(w);
            if (Reloading || Swapping || MagVar(w).Value >= def.MagSize || ReserveVar(w).Value <= 0) return;
            ReloadUntil.Value = Now + def.ReloadSeconds;
            ReloadStartedClientRpc();
        }

        void FinishReload()
        {
            int w = Weapon.Value;
            var def = WeaponDef.Get(w);
            int take = Mathf.Min(def.MagSize - MagVar(w).Value, ReserveVar(w).Value);
            if (take > 0)
            {
                SetMag(w, MagVar(w).Value + take);
                ReserveVar(w).Value -= take;
            }
            ReloadUntil.Value = 0;
        }

        /// Toggle between weapons. Call from the owning client (or the host).
        public void RequestSwap()
        {
            if (!IsSpawned || !IsOwner) return;
            SwapServerRpc(1 - Weapon.Value);
        }

        /// Switch to a specific weapon. Call from the owning client (or the host).
        public void RequestSwap(WeaponId id)
        {
            if (!IsSpawned || !IsOwner) return;
            SwapServerRpc((int)id);
        }

        [ServerRpc]
        void SwapServerRpc(int id)
        {
            int phase = ArenaMatch.Instance ? ArenaMatch.Instance.Phase.Value : 0;
            if (Alive && (phase == 1 || phase == 3)) BeginSwap(id);
        }

        void BeginSwap(int id)
        {
            id = Mathf.Clamp(id, 0, WeaponDef.All.Length - 1);
            if (id == Weapon.Value) return;
            ReloadUntil.Value = 0;
            Weapon.Value = id;
            Ammo.Value = MagVar(id).Value;
            SwapUntil.Value = Now + SwapSeconds;
            nextShot = Time.time + SwapSeconds;
            WeaponSwappedClientRpc(id);
        }

        [ClientRpc]
        void ReloadStartedClientRpc() { PulseEvents.RaiseReloadStarted(this); }

        [ClientRpc]
        void WeaponSwappedClientRpc(int id) { PulseEvents.RaiseWeaponSwapped(this, (WeaponId)Mathf.Clamp(id, 0, WeaponDef.All.Length - 1)); }

        // ---------- Shooting ----------

        void TryShoot()
        {
            int w = Weapon.Value;
            var def = WeaponDef.Get(w);
            if (Reloading || Swapping || MagVar(w).Value <= 0 || Time.time < nextShot) return;
            nextShot = Time.time + def.Interval;
            SetMag(w, MagVar(w).Value - 1);

            Vector3 origin = Eye;
            Quaternion aim = Quaternion.Euler(pitch, yaw, 0);
            Vector3 forward = aim * Vector3.forward;
            Vector3 muzzle = origin + forward * .65f;
            int targetCount = 0;
            bool hitPlayer = false;

            if (def.Pellets <= 1)
            {
                Vector3 end = origin + forward * def.Range;
                if (Physics.Raycast(origin, forward, out var hit, def.Range, ~0, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    hitPlayer |= RegisterHit(hit.collider, def.Damage, ref targetCount);
                }
                // Bullet magnetism (human shooters only): a near miss within a small angle of an enemy still connects.
                if (!hitPlayer && !Bot.Value && PullToward(origin, forward, def.Range, out var magnetHit))
                {
                    end = magnetHit.point;
                    hitPlayer |= RegisterHit(magnetHit.collider, def.Damage, ref targetCount);
                }
                ShotClientRpc(muzzle, end, hitPlayer, w);
            }
            else
            {
                int mask = 0;
                int count = Mathf.Min(def.Pellets, pelletEnds.Length);
                for (int i = 0; i < count; i++)
                {
                    Vector2 offset = i == 0 ? Vector2.zero : Random.insideUnitCircle * def.Spread;
                    Vector3 direction = aim * (Quaternion.Euler(offset.y, offset.x, 0) * Vector3.forward);
                    Vector3 end = origin + direction * def.Range;
                    if (Physics.Raycast(origin, direction, out var hit, def.Range, ~0, QueryTriggerInteraction.Ignore))
                    {
                        end = hit.point;
                        if (RegisterHit(hit.collider, def.Damage, ref targetCount))
                        {
                            hitPlayer = true;
                            mask |= 1 << i;
                        }
                    }
                    pelletEnds[i] = end;
                }
                ShotClientRpc(muzzle, pelletEnds[0], hitPlayer, w);
                PelletsClientRpc(muzzle, pelletEnds, mask);
            }

            for (int i = 0; i < targetCount; i++)
            {
                hitTargets[i].ReceiveDamage(hitDamage[i], this, (WeaponId)w);
                hitTargets[i] = null;
            }
        }

        /// Server-side bullet magnetism: finds an enemy chest/head within a small angle of the aim ray
        /// (3.5 degrees close up, 1.5 at 40 m+) that has a clear line of sight, and returns that hit.
        bool PullToward(Vector3 origin, Vector3 forward, float range, out RaycastHit result)
        {
            result = default;
            float bestAngle = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var p = All[i];
                if (!p || p == this || !p.Alive || Now <= p.immuneUntil) continue;
                for (int part = 0; part < 2; part++)
                {
                    Vector3 point = p.transform.position + Vector3.up * (part == 0 ? 1.15f : 1.55f);
                    Vector3 d = point - origin;
                    float dist = d.magnitude;
                    if (dist < .5f || dist > range) continue;
                    float angle = Vector3.Angle(forward, d);
                    if (angle >= bestAngle || angle > Mathf.Lerp(3.5f, 1.5f, Mathf.Clamp01(dist / 40f))) continue;
                    if (!Physics.Raycast(origin, d / dist, out var hit, dist + .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.collider.GetComponentInParent<ArenaPlayer>() != p) continue;
                    bestAngle = angle;
                    result = hit;
                }
            }
            return bestAngle < float.MaxValue;
        }

        /// Adds damage for a pilot hit by a ray. Returns true when a valid pilot was hit.
        bool RegisterHit(Collider collider, int damage, ref int targetCount)
        {
            var target = collider.GetComponentInParent<ArenaPlayer>();
            if (!target || target == this || !target.Alive || Now <= target.immuneUntil) return false;
            for (int i = 0; i < targetCount; i++)
            {
                if (hitTargets[i] == target) { hitDamage[i] += damage; return true; }
            }
            if (targetCount < hitTargets.Length)
            {
                hitTargets[targetCount] = target;
                hitDamage[targetCount] = damage;
                targetCount++;
            }
            return true;
        }

        [ClientRpc]
        void ShotClientRpc(Vector3 from, Vector3 to, bool hit, int weapon)
        {
            var id = (WeaponId)Mathf.Clamp(weapon, 0, WeaponDef.All.Length - 1);
            PulseEvents.RaiseShot(this, id, from, to, hit);
            if (Local == this && PulseApp.Instance)
            {
                PulseApp.Instance.Recoil = 1;
                if (hit) PulseApp.Instance.HitMarker = 1;
            }
        }

        [ClientRpc]
        void PelletsClientRpc(Vector3 from, Vector3[] ends, int hitMask)
        {
            if (PulseEffects.Instance) PulseEffects.Instance.OnPellets(this, from, ends, hitMask);
        }

        // ---------- Damage / elimination ----------

        public bool ReceiveDamage(int amount, ArenaPlayer attacker) => ReceiveDamage(amount, attacker, WeaponId.Carbine);

        public bool ReceiveDamage(int amount, ArenaPlayer attacker, WeaponId weapon)
        {
            if (!IsServer || !Alive || Now <= immuneUntil) return false;
            amount = Mathf.Max(0, amount);
            Health.Value = Mathf.Max(0, Health.Value - amount);
            DamagedClientRpc(amount, attacker ? attacker.NetworkObjectId : NoObject, attacker ? attacker.Eye : transform.position);
            if (Health.Value > 0) return false;

            Deaths.Value++;
            if (attacker) attacker.Kills.Value++;
            RespawnAt.Value = Now + 3;
            move = Vector2.zero;
            fire = false;
            ReloadUntil.Value = 0;
            SwapUntil.Value = 0;
            EliminatedClientRpc(attacker ? attacker.NetworkObjectId : NoObject, (int)weapon);
            if (ArenaMatch.Instance) ArenaMatch.Instance.CheckWinner();
            return true;
        }

        ArenaPlayer Resolve(ulong id)
        {
            if (id == NoObject || NetworkManager == null || NetworkManager.SpawnManager == null) return null;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var o) && o ? o.GetComponent<ArenaPlayer>() : null;
        }

        [ClientRpc]
        void DamagedClientRpc(int amount, ulong attackerId, Vector3 attackerPosition)
        {
            PulseEvents.RaiseDamaged(this, Resolve(attackerId), amount, attackerPosition);
        }

        [ClientRpc]
        void EliminatedClientRpc(ulong attackerId, int weapon)
        {
            var attacker = Resolve(attackerId);
            PulseEvents.RaiseEliminated(this, attacker, (WeaponId)Mathf.Clamp(weapon, 0, WeaponDef.All.Length - 1));
            if (attacker && attacker == Local && attacker != this && PulseApp.Instance) PulseApp.Instance.Announce("ELIMINATION  +100");
        }

        // ---------- Pickups (server only) ----------

        /// Applies a pickup. Returns false (pickup not consumed) when it would have no effect.
        public bool ServerApplyPickup(PickupKind kind)
        {
            if (!IsServer || !Alive) return false;
            if (kind == PickupKind.Shield)
            {
                if (Health.Value >= 100) return false;
                Health.Value = Mathf.Min(100, Health.Value + 50);
            }
            else
            {
                bool changed = false;
                for (int w = 0; w < WeaponDef.All.Length; w++)
                {
                    var reserve = ReserveVar(w);
                    int cap = ReserveCap(w);
                    if (reserve.Value >= cap) continue;
                    reserve.Value = Mathf.Min(cap, reserve.Value + WeaponDef.Get(w).MagSize);
                    changed = true;
                }
                if (!changed) return false;
            }
            PickedUpClientRpc((int)kind, transform.position);
            return true;
        }

        [ClientRpc]
        void PickedUpClientRpc(int kind, Vector3 at) { PulseEvents.RaisePickedUp(this, (PickupKind)kind, at); }

        // ---------- Round / respawn ----------

        public void ResetForRound()
        {
            if (!IsServer) return;
            Kills.Value = 0;
            Deaths.Value = 0;
            Respawn();
        }

        void ResetLoadout()
        {
            Weapon.Value = 0;
            MagCarbine.Value = WeaponDef.Get(0).MagSize;
            MagScatter.Value = WeaponDef.Get(1).MagSize;
            ReserveCarbine.Value = StartReserveCarbine;
            ReserveScatter.Value = StartReserveScatter;
            Ammo.Value = MagCarbine.Value;
            ReloadUntil.Value = 0;
            SwapUntil.Value = 0;
        }

        void Respawn()
        {
            int slot = Bot.Value ? BotIndex + 1 : (int)OwnerClientId;
            Vector3 spot = ArenaArt.Spawn(slot + spawnCount++);
            controller.enabled = false;
            transform.position = spot;
            transform.rotation = Quaternion.LookRotation(new Vector3(-spot.x, 0, -spot.z));
            controller.enabled = true;
            yaw = transform.eulerAngles.y;
            pitch = 0;
            vertical = 0;
            move = Vector2.zero;
            fire = false;
            brain?.Reset(yaw);
            Health.Value = 100;
            ResetLoadout();
            RespawnAt.Value = 0;
            immuneUntil = Now + 1.5;
            GetComponent<NetworkTransform>().Teleport(spot, transform.rotation, Vector3.one);
            RespawnClientRpc(yaw);
        }

        [ClientRpc]
        void RespawnClientRpc(float facing)
        {
            PulseEvents.RaiseRespawned(this);
            if (IsOwner && !Bot.Value && PulseApp.Instance)
            {
                PulseApp.Instance.Controls.Yaw = facing;
                PulseApp.Instance.Controls.Pitch = 0;
            }
        }
    }
}
