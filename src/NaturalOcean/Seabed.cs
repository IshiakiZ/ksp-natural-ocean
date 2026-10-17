using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalOcean
{
    /// <summary>
    /// How high the ground stands round the camera, against the sea's level: under water, that is how deep the sea is,
    /// which is what a wave feels as it comes in (it slows, steepens and breaks when the water is about as deep as it is
    /// high), and what the light in shallow water goes by.
    ///
    /// Four maps, one in each of a picture's four numbers, as the foam is kept (Foam.cs): 128 points each way, 2 m apart
    /// (256 m across), 8 m, 32 m and 128 m (16 km). A place keeps its point for as long as the camera is near (the picture
    /// wraps round). The game is asked how high its terrain is at each point (7 millionths of a second a point on this Mac),
    /// a few hundred points a frame, the nearest first: a map is full a second or two after the camera comes to rest, and
    /// until a point has been asked it has what the next coarser map has there.
    ///
    /// A second picture, laid out the same, has for every point how long a wave would take from there to the shore (seconds):
    /// in shallow water a wave runs at the root of g times the depth, whatever its length, so its crests swing round to lie
    /// along the shore whichever way they came, and the lines of equal time-to-shore ARE the lines its crests lie on. It is
    /// worked out from the first picture by sweeping (Zhao's fast sweeping for the eikonal equation, 2005), one sweep of one
    /// map a frame while anything is still changing. The sea's shader lays its surf along those lines.
    /// </summary>
    sealed class Seabed : IDisposable
    {
        public const int Across = 128, Maps = 4;
        public static readonly float[] Cell = { 2f, 8f, 32f, 128f };
        /// <summary>What a point has until anything is known of it: deep water.</summary>
        public const float Unknown = -1000f;
        readonly float[][] height = new float[Maps][];
        readonly bool[][] asked = new bool[Maps][];
        readonly Queue<int>[] waiting = new Queue<int>[Maps];
        readonly long[] cellX = new long[Maps], cellZ = new long[Maps];
        readonly bool[] begun = new bool[Maps];
        readonly ushort[] raw = new ushort[Across * Across * 4];
        public readonly Texture2D Map;
        bool changed;
        // (how long a wave takes from each point to the shore, s; nothing known yet: Far)
        public const float Far = 5000f;
        readonly float[][] time = new float[Maps][];
        readonly ushort[] rawTime = new ushort[Across * Across * 4];
        public readonly Texture2D Times;
        readonly int[] unsettled = new int[Maps];
        // (How deep the water is at each point taken over some metres round it: a wave does not feel a bump of the bottom
        // a tenth of its own length across, and the game's seabed is all such bumps. The surf goes by this.)
        readonly float[][] smooth = new float[Maps][];
        static readonly int[] Round = { 5, 2, 1, 1 };
        readonly float[] line = new float[Across];
        readonly bool[] rough = new bool[Maps];
        readonly int[] smoothedAt = new int[Maps];
        readonly ushort[] rawSmooth = new ushort[Across * Across * 4];
        public readonly Texture2D Deeps;
        bool smoothChanged;
        int frame;
        int sweep;
        bool timesChanged;
        float gravity = 9.81f;
        string placedOn = "";
        /// <summary>How many points may be asked in a frame.</summary>
        public int Budget = 320;
        public int Waiting { get { int sum = 0; for (int l = 0; l < Maps; l++) sum += waiting[l].Count; return sum; } }

        static readonly int idMap = Shader.PropertyToID("_OceanSeabed"), idAtA = Shader.PropertyToID("_SeabedAtA"), idAtB = Shader.PropertyToID("_SeabedAtB"), idCell = Shader.PropertyToID("_SeabedCell"), idTimes = Shader.PropertyToID("_OceanSurfTime"), idDeeps = Shader.PropertyToID("_OceanSurfDeep");

        public Seabed()
        {
            for (int l = 0; l < Maps; l++)
            {
                height[l] = new float[Across * Across]; asked[l] = new bool[Across * Across]; waiting[l] = new Queue<int>(Across * Across);
                for (int n = 0; n < height[l].Length; n++) height[l][n] = Unknown;
                time[l] = new float[Across * Across];
                smooth[l] = new float[Across * Across];
                for (int n = 0; n < smooth[l].Length; n++) smooth[l][n] = -Unknown;
                for (int n = 0; n < time[l].Length; n++) time[l][n] = Far;
            }
            Times = new Texture2D(Across, Across, TextureFormat.RGBAHalf, false, true) { name = "Natural Ocean: time to the shore", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            ushort far = Cascade.Half(Far);
            for (int n = 0; n < rawTime.Length; n++) rawTime[n] = far;
            Times.SetPixelData(rawTime, 0); Times.Apply(false, false);
            Deeps = new Texture2D(Across, Across, TextureFormat.RGBAHalf, false, true) { name = "Natural Ocean: the depth as a wave feels it", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            ushort deepest = Cascade.Half(-Unknown);
            for (int n = 0; n < rawSmooth.Length; n++) rawSmooth[n] = deepest;
            Deeps.SetPixelData(rawSmooth, 0); Deeps.Apply(false, false);
            Map = new Texture2D(Across, Across, TextureFormat.RGBAHalf, false, true) { name = "Natural Ocean: the seabed", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            ushort deep = Cascade.Half(Unknown);
            for (int n = 0; n < raw.Length; n++) raw[n] = deep;
            Map.SetPixelData(raw, 0); Map.Apply(false, false);
        }

        static int Wrap(long v) => (int)(((v % Across) + Across) % Across);
        static float Part(double where, double side) => (float)((where / side) - Math.Floor(where / side));

        /// <summary>The ground's height against the sea's level at a place on the sea's two axes (m): from the finest map that has it; deep water where none has.</summary>
        public float At(double x, double z)
        {
            for (int l = 0; l < Maps; l++)
            {
                if (!begun[l]) continue;
                double c = Cell[l];
                double u = x / c - 0.5, v = z / c - 0.5;
                long i = (long)Math.Floor(u), j = (long)Math.Floor(v);
                // (inside this map's reach, with a point to spare all round for reading between four of them)
                if (i - cellX[l] < -Across / 2 + 1 || i - cellX[l] >= Across / 2 - 2 || j - cellZ[l] < -Across / 2 + 1 || j - cellZ[l] >= Across / 2 - 2) continue;
                float a = (float)(u - i), b = (float)(v - j);
                float[] h = height[l];
                int i0 = Wrap(i), i1 = Wrap(i + 1), j0 = Wrap(j), j1 = Wrap(j + 1);
                return h[j0 * Across + i0] * (1f - a) * (1f - b) + h[j0 * Across + i1] * a * (1f - b) + h[j1 * Across + i0] * (1f - a) * b + h[j1 * Across + i1] * a * b;
            }
            return Unknown;
        }

        /// <summary>
        /// Once a frame. `at`: where the point of the sea under the camera is on the sea's two axes; `mid` that point in the
        /// scene, `first`, `second` and `up` the axes there. Newly reached points are set to wait, as many as the frame allows
        /// are asked, and the sea's and the foam's materials are told where the maps are.
        /// </summary>
        public void Step(CelestialBody body, Vector2d at, Vector3d mid, Vector3d first, Vector3d second, Vector3d up, Material sea, Material foam)
        {
            if (placedOn != body.bodyName) { placedOn = body.bodyName; for (int l = 0; l < Maps; l++) begun[l] = false; }
            var where = new float[8];
            // (the coarsest first: a finer map's new points take what the next coarser has meanwhile)
            for (int l = Maps - 1; l >= 0; l--)
            {
                double c = Cell[l];
                long cx = (long)Math.Floor(at.x / c), cz = (long)Math.Floor(at.y / c);
                const int Half = Across / 2;
                if (!begun[l] || Math.Abs(cx - cellX[l]) >= Across || Math.Abs(cz - cellZ[l]) >= Across)
                {
                    // (all of it anew, the nearest points first: rings outwards from the camera's own)
                    waiting[l].Clear();
                    cellX[l] = cx; cellZ[l] = cz; begun[l] = true;
                    Reach(l, cx, cz);
                    for (int ring = 1; ring <= Half; ring++)
                        for (int j = -ring; j <= ring; j++)
                        {
                            if (j < -Half || j >= Half) continue;
                            if (j == -ring || j == ring) { for (int i = Math.Max(-ring, -Half); i <= Math.Min(ring, Half - 1); i++) Reach(l, cx + i, cz + j); }
                            else { if (-ring >= -Half) Reach(l, cx - ring, cz + j); if (ring < Half) Reach(l, cx + ring, cz + j); }
                        }
                }
                // (Moved only when the camera has gone more than two of the map's points from where it was last placed. Moved at
                // every point, a camera standing still on a cell's edge (as the sea's point under it does at first: nought,
                // which is an edge of every map) flipped it back and forth with the least wobble of the craft, and each time a
                // strip was asked of the game again and the time to the shore swept again: 1.4 ms a frame on the pad.)
                else if (Math.Abs(cx - cellX[l]) > 2 || Math.Abs(cz - cellZ[l]) > 2)
                {
                    // (the strips that have come into reach)
                    long wasX = cellX[l], wasZ = cellZ[l];
                    cellX[l] = cx; cellZ[l] = cz;
                    for (long j = cz - Half; j < cz + Half; j++)
                        for (long i = cx - Half; i < cx + Half; i++)
                            if (i < wasX - Half || i >= wasX + Half || j < wasZ - Half || j >= wasZ + Half) Reach(l, i, j);
                }
                where[2 * l] = Part(at.x, Across * c); where[2 * l + 1] = Part(at.y, Across * c);
            }

            // ---- as many points asked as the frame allows: the coarsest map first (it gives every finer one something to go on)
            if (body.pqsController != null)
            {
                int left = Budget;
                double R = body.Radius;
                for (int l = Maps - 1; l >= 0 && left > 0; l--)
                {
                    double c = Cell[l];
                    Queue<int> q = waiting[l];
                    while (left > 0 && q.Count > 0)
                    {
                        int n = q.Dequeue();
                        if (asked[l][n]) continue;
                        // (which place this point of the picture holds now: the one nearest the camera's own, the picture wrapping round)
                        int ti = n % Across, tj = n / Across;
                        long i = cellX[l] + (((ti - Wrap(cellX[l])) % Across + Across + Across / 2) % Across) - Across / 2;
                        long j = cellZ[l] + (((tj - Wrap(cellZ[l])) % Across + Across + Across / 2) % Across) - Across / 2;
                        double ox = (i + 0.5) * c - at.x, oz = (j + 0.5) * c - at.y;
                        Vector3d place = mid + first * ox + second * oz;
                        double lat = body.GetLatitude(place), lon = body.GetLongitude(place);
                        height[l][n] = (float)(body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(lat, lon)) - R);
                        asked[l][n] = true;
                        raw[4 * n + l] = Cascade.Half(height[l][n]);
                        // (the ground here may be other than was supposed: times that went by the supposed ground are begun again)
                        time[l][n] = Far;
                        for (int k = l; k >= 0; k--) unsettled[k] = 8;
                        rough[l] = true;
                        changed = true; left--;
                    }
                }
            }
            if (changed) { Map.SetPixelData(raw, 0); Map.Apply(false, false); changed = false; }
            // ---- the depth as a wave feels it, wherever the ground has been asked anew (while a map is still being asked, only now and then)
            frame++;
            for (int l = Maps - 1; l >= 0; l--)
                if (rough[l] && (waiting[l].Count == 0 || frame - smoothedAt[l] > 40)) { Smooth(l); rough[l] = false; smoothedAt[l] = frame; unsettled[l] = 8; break; }
            if (smoothChanged) { Deeps.SetPixelData(rawSmooth, 0); Deeps.Apply(false, false); smoothChanged = false; }
            // ---- the time to the shore: one sweep of one map, the coarsest that is still changing first
            gravity = (float)(body.GeeASL * 9.80665);
            for (int l = Maps - 1; l >= 0; l--)
                if (unsettled[l] > 0) { Sweep(l, sweep++ & 3); break; }
            if (timesChanged) { Times.SetPixelData(rawTime, 0); Times.Apply(false, false); timesChanged = false; }
            var atA = new Vector4(where[0], where[1], where[2], where[3]); var atB = new Vector4(where[4], where[5], where[6], where[7]);
            var cells = new Vector4(Cell[0], Cell[1], Cell[2], Cell[3]);
            sea.SetTexture(idMap, Map); sea.SetTexture(idTimes, Times); sea.SetTexture(idDeeps, Deeps); sea.SetVector(idAtA, atA); sea.SetVector(idAtB, atB); sea.SetVector(idCell, cells);
            if (foam != null) { foam.SetTexture(idMap, Map); foam.SetTexture(idTimes, Times); foam.SetTexture(idDeeps, Deeps); foam.SetVector(idAtA, atA); foam.SetVector(idAtB, atB); foam.SetVector(idCell, cells); }
        }

        /// <summary>A place has come into a map's reach: its point waits to be asked, and has meanwhile what the next coarser map has there.</summary>
        void Reach(int l, long i, long j)
        {
            int n = Wrap(j) * Across + Wrap(i);
            asked[l][n] = false;
            float meanwhile = Unknown;
            if (l + 1 < Maps && begun[l + 1])
            {
                double c = Cell[l], coarse = Cell[l + 1];
                long ci = (long)Math.Floor((i + 0.5) * c / coarse), cj = (long)Math.Floor((j + 0.5) * c / coarse);
                if (Math.Abs(ci - cellX[l + 1]) < Across / 2 - 1 && Math.Abs(cj - cellZ[l + 1]) < Across / 2 - 1) meanwhile = height[l + 1][Wrap(cj) * Across + Wrap(ci)];
            }
            height[l][n] = meanwhile;
            raw[4 * n + l] = Cascade.Half(meanwhile);
            time[l][n] = Far; rawTime[4 * n + l] = Cascade.Half(Far);
            // (and the depth as a wave feels it: what is supposed of the place, until the map is next smoothed. Left as the
            // point's last place had it, which may have been a beach five kilometres off, the sea out here was taken for
            // shallows for a second after the camera came, and the foam kept the white sheets that made for a quarter of a minute.)
            smooth[l][n] = Mathf.Max(-meanwhile, 0f); rawSmooth[4 * n + l] = Cascade.Half(smooth[l][n]);
            changed = true; timesChanged = true; smoothChanged = true;
            unsettled[l] = 8; rough[l] = true;
            waiting[l].Enqueue(n);
        }

        /// <summary>A map's depths taken over `Round` points to every side (first along one axis, then along the other); land counts as no depth.</summary>
        void Smooth(int l)
        {
            const int Half = Across / 2;
            int round = Round[l];
            float[] h = height[l], into = smooth[l];
            long cx = cellX[l], cz = cellZ[l];
            for (int oj = -Half; oj < Half; oj++)
            {
                int row = Wrap(cz + oj) * Across;
                for (int oi = -Half; oi < Half; oi++)
                {
                    float sum = 0f;
                    for (int k = -round; k <= round; k++) sum += Mathf.Max(-h[row + Wrap(cx + Mathf.Clamp(oi + k, -Half, Half - 1))], 0f);
                    line[oi + Half] = sum / (2 * round + 1);
                }
                for (int oi = -Half; oi < Half; oi++) into[row + Wrap(cx + oi)] = line[oi + Half];
            }
            for (int oi = -Half; oi < Half; oi++)
            {
                int col = Wrap(cx + oi);
                for (int oj = -Half; oj < Half; oj++)
                {
                    float sum = 0f;
                    for (int k = -round; k <= round; k++) sum += into[Wrap(cz + Mathf.Clamp(oj + k, -Half, Half - 1)) * Across + col];
                    line[oj + Half] = sum / (2 * round + 1);
                }
                for (int oj = -Half; oj < Half; oj++)
                {
                    int n = Wrap(cz + oj) * Across + col;
                    into[n] = line[oj + Half];
                    rawSmooth[4 * n + l] = Cascade.Half(line[oj + Half]);
                }
            }
            smoothChanged = true;
        }

        /// <summary>How long a wave takes to the shore from a place on the sea's two axes, by map l's reckoning (Far where it does not reach).</summary>
        float TimeAt(int l, double x, double z)
        {
            double c = Cell[l];
            double u = x / c - 0.5, v = z / c - 0.5;
            long i = (long)Math.Floor(u), j = (long)Math.Floor(v);
            if (i - cellX[l] < -Across / 2 + 1 || i - cellX[l] >= Across / 2 - 2 || j - cellZ[l] < -Across / 2 + 1 || j - cellZ[l] >= Across / 2 - 2) return Far;
            float a = (float)(u - i), b = (float)(v - j);
            float[] t = time[l];
            int i0 = Wrap(i), i1 = Wrap(i + 1), j0 = Wrap(j), j1 = Wrap(j + 1);
            return t[j0 * Across + i0] * (1f - a) * (1f - b) + t[j0 * Across + i1] * a * (1f - b) + t[j1 * Across + i0] * (1f - a) * b + t[j1 * Across + i1] * a * b;
        }

        /// <summary>
        /// One sweep of a map, from one of its four corners: each point's time to the shore is made no more than its
        /// neighbours' allow (nought on land; else the least of the two neighbours along each axis, and the time across one
        /// point at the speed the depth there gives). Four sweeps from the four corners carry the times out from every shore;
        /// a map is settled when eight sweeps running have changed nothing.
        /// </summary>
        void Sweep(int l, int corner)
        {
            const int Half = Across / 2;
            float c = Cell[l];
            float[] t = time[l], h = height[l], feel = smooth[l];
            long cx = cellX[l], cz = cellZ[l];
            bool stepX = (corner & 1) == 0, stepZ = (corner & 2) == 0;
            bool any = false;
            for (int b = 0; b < Across; b++)
            {
                int oj = stepZ ? b - Half : Half - 1 - b;
                int row = Wrap(cz + oj) * Across, rowDown = oj > -Half ? Wrap(cz + oj - 1) * Across : -1, rowUp = oj < Half - 1 ? Wrap(cz + oj + 1) * Across : -1;
                for (int a = 0; a < Across; a++)
                {
                    int oi = stepX ? a - Half : Half - 1 - a;
                    int col = Wrap(cx + oi), n = row + col;
                    float was = t[n], now;
                    float ground = h[n];
                    // (Land is where a wave's run ends: but not a lone rock awash in a few metres of water, which a wave passes as
                    // if it were not there. Counted as shore, each drew a ring of surf in round itself. So land counts as shore
                    // only where the water round it is shallow too, as a wave feels it.)
                    if (ground >= 0f && feel[n] < 0.6f) now = 0f;
                    else
                    {
                        float left = oi > -Half ? t[row + Wrap(cx + oi - 1)] : Far, right = oi < Half - 1 ? t[row + Wrap(cx + oi + 1)] : Far;
                        float down = rowDown >= 0 ? t[rowDown + col] : Far, up = rowUp >= 0 ? t[rowUp + col] : Far;
                        float x = Mathf.Min(left, right), z = Mathf.Min(down, up);
                        // (a wave in shallow water runs at the root of g times the depth; in water deeper than 25 m the long waves run as in deep water)
                        float across = c / Mathf.Sqrt(gravity * Mathf.Clamp(Mathf.Max(feel[n], ground >= 0f ? 0.3f : 0f), 0.05f, 25f));
                        float d = Mathf.Abs(x - z);
                        now = d >= across ? Mathf.Min(x, z) + across : 0.5f * (x + z + Mathf.Sqrt(2f * across * across - d * d));
                        // (at a map's rim the shore may lie beyond it: there the next coarser map's time stands, if it has one)
                        if (l + 1 < Maps && (oi < -Half + 2 || oi >= Half - 2 || oj < -Half + 2 || oj >= Half - 2))
                            now = Mathf.Min(now, TimeAt(l + 1, (cx + oi + 0.5) * c, (cz + oj + 0.5) * c));
                        now = Mathf.Min(now, was);
                    }
                    if (now < was - 1e-3f || (now == 0f && was != 0f)) { t[n] = now; rawTime[4 * n + l] = Cascade.Half(now); any = true; }
                }
            }
            if (any) { unsettled[l] = 8; timesChanged = true; if (l > 0) unsettled[l - 1] = Math.Max(unsettled[l - 1], 4); }
            else unsettled[l]--;
        }

#if DEV
        /// <summary>(Development build: the ground and the time to the shore along a line through the camera's point of the sea, by the finest map.)</summary>
        public string Line(double fromX, double fromZ, double stepX, double stepZ, int count)
        {
            var b = new System.Text.StringBuilder();
            for (int n = 0; n < count; n++)
            {
                double x = fromX + n * stepX, z = fromZ + n * stepZ;
                b.Append(At(x, z).ToString("F1")).Append('/').Append(TimeAt(0, x, z).ToString("F1")).Append('/');
                // (the depth as a wave feels it, by each map that reaches)
                for (int l = 0; l < Maps; l++)
                {
                    double c = Cell[l];
                    long i = (long)Math.Floor(x / c), j = (long)Math.Floor(z / c);
                    bool inside = Math.Abs(i - cellX[l]) < Across / 2 - 1 && Math.Abs(j - cellZ[l]) < Across / 2 - 1;
                    b.Append(inside ? smooth[l][Wrap(j) * Across + Wrap(i)].ToString("F1") : "-").Append(l < Maps - 1 ? "," : " ");
                }
            }
            b.Append("(unsettled: ").Append(unsettled[0]).Append(' ').Append(unsettled[1]).Append(' ').Append(unsettled[2]).Append(' ').Append(unsettled[3]).Append(")");
            return b.ToString();
        }
#endif

        public void Dispose() { if (Map != null) UnityEngine.Object.Destroy(Map); if (Times != null) UnityEngine.Object.Destroy(Times); if (Deeps != null) UnityEngine.Object.Destroy(Deeps); }
    }
}
