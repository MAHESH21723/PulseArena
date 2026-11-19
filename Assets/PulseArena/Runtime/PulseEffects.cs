using UnityEngine;
using UnityEngine.Rendering;

namespace PulseArena
{
    /// Pooled combat effects (muzzle flash, tracers, impacts, eliminations, respawn).
    /// Driven only by PulseEvents (plus ArenaPlayer's effects-only pellet RPC); never touches game state.
    public class PulseEffects : MonoBehaviour
    {
        public static PulseEffects Instance;

        const int TracerCount = 32, FlashCount = 6, PopCount = 10, RingCount = 4, PendingCount = 8, DecalCount = 24, HitFlashCount = 6;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        sealed class Tracer
        {
            public LineRenderer Line;
            public float Start, Life;
            public bool Active;
        }

        sealed class Flash
        {
            public Transform T;
            public Renderer R;
            public Transform Follow;
            public float End;
            public bool Active;
        }

        /// A short scale-pop of a sphere (hit flash, elimination) or a flat rising ring (respawn).
        sealed class Pop
        {
            public Transform T;
            public Renderer R;
            public Vector3 Position;
            public float Start, Life, Size, Rise;
            public bool Ring, Active;
        }

        /// Pooled bullet-hole glow on world surfaces; shrinks away in its last second.
        sealed class Decal
        {
            public Transform T;
            public float Start;
            public bool Active;
        }

        /// White/red flash on a hit pilot's skinned meshes, restored to the original tint afterwards.
        sealed class HitFlash
        {
            public ArenaPlayer Player;
            public SkinnedMeshRenderer[] Renderers;
            public Color[] Original;
            public float Start, Life;
            public Color Tint;
            public bool Active;
        }

        const float DecalLife = 5f;
        Decal[] decals;
        int decalNext;
        HitFlash[] hitFlashes;
        readonly System.Collections.Generic.Dictionary<ArenaPlayer, SkinnedMeshRenderer[]> rendererCache = new System.Collections.Generic.Dictionary<ArenaPlayer, SkinnedMeshRenderer[]>();
        MaterialPropertyBlock block;   // created in Awake: Unity forbids it in field initializers
        ParticleSystem shells;
        float flashSize;

        Transform root;
        Tracer[] tracers;
        Flash[] flashes;
        Pop[] pops, rings;
        int tracerNext, flashNext, popNext, ringNext;
        Light flashLight;
        Transform lightFollow;
        float lightEnd;
        ParticleSystem amberSparks, cyanSparks;
        readonly ArenaPlayer[] pendingPlayer = new ArenaPlayer[PendingCount];
        readonly float[] pendingAt = new float[PendingCount];
        Material amber, cyan;

