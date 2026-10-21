using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalOcean
{
    /// <summary>
    /// The mesh the sea is drawn on: square rings round the point of the sea under the camera, each ring's cells twice as
    /// large as those of the ring inside it, the innermost a full square (nested grids, as Crest and others lay a sea out).
    /// Each ring is slid along with the camera in steps of two of its own cells, so that a point of the mesh stays at the same
    /// place in the waves and the next ring's points lie on every other one of this ring's. The shader moves the points with
    /// the waves, merges each ring's rim into the next ring, pushes the outermost rim to the horizon and bends the lot over
    /// the planet's curve (tools/shaderpack/ocean.glsl). Each camera draws only the rings that reach into its range, and of
    /// each ring only the run of tiles that the cameras can see.
    /// </summary>
    sealed class Surface : IDisposable
    {
        // (192 cells across a ring since 2026-10-11: at 128 a breaking crest's lip, a metre or two of water, had two or three
        // of the mesh's points to bend over at sixty metres. More than 255 would need wider numbers for the mesh's corners.)
        public const int Cells = 192, Rings = 12;
        // (each ring cut into 8 x 8 tiles of 24 cells, for drawing only what a camera can see)
        const int Tiles = 8, TileCells = Cells / Tiles, Hole = Cells / 4 - 1, Half = Cells / 2;
        const int SeaLayer = 15;
        readonly GameObject root;
        readonly Transform[] ring = new Transform[Rings];
        readonly MeshRenderer[] drawn = new MeshRenderer[Rings];
        readonly MeshFilter[] shape = new MeshFilter[Rings];
        readonly Mesh whole;
        readonly Mesh[] own = new Mesh[Rings];      // rings 1 and out: each its own (hollow) mesh, for its own run of tiles
        readonly Mesh outermost;                    // the outermost ring's (its rim marked to be pushed to the horizon), whichever ring that is
        public readonly Material Material;
        int outer = -1, count;
        bool showing;

        // ---- the tiles, the same in every hollow mesh: their order round the ring and where each is in the indices
        static int[] round;                         // the tiles that have faces, in order round the ring
        static int[] start, length;                 // each tile's faces in the indices (the first time round), by tile
        static int once;                            // indices once round the ring (they are laid twice)

        // ---- what Lay was told, for the culling
        Vector3 mid, first, up, second;
        float cell;
        readonly Vector2[] at = new Vector2[Rings]; // each ring's middle on the sea's axes, from the point under the camera, m
        /// <summary>How far a point of the mesh can be moved from where it lies by the waves, a crest's curl and a blow's dent, m (Sea may set it each frame; the default is ample).</summary>
        public float Reach = 30f;

        // ---- the frame's culling
        int laidFrame = -1, cutFrame = -1;
        bool cutting;
        readonly bool[] wanted = new bool[Rings];
        readonly Plane[] planes = new Plane[6];
        readonly Vector4[][] seaPlanes = { new Vector4[6], new Vector4[6] };
        Camera[] all = new Camera[16];
        readonly bool[] seen = new bool[Tiles * Tiles];
        float pushed = 1f, radius = 600000f;          // (the outermost rim pushed out to the horizon by so many times; the sea's radius, m: as Sea gave the material)
        static readonly int idAxisB = Shader.PropertyToID("_OceanAxisB"), idMid = Shader.PropertyToID("_OceanMid");

        public Surface(Shader shader)
        {
            Material = new Material(shader) { name = "Natural Ocean: the sea", renderQueue = 2501 };
            // (the seaweed reads these from here each frame, the first time before the mirror has set them: Unity writes an
            // error to the log for each one a material was never given)
            Material.SetVector("_OceanMirrorX", Vector4.zero); Material.SetVector("_OceanMirrorY", Vector4.zero); Material.SetVector("_OceanMirrorW", Vector4.zero);
            whole = Make(false, false); outermost = Make(true, true);
            for (int n = 1; n < Rings; n++) own[n] = Make(true, false);
            root = new GameObject("Natural Ocean: the sea");
            for (int n = 0; n < Rings; n++)
            {
                var g = new GameObject("ring " + n) { layer = SeaLayer };
                g.transform.SetParent(root.transform, false);
                shape[n] = g.AddComponent<MeshFilter>();
                shape[n].sharedMesh = n == 0 ? whole : own[n];
                drawn[n] = g.AddComponent<MeshRenderer>();
                drawn[n].sharedMaterial = Material;
                drawn[n].shadowCastingMode = ShadowCastingMode.Off;
                drawn[n].receiveShadows = false;
                drawn[n].lightProbeUsage = LightProbeUsage.Off;
                drawn[n].reflectionProbeUsage = ReflectionProbeUsage.Off;
                drawn[n].enabled = false;
                ring[n] = g.transform;
            }
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
        }

        /// <summary>
        /// A square of Cells by Cells cells with its middle at nought; hollow: without the middle that the ring inside covers
        /// (less one cell all round, so that the two always overlap); rim: its outermost points marked, to be pushed out to
        /// the horizon. A hollow one's faces are laid tile by tile round the ring, twice over, and drawn a run at a time.
        /// </summary>
        static Mesh Make(bool hollow, bool rim)
        {
            int side = Cells + 1;
            var points = new Vector3[side * side];
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                    points[z * side + x] = new Vector3(x - Half, rim && (x == 0 || z == 0 || x == Cells || z == Cells) ? 1f : 0f, z - Half);
            var mesh = new Mesh { name = "Natural Ocean: ring" };
            mesh.vertices = points;
            if (!hollow)
            {
                // (the whole square, as it always was: row by row)
                var faces = new int[Cells * Cells * 6];
                int f = 0;
                for (int z = 0; z < Cells; z++)
                    for (int x = 0; x < Cells; x++) f = Cell(faces, f, x, z, side);
                mesh.triangles = faces;
            }
            else
            {
                if (round == null) Order();
                var faces = new int[once * 2];
                int f = 0;
                for (int pass = 0; pass < 2; pass++)
                    foreach (int t in round)
                    {
                        int tx = t % Tiles, tz = t / Tiles;
                        for (int z = tz * TileCells; z < (tz + 1) * TileCells; z++)
                            for (int x = tx * TileCells; x < (tx + 1) * TileCells; x++)
                                if (!InHole(x, z)) f = Cell(faces, f, x, z, side);
                    }
                mesh.SetIndices(faces, MeshTopology.Triangles, 0, false);
            }
            // (the shader puts the points where they go: nothing here says where the mesh is, so it is never left out of the
            // picture by Unity's own culling; this class does the culling, by tiles)
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
            return mesh;
        }

        static bool InHole(int x, int z) => x - Half >= -Hole && x - Half < Hole && z - Half >= -Hole && z - Half < Hole;

        static int Cell(int[] faces, int f, int x, int z, int side)
        {
            int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
            // (the diagonal turned in a chequer, so that no direction of wave is favoured)
            if (((x + z) & 1) == 0) { faces[f++] = a; faces[f++] = c; faces[f++] = b; faces[f++] = b; faces[f++] = c; faces[f++] = d; }
            else { faces[f++] = a; faces[f++] = c; faces[f++] = d; faces[f++] = a; faces[f++] = d; faces[f++] = b; }
            return f;
        }

        /// <summary>The tiles of a hollow ring in order round it (by the way each tile's middle lies from the ring's), and where each one's faces are.</summary>
        static void Order()
        {
            int tiles = Tiles * Tiles;
            var ways = new float[tiles];
            var order = new int[tiles];
            start = new int[tiles]; length = new int[tiles];
            for (int t = 0; t < tiles; t++)
            {
                order[t] = t;
                ways[t] = Mathf.Atan2((t / Tiles + 0.5f) * TileCells - Half, (t % Tiles + 0.5f) * TileCells - Half);
            }
            Array.Sort(ways, order);
            var kept = new System.Collections.Generic.List<int>(tiles);
            int at = 0;
            foreach (int t in order)
            {
                int tx = t % Tiles, tz = t / Tiles, cells = 0;
                for (int z = tz * TileCells; z < (tz + 1) * TileCells; z++)
                    for (int x = tx * TileCells; x < (tx + 1) * TileCells; x++)
                        if (!InHole(x, z)) cells++;
                start[t] = at; length[t] = cells * 6; at += cells * 6;
                if (cells > 0) kept.Add(t);
            }
            round = kept.ToArray();
            once = at;
        }

        /// <summary>
        /// The rings laid round a point of the sea: `mid` that point in the scene, `first` and `second` the sea's two level
        /// axes there (with `up`), `at` where the point is on those axes (m, as the waves have it), `cell` the innermost
        /// ring's cell (m), `count` how many rings. Returns how far from the point the outermost ring's own rim is.
        /// </summary>
        public float Lay(Vector3 mid, Vector3 first, Vector3 up, Vector3 second, Vector2d at, float cell, int count)
        {
            count = Mathf.Clamp(count, 1, Rings);
            Quaternion turned = Quaternion.LookRotation(second, up);
            for (int n = 0; n < Rings; n++)
            {
                if (n >= count) { drawn[n].enabled = false; wanted[n] = false; continue; }
                double size = cell * Math.Pow(2.0, n), step = 2.0 * size;
                double sx = Math.Round(at.x / step) * step - at.x, sz = Math.Round(at.y / step) * step - at.y;
                ring[n].position = mid + first * (float)sx + second * (float)sz;
                ring[n].rotation = turned;
                ring[n].localScale = new Vector3((float)size, (float)size, (float)size);
                drawn[n].enabled = true; drawn[n].forceRenderingOff = false; wanted[n] = true;
                this.at[n] = new Vector2((float)sx, (float)sz);
            }
            if (outer != count - 1)
            {
                if (outer > 0 && outer < Rings) shape[outer].sharedMesh = own[outer];
                outer = count - 1;
                shape[outer].sharedMesh = outer == 0 ? whole : outermost;
            }
            this.count = count; this.mid = mid; this.first = first; this.up = up; this.second = second; this.cell = cell;
            laidFrame = Time.frameCount; showing = true;
            return cell * Mathf.Pow(2f, count - 1) * Cells * 0.5f;
        }

        // (when a scene is left the game has already destroyed the rings by the time the sea is told: nothing to hide then)
        public void Hide()
        {
            for (int n = 0; n < Rings; n++) { if (drawn[n] != null) drawn[n].enabled = false; wanted[n] = false; }
            showing = false;
        }

        /// <summary>The innermost ring (the full square under the camera), and where it is laid this frame: for drawing it once more by hand (Sea.Lens).</summary>
        public Mesh Inner => whole;
        public Matrix4x4 InnerAt => ring[0].localToWorldMatrix;

        /// <summary>Whether the sea's mesh is being drawn (laid this frame and not hidden; whichever rings a camera draws).</summary>
        public bool Showing => showing && drawn[0] != null;

        // ---- the culling
#if DEV
        /// <summary>(Development build: every ring drawn whole by both cameras, as before 2026-10-18, or only what they see: to compare.)</summary>
        internal static bool DrawWhole;
#endif

        static bool Flight(Camera cam, out int which)
        {
            which = -1;
            FlightCamera fc = FlightCamera.fetch;
            if (fc == null || fc.cameras == null || cam == null) return false;
            for (int i = 0; i < fc.cameras.Length; i++) if (fc.cameras[i] == cam) { which = i; return true; }
            return false;
        }

        void BeforeCamera(Camera cam)
        {
#if DEV
            if (Watching > 0 && Flight(cam, out int w))
            {
                Watching--;
                string rings = "";
                for (int n = 0; n < count; n++) rings += drawn[n] != null && drawn[n].enabled ? (wanted[n] ? "W" : "e") : (wanted[n] ? "w" : ".");
                Watched.Append("frame " + Time.frameCount + " camera " + w + ": laid " + (laidFrame == Time.frameCount) + ", showing " + showing + ", cut " + (cutFrame == Time.frameCount) + "/" + cutting + ", before " + rings);
            }
#endif
            if (laidFrame != Time.frameCount || !showing || !Flight(cam, out _)) { WatchEnd(cam); return; }
            if (cutFrame != Time.frameCount) Cut();
            if (!cutting) { WatchEnd(cam); return; }
            // (Kept from this camera with forceRenderingOff, the renderer itself left on. Switched off and on round each camera, every
            // frame, as until 2026-10-18, a ring lost what Unity keeps of it from frame to frame, and the sea flickered as a rocket
            // climbed: from one frame to the next the whole far sea changed, by 25 levels of 255, in the eight-frame pattern of the
            // antialiasing's jitter ("the ocean still flickers a whole bunch when you're going up", 12:40). Filmed again: none.)
            for (int n = 0; n < count; n++) drawn[n].forceRenderingOff = !(wanted[n] && Reaches(cam, n));
            WatchEnd(cam);
        }

#if DEV
        /// <summary>(Development build: for so many flight cameras' drawings, the rings each drew and why, to find a flicker.)</summary>
        internal static int Watching;
        internal static readonly System.Text.StringBuilder Watched = new System.Text.StringBuilder();
        void WatchEnd(Camera cam)
        {
            if (Watched.Length == 0 || Watched[Watched.Length - 1] == '\n') return;
            string rings = "";
            for (int n = 0; n < count; n++) rings += drawn[n] != null && drawn[n].enabled && !drawn[n].forceRenderingOff ? "D" : ".";
            string runs = "";
            for (int n = 1; n < count; n++) { SubMeshDescriptor d = shape[n].sharedMesh.GetSubMesh(0); runs += " " + (d.indexCount * 100 / Math.Max(1, once)) + "%"; }
            Watched.Append(", drawn " + rings + " (near " + cam.nearClipPlane.ToString("F1") + ", far " + cam.farClipPlane.ToString("F0") + "), runs" + runs + "\n");
        }
#else
        void WatchEnd(Camera cam) { }
#endif

        void AfterCamera(Camera cam)
        {
            if (!cutting || cutFrame != Time.frameCount || !Flight(cam, out _)) return;
            for (int n = 0; n < count; n++) drawn[n].forceRenderingOff = false;
        }

        /// <summary>
        /// Once a frame, at the first flight camera to draw: each ring's run of tiles, those either flight camera can see. Where
        /// another camera that draws the sea's layer is on, nothing is cut (it would see the holes).
        /// </summary>
        void Cut()
        {
            cutFrame = Time.frameCount;
            cutting = false;
            FlightCamera fc = FlightCamera.fetch;
            if (fc == null || fc.cameras == null) { Whole(); return; }
#if DEV
            if (DrawWhole) { Whole(); return; }
#endif
            int cameras = Camera.allCamerasCount;
            if (all.Length < cameras) all = new Camera[cameras * 2];
            cameras = Camera.GetAllCameras(all);
            for (int i = 0; i < cameras; i++)
            {
                Camera c = all[i];
                if (c == null || (c.cullingMask & (1 << SeaLayer)) == 0 || Flight(c, out _)) continue;
                Whole(); return;
            }
            // (the flight cameras' views, in the sea's own axes: x along `first`, y up, z along `second`, from `mid`)
            int views = 0;
            float move = Reach;
            Vessel v = FlightGlobals.ActiveVessel;
            // (the camera may still be carried on to the frame's own moment after this (Smooth Running's beat): room for it)
            if (v != null) move += (float)v.srfSpeed * 0.04f;
            for (int i = 0; i < fc.cameras.Length && views < 2; i++)
            {
                Camera c = fc.cameras[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                GeometryUtility.CalculateFrustumPlanes(c, planes);
                for (int p = 0; p < 6; p++)
                {
                    Vector3 nrm = planes[p].normal;
                    seaPlanes[views][p] = new Vector4(Vector3.Dot(nrm, first), Vector3.Dot(nrm, up), Vector3.Dot(nrm, second), planes[p].distance + Vector3.Dot(nrm, mid));
                }
                views++;
            }
            if (views == 0) { Whole(); return; }
            pushed = Mathf.Max(1f, Material.GetVector(idAxisB).w);
            radius = Mathf.Max(Material.GetVector(idMid).w, 1f);
            float R = radius;
            for (int n = 1; n < count; n++)
            {
                Mesh mesh = shape[n].sharedMesh;
                float size = cell * Mathf.Pow(2f, n), m = move + 2f * size;
                bool rimmed = n == count - 1;
                int any = 0;
                for (int k = 0; k < round.Length; k++)
                {
                    int t = round[k], tx = t % Tiles, tz = t / Tiles;
                    float x0 = (tx * TileCells - Half) * size, x1 = ((tx + 1) * TileCells - Half) * size;
                    float z0 = (tz * TileCells - Half) * size, z1 = ((tz + 1) * TileCells - Half) * size;
                    if (rimmed)
                    {
                        if (tx == 0) x0 *= pushed; if (tx == Tiles - 1) x1 *= pushed;
                        if (tz == 0) z0 *= pushed; if (tz == Tiles - 1) z1 *= pushed;
                    }
                    var lo = new Vector3(at[n].x + x0 - m, -m, at[n].y + z0 - m);
                    var hi = new Vector3(at[n].x + x1 + m, m, at[n].y + z1 + m);
                    float far2 = Mathf.Max(lo.x * lo.x, hi.x * hi.x) + Mathf.Max(lo.z * lo.z, hi.z * hi.z);
                    // (bent down over the planet's curve, so far out)
                    lo.y -= far2 / (R + Mathf.Sqrt(Mathf.Max(R * R - far2, 0f)));
                    bool s = false;
                    for (int i = 0; i < views && !s; i++) s = Inside(seaPlanes[i], lo, hi);
                    seen[k] = s;
                    if (s) any++;
                }
                if (any == 0) { wanted[n] = false; continue; }
                Run(mesh, any);
            }
            cutting = true;
        }

        static bool Inside(Vector4[] p, Vector3 lo, Vector3 hi)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector4 q = p[i];
                float x = q.x >= 0f ? hi.x : lo.x, y = q.y >= 0f ? hi.y : lo.y, z = q.z >= 0f ? hi.z : lo.z;
                if (q.x * x + q.y * y + q.z * z + q.w < 0f) return false;
            }
            return true;
        }

        /// <summary>The ring's one submesh made the run of tiles seen: all but the longest run of tiles not seen, round the ring (the indices are laid twice round, so a run that comes past the end is one run all the same).</summary>
        void Run(Mesh mesh, int any)
        {
            int K = round.Length, first_, run;
            if (any == K) { first_ = 0; run = K; }
            else
            {
                int bestStart = 0, bestLength = 0;
                for (int i = 0; i < K; )
                {
                    if (seen[i]) { i++; continue; }
                    int j = i;
                    while (!seen[j % K] && j - i < K) j++;
                    if (j - i > bestLength) { bestLength = j - i; bestStart = i; }
                    i = j;
                }
                first_ = (bestStart + bestLength) % K; run = K - bestLength;
            }
            int from = start[round[first_]], to = first_ + run <= K ? start[round[first_ + run - 1]] + length[round[first_ + run - 1]]
                                                                    : once + start[round[first_ + run - 1 - K]] + length[round[first_ + run - 1 - K]];
            mesh.SetSubMesh(0, new SubMeshDescriptor(from, to - from), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
        }

        /// <summary>Every ring whole, for every camera, this frame.</summary>
        void Whole()
        {
            for (int n = 1; n < count; n++)
                shape[n].sharedMesh.SetSubMesh(0, new SubMeshDescriptor(0, once), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
        }

        /// <summary>
        /// Whether a ring reaches into a camera's range: its nearest point (with room for the waves) no further than the
        /// camera's far plane over the cosine of its view's corner, and its farthest point no nearer than its near plane.
        /// </summary>
        bool Reaches(Camera cam, int n)
        {
            float size = cell * Mathf.Pow(2f, n), m = Reach + 2f * size;
            Vector3 eye = cam.transform.position - mid;
            float ex = Vector3.Dot(eye, first) - at[n].x, ey = Vector3.Dot(eye, up), ez = Vector3.Dot(eye, second) - at[n].y;
            float most = Mathf.Max(Mathf.Abs(ex), Mathf.Abs(ez));
            float holeEdge = n == 0 ? 0f : Hole * size - most;
            float nearH = Mathf.Max(0f, holeEdge - m), nearV = Mathf.Max(0f, Mathf.Abs(ey) - m);
            float outer_ = Half * size * (n == count - 1 ? pushed : 1f);
            float farX = Mathf.Abs(ex) + outer_ + m, farZ = Mathf.Abs(ez) + outer_ + m;
            float R = radius, far2 = farX * farX + farZ * farZ;
            float farV = Mathf.Abs(ey) + m + far2 / (R + Mathf.Sqrt(Mathf.Max(R * R - far2, 0f)));
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * cam.aspect;
            float corner = Mathf.Sqrt(1f + tanX * tanX + tanY * tanY);
            return nearH * nearH + nearV * nearV <= cam.farClipPlane * cam.farClipPlane * corner * corner && far2 + farV * farV >= cam.nearClipPlane * cam.nearClipPlane;
        }

        public void Dispose()
        {
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            if (root != null) UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(whole); UnityEngine.Object.Destroy(outermost);
            for (int n = 1; n < Rings; n++) if (own[n] != null) UnityEngine.Object.Destroy(own[n]);
            if (Material != null) UnityEngine.Object.Destroy(Material);
        }
    }
}
