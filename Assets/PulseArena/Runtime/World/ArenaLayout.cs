using UnityEngine;

namespace PulseArena
{
    /// The one place that knows where everything in "Reactor 07" goes. Used by the editor
    /// generation step (bakes the arena into the scene) and by the runtime fallback
    /// (ArenaArt.BuildArena when no baked arena exists). Pieces come from prefabs named
    /// "Arena_<piece>" in Resources/World, created by PulseWorldBuilder. Every prefab has its
    /// pivot at bottom-centre (floor tiles: top-centre) and a BoxCollider if solid.
    public static class ArenaLayout
    {
        public const string RootName = "Reactor 07 | Arena";
        public const float Half = 24f;
        public const float WallScaleY = 1.4f;   // 3 m kit walls become 4.2 m perimeter walls

        /// Creates (instantiates) the named piece prefab under parent. May return null if missing.
        public delegate GameObject PieceFactory(string piece, Transform parent);

        /// Every piece key the layout uses; the editor step generates one prefab per key.
        public static readonly string[] PieceKeys =
        {
            "floor", "floor-detail", "floor-panel", "floor-solid",
            "wall", "wall-window", "wall-banner", "wall-pillar", "wall-detail",
            "container", "container-wide", "container-tall", "container-flat", "table-large",
            "stairs-ramp", "computer-wide", "display-wall",
        };

        // Consecutive indices are far apart so early joiners do not spawn next to each other.
        static readonly Vector3[] spawns =
        {
            new Vector3(-21, .1f, -21), new Vector3(21, .1f, 21), new Vector3(-21, .1f, 21), new Vector3(21, .1f, -21),
            new Vector3(0, .1f, -21), new Vector3(0, .1f, 21), new Vector3(-21, .1f, 0), new Vector3(21, .1f, 0),
        };
        static readonly Vector3[] shieldSpots = { new Vector3(-7.5f, .05f, 7.5f), new Vector3(7.5f, .05f, -7.5f) };
        static readonly Vector3[] ammoSpots = { new Vector3(7.5f, .05f, 7.5f), new Vector3(-7.5f, .05f, -7.5f) };

        public static int SpawnCount => spawns.Length;
        public static Vector3 Spawn(int index) => spawns[Mathf.Abs(index) % spawns.Length];
        /// Facing the reactor.
        public static Quaternion SpawnRotation(int index)
        {
            var p = Spawn(index); p.y = 0;
            return p.sqrMagnitude < .01f ? Quaternion.identity : Quaternion.LookRotation(-p.normalized, Vector3.up);
        }
        public static Vector3[] PickupSpots(PickupKind kind) => (Vector3[])(kind == PickupKind.Shield ? shieldSpots : ammoSpots).Clone();

        // Zone colour: quadrant 0 is x>0,z>0; each further quadrant is a 90 degree turn.
        static string Zone(int q) => q % 2 == 0 ? "Cyan" : "Amber";
        static int QuadrantOf(Vector3 p) => p.x >= 0 ? (p.z >= 0 ? 0 : 1) : (p.z >= 0 ? 3 : 2);

        public static void Build(Transform root, PieceFactory make) => new Builder(root, make).Run();

        sealed class Builder
        {
            readonly Transform root;
            readonly PieceFactory make;
            Transform floor, walls, cover, reactor, decor, lights, spawnsGroup, backdrop;
            readonly System.Collections.Generic.HashSet<string> warned = new System.Collections.Generic.HashSet<string>();

            public Builder(Transform r, PieceFactory f) { root = r; make = f; }

            static Transform Group(Transform parent, string name)
            {
                var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
            }
            // Yaw rotation of an x,z pair, the same convention as Quaternion.Euler(0, yaw, 0).
            static Vector2 Rot(float x, float z, float yaw)
            {
                float r = yaw * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
                return new Vector2(x * c + z * s, -x * s + z * c);
            }