        void Awake()
        {
            Instance = this;
            block = new MaterialPropertyBlock();
            amber = ArenaArt.Mat("Amber");
            cyan = ArenaArt.Mat("Cyan");
            root = new GameObject("Pulse FX").transform;
            root.SetParent(transform, false);

            tracers = new Tracer[TracerCount];
            for (int i = 0; i < TracerCount; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(root, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                tracers[i] = new Tracer { Line = line };
            }

            flashes = new Flash[FlashCount];
            for (int i = 0; i < FlashCount; i++)
            {
                var quad = Quiet(ArenaArt.Shape("Muzzle flash", PrimitiveType.Quad, root, Vector3.zero, Vector3.one * .3f, "Amber"));
                quad.SetActive(false);
                flashes[i] = new Flash { T = quad.transform, R = quad.GetComponent<Renderer>() };
            }

            pops = new Pop[PopCount];
            for (int i = 0; i < PopCount; i++)
            {
                var sphere = Quiet(ArenaArt.Shape("Burst", PrimitiveType.Sphere, root, Vector3.zero, Vector3.one * .2f, "Cyan"));
                sphere.SetActive(false);
                pops[i] = new Pop { T = sphere.transform, R = sphere.GetComponent<Renderer>() };
            }
            rings = new Pop[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                var ring = Quiet(ArenaArt.Shape("Respawn ring", PrimitiveType.Cylinder, root, Vector3.zero, new Vector3(1, .02f, 1), "Cyan"));
                ring.SetActive(false);
                rings[i] = new Pop { T = ring.transform, R = ring.GetComponent<Renderer>(), Ring = true };
            }

            // One shared point light for all muzzle flashes (mobile: max one extra light).
            var lightGo = new GameObject("Muzzle light");
            lightGo.transform.SetParent(root, false);
            flashLight = lightGo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = 7f;
            flashLight.intensity = 2.5f;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;

            decals = new Decal[DecalCount];
            for (int i = 0; i < DecalCount; i++)
            {
                var quad = Quiet(ArenaArt.Shape("Bullet hole", PrimitiveType.Quad, root, Vector3.zero, Vector3.one * .14f, "Amber"));
                quad.SetActive(false);
                decals[i] = new Decal { T = quad.transform };
            }
            hitFlashes = new HitFlash[HitFlashCount];
            for (int i = 0; i < HitFlashCount; i++) hitFlashes[i] = new HitFlash();

            amberSparks = MakeParticles("Amber sparks", amber);
            cyanSparks = MakeParticles("Cyan sparks", cyan);
            shells = MakeParticles("Shell casings", amber);
            var shellMain = shells.main;
            shellMain.gravityModifier = 2.5f;
            shellMain.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var shellRot = shells.rotationOverLifetime;
            shellRot.enabled = true;
            shellRot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
        }

        static GameObject Quiet(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        ParticleSystem MakeParticles(string name, Material material)
        {
            // Created inactive so it can be configured before it starts playing.
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.gravityModifier = .6f;
            main.startLifetime = .4f;
            main.startSpeed = 0f;
            main.startSize = .08f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            return ps;
        }

        void OnEnable()
        {
            PulseEvents.Shot += OnShot;
            PulseEvents.Damaged += OnDamaged;
            PulseEvents.Eliminated += OnEliminated;
            PulseEvents.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            PulseEvents.Shot -= OnShot;
            PulseEvents.Damaged -= OnDamaged;
            PulseEvents.Eliminated -= OnEliminated;
            PulseEvents.Respawned -= OnRespawned;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------- Event handlers ----------

        void OnShot(ArenaPlayer shooter, WeaponId weapon, Vector3 from, Vector3 to, bool hit)
        {
            Transform follow;
            Vector3 start = StartPoint(shooter, weapon, from, out follow);
            ShowFlash(start, follow, weapon);
            EjectShell(shooter, start, weapon);
            if (weapon != WeaponId.Carbine) return; // pellets are drawn by OnPellets

            AddTracer(start, to, weapon);
            var def = WeaponDef.Get((int)weapon);
            float reach = def.Range - 1f;
            if ((to - from).sqrMagnitude < reach * reach) Impact(to, to - start, hit, 6);
        }

        /// Called by ArenaPlayer for scatter shots: one tracer and impact per pellet.
        public void OnPellets(ArenaPlayer shooter, Vector3 from, Vector3[] ends, int hitMask)
        {
            Transform follow;
            Vector3 start = StartPoint(shooter, WeaponId.Scatter, from, out follow);
            float reach = WeaponDef.Get((int)WeaponId.Scatter).Range - 1f;
            for (int i = 0; i < ends.Length; i++)
            {
                AddTracer(start, ends[i], WeaponId.Scatter);
                if ((ends[i] - from).sqrMagnitude < reach * reach) Impact(ends[i], ends[i] - start, (hitMask & (1 << i)) != 0, 2);
            }
        }

        void OnEliminated(ArenaPlayer victim, ArenaPlayer attacker, WeaponId weapon)
        {
            if (!victim) return;
            Vector3 at = victim.transform.position + Vector3.up * 1.1f;
            bool bot = victim.Bot.Value;
            StartPop(at, bot ? amber : cyan, 2.4f, .5f, 0f, false);
            EmitSparks(amberSparks, at, 18, 6f, .14f, .7f);
            EmitSparks(cyanSparks, at, 14, 5f, .12f, .7f);
        }

        void OnRespawned(ArenaPlayer player)
        {
            // The pilot is teleported just before this event; wait a moment so its position is current on clients.
            for (int i = 0; i < PendingCount; i++)
            {
                if (pendingPlayer[i]) continue;
                pendingPlayer[i] = player;
                pendingAt[i] = Time.time + .2f;
                return;
            }
        }

        // ---------- Effect helpers ----------

        Vector3 StartPoint(ArenaPlayer shooter, WeaponId weapon, Vector3 from, out Transform follow)
        {
            follow = null;
            if (!shooter) return from;
            var app = PulseApp.Instance;
            if (shooter == ArenaPlayer.Local && app)
            {
                var muzzle = app.MuzzleOf(weapon);
                if (muzzle && muzzle.gameObject.activeInHierarchy)
                {
                    follow = muzzle;
                    return muzzle.position;
                }
                return from;
            }
            return shooter.WorldMuzzlePosition;
        }

        void ShowFlash(Vector3 position, Transform follow, WeaponId weapon)
        {
            var f = flashes[flashNext];
            flashNext = (flashNext + 1) % FlashCount;
            bool scatter = weapon == WeaponId.Scatter;
            f.R.sharedMaterial = scatter ? amber : cyan;
            f.T.position = position;
            // First-person muzzles sit ~0.5 m from the camera, so the flash must be small there or it covers the screen.
            flashSize = follow ? (scatter ? .16f : .11f) : (scatter ? .85f : .5f);
            f.T.localScale = Vector3.one * flashSize;
            f.T.localRotation = Random.rotation;
            f.Follow = follow;
            f.End = Time.time + (scatter ? .08f : .06f);
            f.Active = true;
            // First person: the opaque flash quad reads as a flat square next to the gun, so only the light flashes.
            f.T.gameObject.SetActive(!follow);

            flashLight.color = scatter ? ArenaArt.Amber : ArenaArt.Cyan;
            flashLight.intensity = scatter ? 6f : 4f;
            flashLight.range = scatter ? 10f : 8f;
            flashLight.transform.position = position;
            lightFollow = follow;
            lightEnd = f.End;
            flashLight.enabled = true;
        }

        void AddTracer(Vector3 from, Vector3 to, WeaponId weapon)
        {
            var t = tracers[tracerNext];
            tracerNext = (tracerNext + 1) % TracerCount;
            bool scatter = weapon == WeaponId.Scatter;
            var line = t.Line;
            // A trail that ends (or starts) at the first-person camera is drawn as a screen-filling slab:
            // stop it 1.2 m short of the eye. (Own shots start at the gun muzzle, which is in front of the camera.)
            var view = PulseApp.Instance ? PulseApp.Instance.View : null;
            if (view)
            {
                Vector3 eye = view.transform.position;
                const float Keep = 1.2f;
                if ((to - eye).sqrMagnitude < Keep * Keep)
                {
                    Vector3 dir = to - from; float len = dir.magnitude;
                    if (len <= Keep + .2f) return;
                    to = from + dir / len * (len - Keep);
                }
            }
            line.sharedMaterial = scatter ? amber : cyan;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            // Thin at the muzzle (it starts ~0.5 m from the first-person camera), slightly wider far away.
            line.startWidth = scatter ? .006f : .01f;
            line.endWidth = scatter ? .012f : .02f;
            line.widthMultiplier = 1f;
            line.enabled = true;
            t.Start = Time.time;
            t.Life = scatter ? .1f : .09f;
            t.Active = true;
        }

        void Impact(Vector3 point, Vector3 travel, bool pilot, int sparkCount)
        {
            if (pilot)
            {
                // Distinct cyan shield flash on pilots.
                StartPop(point, cyan, .55f, .2f, 0f, false);
                EmitSparks(cyanSparks, point, sparkCount + 2, 3.5f, .07f, .35f);
            }
            else
            {
                EmitSparks(amberSparks, point, sparkCount + 2, 3.5f, .07f, .32f);
                AddDecal(point, travel);
            }
        }

        void AddDecal(Vector3 point, Vector3 travel)
        {
            if (travel.sqrMagnitude < .0001f) return;
            Vector3 dir = travel.normalized;
            if (!Physics.Raycast(point - dir * .15f, dir, out var hit, .4f, ~0, QueryTriggerInteraction.Ignore)) return;
            if (hit.collider.GetComponentInParent<ArenaPlayer>()) return;
            var d = decals[decalNext];
            decalNext = (decalNext + 1) % DecalCount;
            d.T.position = hit.point + hit.normal * .012f;
            d.T.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0, 0, Random.value * 360f);
            d.T.localScale = Vector3.one * Random.Range(.1f, .16f);
            d.Start = Time.time;
            d.Active = true;
            d.T.gameObject.SetActive(true);
        }

        void EjectShell(ArenaPlayer shooter, Vector3 at, WeaponId weapon)
        {
            var app = PulseApp.Instance;
            Transform basis = shooter == ArenaPlayer.Local && app && app.View ? app.View.transform : (shooter ? shooter.transform : null);
            if (!basis) return;
            var emit = new ParticleSystem.EmitParams();
            emit.position = at - basis.forward * .12f;
            emit.startSize = weapon == WeaponId.Scatter ? .05f : .03f;
            emit.startLifetime = .7f;
            emit.velocity = basis.right * Random.Range(1.6f, 2.6f) + Vector3.up * Random.Range(1.4f, 2.4f) - basis.forward * Random.Range(.1f, .5f);
            shells.Emit(emit, 1);
        }

        // ---------- Enemy hit flash ----------

        void OnDamaged(ArenaPlayer victim, ArenaPlayer attacker, int amount, Vector3 from)
        {
            if (!victim || victim == ArenaPlayer.Local) return;
            bool lethal = victim.Health.Value <= 0;
            HitFlash slot = null;
            for (int i = 0; i < HitFlashCount; i++)
            {
                var h = hitFlashes[i];
                if (h.Active && h.Player == victim) { slot = h; break; }
            }
            if (slot == null)
            {
                for (int i = 0; i < HitFlashCount; i++) if (!hitFlashes[i].Active) { slot = hitFlashes[i]; break; }
                if (slot == null) return;
                if (!rendererCache.TryGetValue(victim, out var renderers) || renderers == null)
                    rendererCache[victim] = renderers = victim.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0) return;
                slot.Player = victim;
                slot.Renderers = renderers;
                if (slot.Original == null || slot.Original.Length < renderers.Length) slot.Original = new Color[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    Color c = Color.white;
                    if (renderers[i])
                    {
                        renderers[i].GetPropertyBlock(block);
                        c = block.GetColor(BaseColorId);
                        if (c == Color.clear) c = block.GetColor(ColorId);
                        if (c == Color.clear) c = Color.white;
                    }
                    slot.Original[i] = c;
                }
            }
            slot.Tint = lethal ? new Color(1f, .25f, .2f) : new Color(1f, .85f, .85f);
            slot.Start = Time.time;
            slot.Life = lethal ? .22f : .13f;
            slot.Active = true;
            ApplyHitFlash(slot, 1f);
        }

        void ApplyHitFlash(HitFlash h, float k)
        {
            for (int i = 0; i < h.Renderers.Length; i++)
            {
                var r = h.Renderers[i];
                if (!r) continue;
                Color c = Color.Lerp(h.Original[i], h.Tint, k);
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block);
            }
        }

