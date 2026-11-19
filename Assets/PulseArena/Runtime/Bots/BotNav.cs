using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PulseArena
{
    // Runtime NavMesh for the practice bots. Built lazily on the server from the arena's
    // physics colliders (no NavMeshSurface / NavMeshAgent needed) and cached. Rebuilt when the
    // arena root instance goes away or its child count changes. If anything fails, Ready stays
    // false and BotBrain falls back to simple wandering.
    public static class BotNav
    {
        public const string ArenaRootName = "Reactor 07 | Arena";
        const float AgentRadius = .34f, AgentHeight = 1.95f, AgentClimb = .3f, AgentSlope = 40f;

        public static bool Ready { get; private set; }
        /// Walkable area bounds (arena collider bounds). Valid when Ready.
        public static Bounds Bounds { get; private set; }

        static GameObject root;
        static NavMeshData data;
        static NavMeshDataInstance instance;
        static int builtChildCount;
        static float nextTry, nextCheck;
        static int failCount;
        static GameObject failedRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Ready = false; root = null; data = null; instance = default; failCount = 0; nextTry = 0; nextCheck = 0;
        }

        /// Cheap; call every Think. Builds on first use, retries politely on failure.
        public static void Ensure()
        {
            float t = Time.unscaledTime;
            if (Ready)
            {
                if (t < nextCheck) return;
                nextCheck = t + 1f;
                if (root != null && root.activeInHierarchy) return;
                Teardown();   // arena instance destroyed or replaced: re-bake
            }
            if (t < nextTry) return;
            var go = GameObject.Find(ArenaRootName);
            if (go == null) { nextTry = t + 2f; return; }
            if (failedRoot != go) { failedRoot = go; failCount = 0; }
            if (failCount >= 3) { nextTry = t + 10f; failCount = 2; return; }   // keep trying rarely
            nextTry = t + 1.5f;
            bool ok = false;
            try { ok = Build(go); }
            catch (System.Exception e) { Debug.LogWarning("[BotNav] build failed: " + e.Message); }
            if (!ok) { failCount++; Teardown(); }
        }

        static void Teardown()
        {
            if (instance.valid) NavMesh.RemoveNavMeshData(instance);
            instance = default; data = null; Ready = false;
        }

        static bool Build(GameObject arena)
        {
            // Arena bounds from solid, non-trigger, non-pilot colliders.
            var cols = arena.GetComponentsInChildren<Collider>(false);
            bool any = false; Bounds b = default;
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || c.isTrigger || !c.enabled || IsPilot(c)) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any) return false;
            b.Expand(new Vector3(2f, 4f, 2f));

            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(b, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, false, markups, false, sources);
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var comp = sources[i].component;
                if (comp == null) continue;
                var col = comp as Collider;
                if ((col != null && col.isTrigger) || IsPilot(comp)) sources.RemoveAt(i);
            }
            if (sources.Count == 0) return false;

            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = AgentRadius; s.agentHeight = AgentHeight; s.agentClimb = AgentClimb; s.agentSlope = AgentSlope;
            s.overrideVoxelSize = true; s.voxelSize = AgentRadius / 2f;   // coarser = faster bake on phones
            var built = NavMeshBuilder.BuildNavMeshData(s, sources, b, Vector3.zero, Quaternion.identity);
            if (built == null) return false;

            data = built;
            instance = NavMesh.AddNavMeshData(data);
            // Sanity: the mesh must contain something walkable near the arena centre column.
            root = arena; builtChildCount = arena.transform.childCount;
            Bounds = b;
            Ready = instance.valid && ProbeWalkable(b);
            if (!Ready) Debug.LogWarning("[BotNav] baked NavMesh has no walkable area; bots will wander.");
            return Ready;
        }

        static bool IsPilot(Component c)
        {
            return c.GetComponentInParent<ArenaPlayer>() != null || c.GetComponentInParent<CharacterController>() != null;
        }

        static bool ProbeWalkable(Bounds b)
        {
            for (int i = 0; i < 24; i++)
            {
                var p = new Vector3(Random.Range(b.min.x, b.max.x), b.center.y, Random.Range(b.min.z, b.max.z));
                if (NavMesh.SamplePosition(p, out _, Mathf.Max(b.extents.y, 3f), NavMesh.AllAreas)) return true;
            }
            return false;
        }

        // ---- queries (all safe to call when not Ready: they just return false) ----

        public static bool Sample(Vector3 p, float maxDist, out Vector3 result)
        {
            result = p;
            if (!Ready) return false;
            if (NavMesh.SamplePosition(p, out var hit, maxDist, NavMesh.AllAreas)) { result = hit.position; return true; }
            return false;
        }

        public static bool CalcPath(Vector3 from, Vector3 to, NavMeshPath path, bool requireComplete)
        {
            if (!Ready || path == null) return false;
            if (!NavMesh.SamplePosition(from, out var a, 3f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out var c, 3f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(a.position, c.position, NavMesh.AllAreas, path)) return false;
            if (path.status == NavMeshPathStatus.PathInvalid) return false;
            return !requireComplete || path.status == NavMeshPathStatus.PathComplete;
        }

        /// A random point on the NavMesh somewhere in the arena.
        public static bool RandomPoint(out Vector3 result)
        {
            result = default;
            if (!Ready) return false;
            var b = Bounds;
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(Random.Range(b.min.x, b.max.x), b.center.y, Random.Range(b.min.z, b.max.z));
                if (NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) { result = hit.position; return true; }
            }
            return false;
        }
    }
}