            Transform Piece(string key, Transform parent, float x, float z, float yaw, float y = 0, float sx = 1, float sy = 1, float sz = 1)
            {
                var go = make(key, parent);
                if (!go) { if (warned.Add(key)) Debug.LogWarning("Arena piece prefab missing: " + key + " (run Pulse Arena/2. Generate Game)"); return null; }
                var t = go.transform;
                t.localPosition = new Vector3(x, y, z); t.localRotation = Quaternion.Euler(0, yaw, 0); t.localScale = new Vector3(sx, sy, sz);
                return t;
            }
            /// Places a piece given in quadrant-0 coordinates, repeated in all four quadrants.
            void Quad(string key, Transform parent, float x, float z, float yaw, float y = 0, float sx = 1, float sy = 1, float sz = 1, string strip = null)
            {
                for (int q = 0; q < 4; q++)
                {
                    var p = Rot(x, z, q * 90);
                    var t = Piece(key, parent, p.x, p.y, yaw + q * 90, y, sx, sy, sz);
                    if (t && strip != null) TopStrip(t, Zone(q));
                }
            }
            GameObject Glow(string name, Transform parent, Vector3 pos, Vector3 size, float yaw, string mat)
            {
                var go = ArenaArt.Shape(name, PrimitiveType.Cube, parent, pos, size, mat, false);
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                return go;
            }
            void TopStrip(Transform piece, string mat)
            {
                var bc = piece.GetComponent<BoxCollider>();
                if (!bc) return;
                Vector3 top = piece.TransformPoint(bc.center + new Vector3(0, bc.size.y * .5f, 0));
                float len = Mathf.Max(.5f, bc.size.x * piece.localScale.x - .3f);
                var g = Glow("Cover strip", lights, root.InverseTransformPoint(top) + Vector3.up * .012f, new Vector3(len, .03f, .12f), piece.localEulerAngles.y, mat);
                g.transform.localRotation = Quaternion.Euler(0, piece.localEulerAngles.y, 0);
            }
            GameObject Solid(GameObject go, Vector3 center, Vector3 size)
            {
                var b = go.AddComponent<BoxCollider>(); b.center = center; b.size = size; return go;
            }

            public void Run()
            {
                floor = Group(root, "Floor"); walls = Group(root, "Walls"); cover = Group(root, "Cover"); reactor = Group(root, "Reactor");
                decor = Group(root, "Decor"); lights = Group(root, "Lights"); spawnsGroup = Group(root, "Spawns"); backdrop = Group(root, "Backdrop");
                BuildFloor(); BuildWalls(); BuildCover(); BuildReactor(); BuildLights(); BuildSpawns(); BuildBackdrop();
            }

            void BuildFloor()
            {
                // One big collider keeps movement and navmesh simple; the tiles are visual only.
                var fc = new GameObject("Floor collider"); fc.transform.SetParent(floor, false);
                Solid(fc, new Vector3(0, -.5f, 0), new Vector3(Half * 2 + 2, 1, Half * 2 + 2));
                for (int i = 0; i < 16; i++) for (int j = 0; j < 16; j++)
                {
                    float x = -22.5f + i * 3, z = -22.5f + j * 3;
                    string key = Mathf.Abs(x) < 2 && Mathf.Abs(z) < 2 ? "floor-panel" : ((i + j * 3) % 5 == 0 ? "floor-detail" : "floor");
                    Piece(key, floor, x, z, ((i + j) % 4) * 90);
                }
            }

            void BuildWalls()
            {
                const float c = Half + .45f;
                for (int side = 0; side < 4; side++)
                {
                    float yaw = side * 90;
                    for (int i = 0; i < 16; i++)
                    {
                        float x = -22.5f + i * 3;
                        string key = i % 4 == 1 ? "wall-window" : (i % 8 == 6 ? "wall-banner" : "wall");
                        var p = Rot(x, -c, yaw);
                        Piece(key, walls, p.x, p.y, yaw, 0, 1, WallScaleY, 1);
                        if (i % 4 == 3) { var d = Rot(x, -Half, yaw); Piece("wall-detail", decor, d.x, d.y, yaw); }
                    }
                    // Consoles and screens along the inner face, collider-free.
                    foreach (float x in new[] { -15f, -9f, 9f, 15f })
                    {
                        var p = Rot(x, -Half + .35f, yaw);
                        Piece(x > 0 ? "display-wall" : "computer-wide", decor, p.x, p.y, yaw);
                    }
                    var corner = Rot(-c, -c, yaw);
                    Piece("wall-pillar", walls, corner.x, corner.y, yaw + 45, 0, 1, WallScaleY, 1);
                }
            }

            void BuildCover()
            {
                // Quadrant 0 cluster: tall blockers break sightlines, tables and flattened
                // containers are crouch height (about 1.2 m) so you can shoot over them.
                Quad("container-tall", cover, 14, 14, 0);
                Quad("table-large", cover, 10.8f, 14.4f, 0, 0, 1, 1, 1, "strip");
                Quad("table-large", cover, 17, 10.8f, 90, 0, 1, 1, 1, "strip");
                Quad("container-flat", cover, 9.5f, 10, 90, 0, 1, .65f, 1, "strip");
                Quad("container", cover, 19.5f, 17.5f, 0);
                // Edge corridor blockers (stop 40 m sightlines along the walls).
                Quad("container-wide", cover, 22.2f, 12.5f, 0);
                Quad("container-wide", cover, 12.5f, 22.2f, 0);
                // Lane blockers: tall on one side of the lane, crouch cover on the other.
                Quad("container-tall", cover, 3, 16.5f, 0);
                Quad("table-large", cover, -3, 17, 0, 0, 1, 1, 1, "strip");
                // Short flank route: a 0.9 m platform with a stair ramp (rise 0.9 m over 3 m, about 17 degrees).
                // The ramp rises toward its local -Z; yaw 270 turns that toward +X.
                Quad("floor-solid", cover, 21, 5.5f, 0, .9f);
                Quad("floor-solid", cover, 21, 8.5f, 0, .9f);
                Quad("stairs-ramp", cover, 18, 7, 270);
            }