        void UpdateHitFlashes(float now)
        {
            for (int i = 0; i < HitFlashCount; i++)
            {
                var h = hitFlashes[i];
                if (!h.Active) continue;
                float k = 1f - (now - h.Start) / h.Life;
                if (k <= 0f || !h.Player) { if (h.Player) ApplyHitFlash(h, 0f); h.Active = false; h.Player = null; continue; }
                ApplyHitFlash(h, k * k);
            }
        }

        void StartPop(Vector3 position, Material material, float size, float life, float rise, bool ring)
        {
            // A pop right in front of the first-person camera fills the screen as a flat blob; skip it.
            var view = PulseApp.Instance ? PulseApp.Instance.View : null;
            if (view && (position - view.transform.position).sqrMagnitude < size * size * 4f + 2.25f) return;
            Pop p;
            if (ring)
            {
                p = rings[ringNext];
                ringNext = (ringNext + 1) % RingCount;
            }
            else
            {
                p = pops[popNext];
                popNext = (popNext + 1) % PopCount;
            }
            p.R.sharedMaterial = material;
            p.Position = position;
            p.Size = size;
            p.Life = life;
            p.Rise = rise;
            p.Start = Time.time;
            p.Active = true;
            p.T.position = position;
            p.T.localScale = Vector3.zero;
            p.T.gameObject.SetActive(true);
        }

