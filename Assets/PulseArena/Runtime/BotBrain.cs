using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PulseArena
{
    // Server-only decision making for practice bots. ArenaPlayer owns movement, shooting and
    // damage; the brain only returns a BotCommand each frame.
    //
    // Structure: Decide() runs at ~8-9 Hz per bot (staggered) and does perception, state
    // choice (simple utility scoring) and path planning. Act() runs every frame and does the
    // cheap work: follow the cached path, turn/aim with a limited turn rate, fire gating.
    // Navigation comes from BotNav (runtime NavMesh); without one the bot wanders directly.
    public class BotBrain
    {
        enum State { Roam, Fight, Cover, SeekShield, SeekAmmo, Investigate }
        enum Mode { None, Path, Local }

        const float EyeH = 1.65f, ChestH = 1.2f;

        readonly ArenaPlayer self;
        readonly float seed;

        // ---- difficulty (set in Reset) ----
        BotDifficulty diff;
        float cone, reaction, turnRate, errBase, errFloor, tolFudge, lead, strafeMag, moveScale;
        float memory, hearRange, coverShield, seekShield, burstOnMin, burstOnMax, burstOffMin, burstOffMax, carbinePref;

        // ---- state ----
        float yaw, pitch, wobY, wobP;
        State state;
        Mode mode;
        float nextDecide;

        // perception
        ArenaPlayer target;
        bool visible;
        float lastVisAt = -99, acquireAt, reactionAt;
        Vector3 lastSeenPos, prevTargetPos, targetVel;
        float lastSeenTime = -99, prevTargetTime;
        Vector3 heardPos, hitPos;
        float heardTime = -99, hitTime = -99, alertUntil;
        float eDist = 999;

        // weapon / combat
        int wantWeapon = -1;
        float nextSwitch;
        bool needReload;
        bool burstOn = true;
        float burstSwitchAt;
        int approach;
        float strafeDir = 1, strafeUntil;
        bool blendStrafe;

        // navigation
        readonly NavMeshPath navPath = new NavMeshPath();
        readonly Vector3[] corners = new Vector3[32];
        readonly float[] suffix = new float[33];
        int cornerCount, pathIdx;
        bool goalSet, arrived;
        Vector3 goal;
        float arriveRadius = .7f, lastPathAt, goalExpire;
        Vector3 sep;
        IPickupSite seekSite;
        IPickupSite ignoreSite; float ignoreUntil;
        float arrivedAt;

        // cover
        float coverHoldUntil = -1, coverTravelUntil, coverCooldown, coverBlockUntil;

        // unstick
        Vector3 stuckPos;
        float stuckCheckAt, wantMoveTime, nudgeUntil;
        Vector3 nudgeDir;
        bool navWasReady;

        public BotBrain(ArenaPlayer owner)
        {
            self = owner;
            seed = (owner ? owner.BotIndex : 0) * 17.31f + Random.value * 100f;
            Hook();
            Brains.Add(this);
            Reset(owner ? owner.transform.eulerAngles.y : 0);
        }

        /// Called by ArenaPlayer after every respawn.
        public void Reset(float facing)
        {
            ApplyDifficulty(PulseSettings.Difficulty);
            yaw = facing; pitch = 0; wobY = wobP = 0;
            state = State.Roam; mode = Mode.None;
            target = null; visible = false; lastVisAt = lastSeenTime = heardTime = hitTime = -99; alertUntil = 0;
            targetVel = Vector3.zero; eDist = 999;
            wantWeapon = -1; needReload = false; burstOn = true; burstSwitchAt = 0; nextSwitch = 0;
            goalSet = false; cornerCount = pathIdx = 0; arrived = false; seekSite = null; ignoreSite = null;
            coverHoldUntil = -1; coverCooldown = coverBlockUntil = 0;
            nudgeUntil = 0; wantMoveTime = 0;
            float now = Time.time;
            nextDecide = now + Random.Range(0f, .12f);   // stagger bots
            stuckCheckAt = now + 1f; stuckPos = self ? self.transform.position : Vector3.zero;
            strafeUntil = 0;
        }

        void ApplyDifficulty(BotDifficulty d)
        {
            diff = d;
            switch (d)
            {
                case BotDifficulty.Easy:
                    cone = 75; reaction = .70f; turnRate = 120; errBase = 5.5f; errFloor = .60f; tolFudge = 2.5f; lead = 0f;
                    strafeMag = .6f; moveScale = .85f; memory = 2f; hearRange = 20; coverShield = 0; seekShield = 35;
                    burstOnMin = .25f; burstOnMax = .45f; burstOffMin = .5f; burstOffMax = 1.0f; carbinePref = 18; break;
                case BotDifficulty.Hard:
                    cone = 110; reaction = .25f; turnRate = 300; errBase = 1.6f; errFloor = .45f; tolFudge = .8f; lead = .08f;
                    strafeMag = 1f; moveScale = 1f; memory = 4f; hearRange = 40; coverShield = 50; seekShield = 50;
                    burstOnMin = .5f; burstOnMax = 1.1f; burstOffMin = .15f; burstOffMax = .4f; carbinePref = 14; break;
                default:
                    cone = 90; reaction = .45f; turnRate = 200; errBase = 3.0f; errFloor = .50f; tolFudge = 1.5f; lead = .03f;
                    strafeMag = .85f; moveScale = .95f; memory = 3f; hearRange = 30; coverShield = 50; seekShield = 50;
                    burstOnMin = .35f; burstOnMax = .7f; burstOffMin = .3f; burstOffMax = .6f; carbinePref = 16; break;
            }
        }

        // ================= adapter: combat/weapon API of ArenaPlayer (adjust here if names shift) =================
        WeaponId CurWeapon => self.CurrentWeapon;
        int MagOf(WeaponId w) => self.MagazineOf(w);
        int ReserveOf(WeaponId w) => self.ReserveOf(w);
        bool HasAmmo(WeaponId w) => MagOf(w) + ReserveOf(w) > 0;
        // ===========================================================================================================

        // ================= shared event hints =================
        static readonly List<BotBrain> Brains = new List<BotBrain>();
        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (hooked) { PulseEvents.Shot -= OnShot; PulseEvents.Damaged -= OnDamaged; }
            hooked = false; Brains.Clear();
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            PulseEvents.Shot += OnShot;
            PulseEvents.Damaged += OnDamaged;
        }

        static void OnShot(ArenaPlayer shooter, WeaponId w, Vector3 from, Vector3 to, bool hit)
        {
            float now = Time.time;
            for (int i = Brains.Count - 1; i >= 0; i--)
            {
                var b = Brains[i];
                if (b.self == null) { Brains.RemoveAt(i); continue; }
                if (shooter == b.self || !b.self.Alive) continue;
                if ((from - b.self.transform.position).sqrMagnitude > b.hearRange * b.hearRange) continue;
                b.heardPos = from; b.heardTime = now; b.alertUntil = Mathf.Max(b.alertUntil, now + 2.5f);
            }
        }

        static void OnDamaged(ArenaPlayer victim, ArenaPlayer attacker, int amount, Vector3 from)
        {
            float now = Time.time;
            for (int i = Brains.Count - 1; i >= 0; i--)
            {
                var b = Brains[i];
                if (b.self == null) { Brains.RemoveAt(i); continue; }
                if (victim != b.self) continue;
                b.hitPos = attacker ? attacker.transform.position + Vector3.up * ChestH : from;
                b.hitTime = now; b.alertUntil = Mathf.Max(b.alertUntil, now + 3.5f);
            }
        }

        // ================= per frame =================
        public BotCommand Think(float dt)
        {
            var cmd = new BotCommand { Weapon = -1 };
            if (self == null) return cmd;
            float now = Time.time;
            BotNav.Ensure();
            if (now >= nextDecide)
            {
                nextDecide = now + .10f + Random.value * .03f;
                Decide(now);
            }
            Act(ref cmd, now, dt);
            return cmd;
        }

        // ================= decision tick (~8-9 Hz) =================
        void Decide(float now)
        {
            var pos = self.transform.position;
            var eye = pos + Vector3.up * EyeH;
            int shield = self.Health.Value;
            bool navReady = BotNav.Ready;
            if (navReady != navWasReady) { navWasReady = navReady; goalSet = false; }

            Perceive(now, pos, eye);

            // Hints: memory of where the enemy probably is.
            if (heardTime > lastSeenTime && now - heardTime < memory) { lastSeenPos = heardPos; lastSeenTime = heardTime; }
            if (hitTime > lastSeenTime) { lastSeenPos = hitPos; lastSeenTime = hitTime; }
            bool known = visible || now - lastSeenTime < memory;
            if (!visible && !known) target = null;
            if (visible) eDist = Vector3.Distance(pos, target.transform.position);
            else if (known) eDist = Vector3.Distance(pos, lastSeenPos);
            else eDist = 999;

            // Separation from other pilots (cheap, no allocation).
            sep = Vector3.zero;
            var all = ArenaPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p == self || !p.Alive) continue;
                var d = pos - p.transform.position; d.y = 0;
                float m = d.sqrMagnitude;
                if (m < 2.25f && m > .0001f) { float mm = Mathf.Sqrt(m); sep += d / mm * (1f - mm / 1.5f); }
            }

            DecideWeapon(now, known);
            ChooseAndRun(now, pos, eye, shield, known);
            CheckStuck(now, pos);
        }

        void Perceive(float now, Vector3 pos, Vector3 eye)
        {
            var all = ArenaPlayer.All;
            float halfCone = (cone + (now < alertUntil ? 50f : 0f)) * .5f;
            float fy = yaw * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(fy), 0, Mathf.Cos(fy));
            ArenaPlayer best = null; float bestEff = float.MaxValue; int rays = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p == self || !p.Alive) continue;
                var d = p.transform.position - pos;
                float dist = d.magnitude;
                if (dist > 70f) continue;
                float eff = p == target ? dist * .75f : dist;
                if (eff >= bestEff) continue;
                d.y = 0;
                float ang = dist < 1.5f ? 0 : Vector3.Angle(fwd, d);
                bool tracking = p == target && visible && now - lastVisAt < .4f;
                if (ang > halfCone && !tracking) continue;
                if (rays >= 3) break;
                rays++;
                if (!HasLoS(eye, p)) continue;
                best = p; bestEff = eff;
            }

            bool wasVisible = visible;
            if (best != null)
            {
                bool fresh = best != target || (!wasVisible && now - lastVisAt > .6f);
                var bp = best.transform.position;
                if (best == target && wasVisible && now > prevTargetTime + .02f)
                {
                    var v = (bp - prevTargetPos) / (now - prevTargetTime); v.y = 0;
                    targetVel = Vector3.Lerp(targetVel, v, .6f);
                }
                else targetVel = Vector3.zero;
                prevTargetPos = bp; prevTargetTime = now;
                if (fresh)
                {
                    acquireAt = now;
                    reactionAt = now + reaction * Random.Range(.85f, 1.25f);
                    burstOn = true; burstSwitchAt = reactionAt + Random.Range(burstOnMin, burstOnMax);
                }
                target = best; visible = true; lastVisAt = now;
                lastSeenPos = bp + Vector3.up * ChestH; lastSeenTime = now;
            }
            else visible = false;
        }

        bool HasLoS(Vector3 eye, ArenaPlayer p)
        {
            var chest = p.transform.position + Vector3.up * ChestH;
            var d = chest - eye; float len = d.magnitude;
            if (len < 1f) return true;
            var dir = d / len;
            // start slightly ahead so our own capsule is never the first hit
            if (!Physics.Raycast(eye + dir * .4f, dir, out var hit, len - .4f, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<ArenaPlayer>() == p;
        }

        void DecideWeapon(float now, bool known)
        {
            var cur = CurWeapon;
            var def = WeaponDef.Get((int)cur);
            int mag = MagOf(cur), reserve = ReserveOf(cur);
            bool carbineOk = HasAmmo(WeaponId.Carbine), scatterOk = HasAmmo(WeaponId.Scatter);

            var want = cur;
            if (known)
            {
                if (eDist < 8f && scatterOk) want = WeaponId.Scatter;
                else if ((eDist > 10f || !scatterOk) && carbineOk) want = WeaponId.Carbine;
            }
            if (mag == 0 && reserve == 0)
            {   // dry: use the other weapon if it has anything
                var other = cur == WeaponId.Carbine ? WeaponId.Scatter : WeaponId.Carbine;
                if (HasAmmo(other)) want = other;
            }
            wantWeapon = (want != cur && now >= nextSwitch && !self.Reloading) ? (int)want : -1;

            needReload = !self.Reloading && reserve > 0 &&
                (mag == 0 || (!visible && mag <= Mathf.CeilToInt(def.MagSize * .35f) && mag < def.MagSize));
        }

        // ---- utility state selection ----
        void ChooseAndRun(float now, Vector3 pos, Vector3 eye, int shield, bool known)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool reloading = self.Reloading;
                bool threatened = visible || now - hitTime < 3f;

                float sFight = visible ? .7f : 0f;
                float sCover = 0f;
                if (threatened && known && now >= coverBlockUntil)
                {
                    if (reloading && eDist < 30f) sCover = .9f;
                    else if (diff != BotDifficulty.Easy && shield < coverShield && now >= coverCooldown)
                        sCover = .75f + (coverShield - shield) * .004f;
                }
                if (state == State.Cover && coverHoldUntil > 0 && now < coverHoldUntil) sCover = Mathf.Max(sCover, 1.2f);

                float sShield = 0f;
                if (shield < seekShield && (!visible || eDist > 18f) && FindSite(PickupKind.Shield, pos) != null)
                    sShield = .6f + (seekShield - shield) * .004f;
                float sAmmo = 0f;
                if (!visible && ReserveOf(WeaponId.Carbine) < WeaponDef.Get(0).MagSize && FindSite(PickupKind.Ammo, pos) != null)
                    sAmmo = .5f;
                float sInv = (!visible && now - lastSeenTime < memory) ? .45f : 0f;
                float sRoam = .1f;

                float h = .15f;
                var best = State.Roam; float bs = sRoam + (state == State.Roam ? h : 0);
                Consider(State.Fight, sFight, ref best, ref bs);
                Consider(State.Cover, sCover, ref best, ref bs);
                Consider(State.SeekShield, sShield, ref best, ref bs);
                Consider(State.SeekAmmo, sAmmo, ref best, ref bs);
                Consider(State.Investigate, sInv, ref best, ref bs);

                if (best != state) { state = best; goalSet = false; arrived = false; seekSite = null; coverHoldUntil = -1; }
                if (RunState(now, pos, eye)) return;
                // state could not start (no cover / no path): it set a block, re-evaluate once
                goalSet = false;
            }
            state = State.Roam; RunState(now, pos, eye);
        }

        void Consider(State s, float score, ref State best, ref float bs)
        {
            if (score <= 0) return;
            if (state == s) score += .15f;
            if (score > bs) { bs = score; best = s; }
        }

        bool RunState(float now, Vector3 pos, Vector3 eye)
        {
            mode = Mode.None; blendStrafe = false;
            switch (state)
            {
                case State.Fight: return RunFight(now, pos);
                case State.Cover: return RunCover(now, pos, eye);
                case State.SeekShield: return RunSeek(now, pos, PickupKind.Shield);
                case State.SeekAmmo: return RunSeek(now, pos, PickupKind.Ammo);
                case State.Investigate: return RunInvestigate(now, pos);
                default: return RunRoam(now, pos);
            }
        }

        bool RunFight(float now, Vector3 pos)
        {
            if (!visible || target == null) return false;
            var cur = CurWeapon;
            float pref = cur == WeaponId.Scatter ? 4.5f : carbinePref;
            float lo = cur == WeaponId.Scatter ? 2.5f : pref - 3f;
            approach = eDist > pref + 3f ? 1 : (eDist < lo ? -1 : 0);

            if (now >= strafeUntil)
            {
                strafeDir = Random.value < .5f ? -1f : 1f;
                strafeUntil = now + Random.Range(.6f, 1.6f);
            }
            var tp = target.transform.position;
            if (approach == 1)
            {
                if (!goalSet || now - lastPathAt > .7f || (goal - tp).sqrMagnitude > 4f)
                {
                    if (!SetGoal(now, tp, 1.5f, false)) goalSet = false;
                }
                if (goalSet) { mode = Mode.Path; blendStrafe = true; }
            }
            if (mode != Mode.Path)
            {
                mode = Mode.Local;
                // probe the direction we intend to move; flip / drop the approach if a wall is there
                var wd = FightDir(pos);
                if (wd.sqrMagnitude > .01f)
                {
                    if (Physics.Raycast(pos + Vector3.up * .5f, wd.normalized, 1.1f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        strafeDir = -strafeDir; strafeUntil = now + Random.Range(.6f, 1.2f);
                        wd = FightDir(pos);
                        if (Physics.Raycast(pos + Vector3.up * .5f, wd.normalized, 1.1f, ~0, QueryTriggerInteraction.Ignore))
                            approach = 0;
                    }
                }
            }
            return true;
        }

        // World-space local-combat movement: strafe around the enemy, close/open distance.
        Vector3 FightDir(Vector3 pos)
        {
            Vector3 tp = (visible && target != null) ? target.transform.position : lastSeenPos;
            var toT = tp - pos; toT.y = 0;
            float m = toT.magnitude;
            if (m < .01f) return Vector3.zero;
            toT /= m;
            var right = new Vector3(toT.z, 0, -toT.x);
            var dir = right * (strafeDir * strafeMag) + toT * (approach * .8f);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            return dir;
        }

        bool RunCover(float now, Vector3 pos, Vector3 eye)
        {
            if (!goalSet)
            {
                if (!FindCover(pos, out var spot)) { coverBlockUntil = now + 3f; return false; }
                if (!SetGoal(now, spot, .8f, false)) { coverBlockUntil = now + 3f; return false; }
                coverTravelUntil = now + 6f; coverHoldUntil = -1;
            }
            if (arrived)
            {
                if (coverHoldUntil < 0)
                {
                    coverHoldUntil = now + Random.Range(2.5f, 4f);
                    if (self.Reloading) coverHoldUntil = Mathf.Max(coverHoldUntil, now + 1.8f);
                }
                else if (now >= coverHoldUntil)
                {
                    coverCooldown = now + 6f; coverBlockUntil = now + 1.5f; coverHoldUntil = -1;
                    goalSet = false; return false;
                }
                mode = Mode.None;
            }
            else
            {
                if (now > coverTravelUntil) { coverBlockUntil = now + 3f; goalSet = false; return false; }
                mode = Mode.Path;
            }
            return true;
        }

        bool FindCover(Vector3 pos, out Vector3 spot)
        {
            spot = pos;
            var enemyEye = lastSeenPos + Vector3.up * (EyeH - ChestH);
            float baseAng = Random.value * 360f, bestScore = float.MaxValue;
            bool found = false;
            float enemyToSelf = Vector3.Distance(pos, enemyEye);
            for (int i = 0; i < 8; i++)
            {
                float a = (baseAng + i * 45f) * Mathf.Deg2Rad;
                float r = Random.Range(4f, 9f);
                var cand = pos + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
                Vector3 c;
                if (BotNav.Ready) { if (!BotNav.Sample(cand, 2f, out c)) continue; }
                else
                {   // no navmesh: accept only open floor reachable in a straight line
                    c = cand;
                    if (Physics.Raycast(pos + Vector3.up * .5f, (c - pos).normalized, r, ~0, QueryTriggerInteraction.Ignore)) continue;
                }
                var from = c + Vector3.up * ChestH;
                if (!Physics.Linecast(from, enemyEye, out var hit, ~0, QueryTriggerInteraction.Ignore)) continue;   // open to enemy
                if (hit.collider.GetComponentInParent<ArenaPlayer>() != null) continue;                             // only a pilot in the way
                float score = Vector3.Distance(pos, c) + .5f * Mathf.Max(0, enemyToSelf - Vector3.Distance(c, enemyEye));
                if (score < bestScore) { bestScore = score; spot = c; found = true; }
            }
            return found;
        }

        bool RunSeek(float now, Vector3 pos, PickupKind kind)
        {
            if (seekSite != null && !seekSite.Available) { seekSite = null; goalSet = false; }
            if (!goalSet)
            {
                seekSite = FindSite(kind, pos);
                if (seekSite == null) return false;
                if (!SetGoal(now, seekSite.Position, .5f, false)) { ignoreSite = seekSite; ignoreUntil = now + 10f; seekSite = null; return false; }
                arrivedAt = -1;
            }
            if (arrived)
            {
                if (arrivedAt < 0) arrivedAt = now;
                else if (now - arrivedAt > 1.5f) { ignoreSite = seekSite; ignoreUntil = now + 12f; seekSite = null; goalSet = false; return false; }
            }
            mode = arrived ? Mode.None : Mode.Path;
            return true;
        }

        IPickupSite FindSite(PickupKind kind, Vector3 pos)
        {
            var sites = PickupRegistry.Sites;
            IPickupSite best = null; float bd = float.MaxValue;
            float now = Time.time;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (s == null || s.Kind != kind || !s.Available) continue;
                if (s == ignoreSite && now < ignoreUntil) continue;
                float d = (s.Position - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        bool RunInvestigate(float now, Vector3 pos)
        {
            if (now - lastSeenTime >= memory) return false;
            if (!goalSet)
            {
                if (!SetGoal(now, lastSeenPos, 1.5f, false)) { lastSeenTime = -99; return false; }
            }
            if (arrived) { lastSeenTime = -99; goalSet = false; return false; }
            mode = Mode.Path;
            return true;
        }

        bool RunRoam(float now, Vector3 pos)
        {
            if (!goalSet || arrived || now > goalExpire)
            {
                if (!PickRoamGoal(now, pos)) { mode = Mode.None; return true; }
            }
            mode = Mode.Path;
            // no navmesh: sidestep walls ahead
            if (!BotNav.Ready && pathIdx < cornerCount)
            {
                var to = corners[pathIdx] - pos; to.y = 0;
                if (to.sqrMagnitude > .01f && Physics.Raycast(pos + Vector3.up * .6f, to.normalized, 1.4f, ~0, QueryTriggerInteraction.Ignore))
                    goalSet = false;
            }
            return true;
        }

        bool PickRoamGoal(float now, Vector3 pos)
        {
            Vector3 best = default; float bestScore = float.MinValue; bool any = false;
            var all = ArenaPlayer.All;
            for (int i = 0; i < 5; i++)
            {
                Vector3 c;
                if (BotNav.Ready) { if (!BotNav.RandomPoint(out c)) continue; }
                else c = new Vector3(Random.Range(-20f, 20f), pos.y, Random.Range(-20f, 20f));
                float dSelf = FlatDist(c, pos);
                float minOther = 25f;
                for (int k = 0; k < all.Count; k++)
                {
                    var p = all[k];
                    if (p == null || p == self || !p.Alive) continue;
                    minOther = Mathf.Min(minOther, FlatDist(c, p.transform.position));
                }
                for (int k = 0; k < Brains.Count; k++)
                {
                    var b = Brains[k];
                    if (b == this || b.self == null || !b.goalSet) continue;
                    minOther = Mathf.Min(minOther, FlatDist(c, b.goal));
                }
                float score = Mathf.Min(dSelf, 25f) * .4f + minOther + Random.value * 2f - (dSelf < 6f ? 10f : 0f);
                if (score > bestScore) { bestScore = score; best = c; any = true; }
            }
            if (!any) return false;
            if (!SetGoal(now, best, 1.0f, true)) return false;
            goalExpire = now + 25f;
            return true;
        }

        static float FlatDist(Vector3 a, Vector3 b) { float x = a.x - b.x, z = a.z - b.z; return Mathf.Sqrt(x * x + z * z); }

        // Plan a path now and cache its corners. Without a NavMesh the "path" is the goal itself.
        bool SetGoal(float now, Vector3 g, float arrive, bool requireComplete)
        {
            goal = g; arriveRadius = arrive; lastPathAt = now; arrived = false;
            cornerCount = 0; pathIdx = 0; goalSet = false;
            if (BotNav.Ready)
            {
                if (!BotNav.CalcPath(self.transform.position, g, navPath, requireComplete)) return false;
                cornerCount = navPath.GetCornersNonAlloc(corners);
                if (cornerCount <= 0) return false;
                pathIdx = cornerCount > 1 ? 1 : 0;
            }
            else { corners[0] = g; cornerCount = 1; }
            suffix[cornerCount] = 0; suffix[cornerCount - 1] = 0;
            for (int i = cornerCount - 2; i >= 0; i--) suffix[i] = suffix[i + 1] + Vector3.Distance(corners[i], corners[i + 1]);
            goalSet = true;
            return true;
        }

        void CheckStuck(float now, Vector3 pos)
        {
            if (now < stuckCheckAt) return;
            float moved = FlatDist(pos, stuckPos);
            if (wantMoveTime > .8f && moved < .6f && now >= nudgeUntil) OnStuck(now);
            stuckPos = pos; stuckCheckAt = now + 1f; wantMoveTime = 0;
        }

        void OnStuck(float now)
        {
            goalSet = false; arrived = false;
            strafeDir = -strafeDir; strafeUntil = now + 1f;
            float a = Random.value * Mathf.PI * 2f;
            nudgeDir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            nudgeUntil = now + .5f;
            if (state == State.SeekShield || state == State.SeekAmmo) { ignoreSite = seekSite; ignoreUntil = now + 8f; }
            if (state == State.Cover) coverBlockUntil = now + 2f;
        }

        // ================= per-frame action =================
        void Act(ref BotCommand cmd, float now, float dt)
        {
            var pos = self.transform.position;
            var eye = pos + Vector3.up * EyeH;

            // ---- desired world movement ----
            Vector3 wd = Vector3.zero; float remain = 0;
            if (now < nudgeUntil) wd = nudgeDir;
            else if (mode == Mode.Path)
            {
                if (FollowPath(pos, out wd))
                {
                    remain = FlatDist(pos, corners[pathIdx]) + suffix[pathIdx];
                    if (blendStrafe && visible && target != null)
                    {
                        var toT = target.transform.position - pos; toT.y = 0;
                        if (toT.sqrMagnitude > .01f) { toT.Normalize(); wd += new Vector3(toT.z, 0, -toT.x) * (strafeDir * strafeMag * .5f); }
                    }
                }
            }
            else if (mode == Mode.Local) wd = FightDir(pos);
            wd += sep * .9f;
            if (wd.sqrMagnitude > 1f) wd.Normalize();

            // ---- aim / facing ----
            bool aiming = visible && target != null && target.Alive;
            float wantYaw = yaw, wantPitch = 0f, rate = Mathf.Min(turnRate, 260f);
            Vector3 chest = default, d = default; float flat = 0, dist = 0;
            if (aiming)
            {
                chest = target.transform.position + Vector3.up * ChestH;
                var aimPt = chest + targetVel * lead;
                d = aimPt - eye;
                flat = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                wantPitch = -Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg;
                rate = turnRate;
            }
            else if (Mathf.Max(heardTime, hitTime) > 0 && now - Mathf.Max(heardTime, hitTime) < 1f)
            {
                var h = (hitTime >= heardTime ? hitPos : heardPos) - eye;
                wantYaw = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
            }
            else if (wd.sqrMagnitude > .01f) wantYaw = Mathf.Atan2(wd.x, wd.z) * Mathf.Rad2Deg;

            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, rate * dt);
            pitch = Mathf.MoveTowards(pitch, wantPitch, rate * .8f * dt);

            // aim wobble: shrinks while we keep tracking, grows with movement
            float tw = 0, tp = 0;
            if (aiming)
            {
                float track = Mathf.Clamp01((now - acquireAt) / 2f);
                float amp = errBase * Mathf.Lerp(1f, errFloor, track);
                amp *= 1f + .05f * (wd.magnitude * 5.4f) + .04f * targetVel.magnitude;
                tw = (Mathf.PerlinNoise(now * 1.3f + seed, .37f) - .5f) * 2f * amp;
                tp = (Mathf.PerlinNoise(now * 1.1f + seed + 50f, .81f) - .5f) * 2f * amp * .7f;
            }
            float k = 1f - Mathf.Exp(-10f * dt);
            wobY = Mathf.Lerp(wobY, tw, k); wobP = Mathf.Lerp(wobP, tp, k);
            cmd.Yaw = yaw + wobY;
            cmd.Pitch = pitch + wobP;

            // ---- weapon / reload ----
            if (wantWeapon >= 0) { cmd.Weapon = wantWeapon; wantWeapon = -1; nextSwitch = now + 1.2f; }
            cmd.Reload = needReload;

            // ---- sprint ----
            bool sprint = false;
            if (wd.sqrMagnitude > .01f && remain > 10f && (state == State.Roam || state == State.Investigate || state == State.SeekShield
                || state == State.SeekAmmo || state == State.Cover) && (!aiming || state == State.Cover) && now >= nudgeUntil)
                sprint = true;
            cmd.Sprint = sprint;

            // ---- fire gating ----
            bool fire = false;
            if (aiming && cmd.Weapon < 0 && !sprint && now >= reactionAt && now - lastVisAt < .3f && !self.Reloading)
            {
                var cur = CurWeapon;
                var def = WeaponDef.Get((int)cur);
                dist = Mathf.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
                float maxFire = cur == WeaponId.Scatter ? def.Range * .75f : Mathf.Min(def.Range * .7f, 60f);
                if (MagOf(cur) > 0 && dist <= maxFire)
                {
                    if (now >= burstSwitchAt)
                    {
                        burstOn = !burstOn;
                        burstSwitchAt = now + (burstOn ? Random.Range(burstOnMin, burstOnMax) : Random.Range(burstOffMin, burstOffMax));
                    }
                    if (burstOn || cur == WeaponId.Scatter)
                    {
                        var c = chest - eye;
                        float cf = Mathf.Sqrt(c.x * c.x + c.z * c.z);
                        float perfYaw = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
                        float perfPitch = -Mathf.Atan2(c.y, cf) * Mathf.Rad2Deg;
                        float ey = Mathf.DeltaAngle(cmd.Yaw, perfYaw), ep = Mathf.DeltaAngle(cmd.Pitch, perfPitch);
                        float err = Mathf.Sqrt(ey * ey + ep * ep);
                        float tol = Mathf.Atan2(.4f, Mathf.Max(dist, 1f)) * Mathf.Rad2Deg + tolFudge;
                        if (cur == WeaponId.Scatter) tol += def.Spread * .4f;
                        fire = err <= tol;
                    }
                }
            }
            cmd.Fire = fire;

            // ---- movement in the final facing frame ----
            float yr = cmd.Yaw * Mathf.Deg2Rad;
            var f = new Vector3(Mathf.Sin(yr), 0, Mathf.Cos(yr));
            var rgt = new Vector3(f.z, 0, -f.x);
            float scale = sprint ? 1f : moveScale;
            cmd.Move = new Vector2(Vector3.Dot(wd, rgt), Vector3.Dot(wd, f)) * scale;
            if (cmd.Move.sqrMagnitude > .09f) wantMoveTime += dt;
        }

        bool FollowPath(Vector3 pos, out Vector3 dir)
        {
            dir = Vector3.zero;
            if (!goalSet || cornerCount == 0) return false;
            while (pathIdx < cornerCount)
            {
                var c = corners[pathIdx];
                float dx = c.x - pos.x, dz = c.z - pos.z, sq = dx * dx + dz * dz;
                float rad = pathIdx == cornerCount - 1 ? arriveRadius : .6f;
                if (sq < rad * rad || sq < .0001f) { pathIdx++; continue; }
                float m = Mathf.Sqrt(sq);
                dir = new Vector3(dx / m, 0, dz / m);
                return true;
            }
            arrived = true;
            return false;
        }
    }
}