            void BuildReactor()
            {
                var plinth = ArenaArt.Shape("Reactor plinth", PrimitiveType.Cylinder, reactor, new Vector3(0, .12f, 0), new Vector3(6.4f, .12f, 6.4f), "Steel", false);
                var core = ArenaArt.Shape("Core glow", PrimitiveType.Cylinder, reactor, new Vector3(0, 2.75f, 0), new Vector3(2.4f, 2.5f, 2.4f), "Cyan", false);
                Solid(core, Vector3.zero, new Vector3(1, 2, 1));
                ArenaArt.Shape("Core cap", PrimitiveType.Cylinder, reactor, new Vector3(0, 5.35f, 0), new Vector3(3.4f, .18f, 3.4f), "Steel", false);
                ArenaArt.Shape("Core base", PrimitiveType.Cylinder, reactor, new Vector3(0, .5f, 0), new Vector3(3.4f, .26f, 3.4f), "Steel", false);
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8;
                    var m = Glow("Core marker", reactor, new Vector3(Mathf.Cos(a) * 5.1f, .02f, Mathf.Sin(a) * 5.1f), new Vector3(.25f, .02f, 1.3f), -i * 22.5f, "Cyan");
                }
                for (int q = 0; q < 4; q++)
                {
                    var p = Rot(4.2f, 4.2f, q * 90);
                    Piece("wall-pillar", reactor, p.x, p.y, 45 + q * 90, 0, 1, 1, 1);
                }
                Quad("table-large", reactor, 0, 6.8f, 90, 0, 1, 1, 1, "strip");
            }

            void BuildLights()
            {
                // Zone light strips along the two wall halves of each quadrant, and a floor outline round each cluster.
                for (int q = 0; q < 4; q++)
                {
                    string m = Zone(q);
                    foreach (float h in new[] { 2.9f, .35f })
                    {
                        var a = Rot(12, Half - .06f, q * 90); Glow("Wall strip", lights, new Vector3(a.x, h, a.y), new Vector3(23.4f, .14f, .08f), q * 90, m);
                        var b = Rot(Half - .06f, 12, q * 90); Glow("Wall strip", lights, new Vector3(b.x, h, b.y), new Vector3(.08f, .14f, 23.4f), q * 90, m);
                    }
                    foreach (var e in new[] { new Vector3(14, 8, 0), new Vector3(14, 20, 0), new Vector3(8, 14, 90), new Vector3(20, 14, 90) })
                    {
                        var p = Rot(e.x, e.y, q * 90);
                        Glow("Zone outline", lights, new Vector3(p.x, .012f, p.y), new Vector3(12.1f, .024f, .12f), e.z + q * 90, m);
                    }
                }
            }

            void BuildSpawns()
            {
                for (int i = 0; i < spawns.Length; i++)
                {
                    var p = spawns[i]; string m = Zone(QuadrantOf(p));
                    var pad = ArenaArt.Shape("Spawn pad", PrimitiveType.Cylinder, spawnsGroup, new Vector3(p.x, .02f, p.z), new Vector3(3, .012f, 3), m, false);
                    var inner = ArenaArt.Shape("Spawn pad inner", PrimitiveType.Cylinder, spawnsGroup, new Vector3(p.x, .03f, p.z), new Vector3(2.2f, .01f, 2.2f), "Seam", false);
                }
            }

            void BuildBackdrop()
            {
                // Distant silhouettes: cheap, unlit-looking, no colliders.
                var random = new System.Random(19);
                for (int side = 0; side < 4; side++) for (int i = 0; i < 8; i++)
                {
                    float h = 8 + (float)random.NextDouble() * 18;
                    var p = Rot(-40 + i * 11, 36 + (float)random.NextDouble() * 14, side * 90);
                    ArenaArt.Shape("Distant tower", PrimitiveType.Cube, backdrop, new Vector3(p.x, h * .5f, p.y), new Vector3(6, h, 6), "Skyline", false);
                    if (i % 2 == 0)
                    {
                        var f = Rot(0, -3.05f, side * 90);
                        Glow("Skyline band", backdrop, new Vector3(p.x + f.x, h * .62f, p.y + f.y), new Vector3(4.5f, .35f, .1f), side * 90, (i + side) % 2 == 0 ? "Cyan" : "Amber");
                    }
                }
            }
        }
    }
}