        static void EmitSparks(ParticleSystem ps, Vector3 position, int count, float speed, float size, float life)
        {
            var emit = new ParticleSystem.EmitParams();
            emit.position = position;
            emit.startSize = size;
            emit.startLifetime = life;
            for (int i = 0; i < count; i++)
            {
                emit.velocity = Random.onUnitSphere * (speed * Random.Range(.4f, 1f));
                ps.Emit(emit, 1);
            }
        }

        // ---------- Per-frame upkeep ----------

        void LateUpdate()
        {
            float now = Time.time;
            var app = PulseApp.Instance;
            Camera cam = app ? app.View : null;

            for (int i = 0; i < TracerCount; i++)
            {
                var t = tracers[i];
                if (!t.Active) continue;
                float k = (now - t.Start) / t.Life;
                if (k >= 1f) { t.Active = false; t.Line.enabled = false; }
                else t.Line.widthMultiplier = 1f - k;
            }

            for (int i = 0; i < FlashCount; i++)
            {
                var f = flashes[i];
                if (!f.Active) continue;
                if (now >= f.End)
                {
                    f.Active = false;
                    f.T.gameObject.SetActive(false);
                    continue;
                }
                if (f.Follow) f.T.position = f.Follow.position;
                f.T.localScale = Vector3.one * (flashSize * Mathf.Lerp(1f, .5f, Mathf.Clamp01(1f - (f.End - now) / .07f)));
                if (cam) f.T.rotation = cam.transform.rotation * Quaternion.Euler(0, 0, (f.T.GetHashCode() & 0x7fff) * 37 % 360);
            }

            if (flashLight.enabled)
            {
                if (now >= lightEnd) flashLight.enabled = false;
                else if (lightFollow) flashLight.transform.position = lightFollow.position;
            }

            UpdateHitFlashes(now);
            for (int i = 0; i < DecalCount; i++)
            {
                var d = decals[i];
                if (!d.Active) continue;
                float age = now - d.Start;
                if (age >= DecalLife) { d.Active = false; d.T.gameObject.SetActive(false); }
                else if (age > DecalLife - 1f) d.T.localScale *= Mathf.Clamp01((DecalLife - age) / (DecalLife - age + Time.deltaTime));
            }
            UpdatePops(pops, now);
            UpdatePops(rings, now);

            for (int i = 0; i < PendingCount; i++)
            {
                if (!pendingPlayer[i] || now < pendingAt[i]) continue;
                var p = pendingPlayer[i];
                pendingPlayer[i] = null;
                if (!p || !p.Alive) continue;
                Vector3 feet = p.transform.position;
                StartPop(feet + Vector3.up * .05f, p.Bot.Value ? amber : cyan, 1.7f, .7f, 1.8f, true);
                EmitSparks(cyanSparks, feet + Vector3.up * .1f, 12, 1.6f, .07f, .8f);
            }
        }

        static void UpdatePops(Pop[] list, float now)
        {
            for (int i = 0; i < list.Length; i++)
            {
                var p = list[i];
                if (!p.Active) continue;
                float k = (now - p.Start) / p.Life;
                if (k >= 1f)
                {
                    p.Active = false;
                    p.T.gameObject.SetActive(false);
                    continue;
                }
                float envelope = Mathf.Sin(Mathf.Sqrt(k) * Mathf.PI);
                if (p.Ring)
                {
                    float radius = Mathf.Lerp(p.Size, p.Size * .35f, k) * envelope;
                    p.T.localScale = new Vector3(radius, .02f, radius);
                    p.T.position = p.Position + Vector3.up * (p.Rise * k);
                }
                else
                {
                    p.T.localScale = Vector3.one * (p.Size * envelope);
                }
            }
        }
    }
}
