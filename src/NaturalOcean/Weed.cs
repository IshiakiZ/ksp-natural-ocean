using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalOcean
{
    /// <summary>
    /// Seaweed. In clear shallow water the bottom is not bare: grass grows on it in beds, short and close where it is a
    /// metre or two deep, and taller weed further down, to about eleven metres (below that there is too little light in
    /// this water). Where the beds are is a matter of the PLACE, the same every time the game is played: a slow pattern
    /// worked out from where on the planet a point is (Bed), not from where the camera began.
    ///
    /// Near the camera the plants are there: the seabed is taken in squares of sixteen metres laid out by latitude and
    /// longitude, and for each square within reach one mesh is made of all its plants (a square a frame; the game is asked
    /// how deep the bottom is at each). A plant is a handful of ribbons, laid out and swayed by the shader
    /// (tools/shaderpack/oceanweed.glsl): the mesh only says where each plant stands. Further off, and from the air, the beds
    /// are the dark patches in the pale water over sand: the sea's own shader darkens the bottom it sees by the same pattern.
    /// </summary>
    sealed class Weed : IDisposable
    {
        public const int Ribbons = 7, Rows = 5;
        const double Square = 16.0;
        /// <summary>How far from the camera plants are made, and how far before they are let go, m.</summary>
        const double Reach = 58.0, Kept = 96.0;
        const int Across = 24;
        /// <summary>The water must be so deep for anything to grow, and no deeper than so, m.</summary>
        public const float Shallowest = 1.0f, Deepest = 11f;

        sealed class Plot { public GameObject Holder; public Mesh Mesh; public Vector3d Middle; public int Plants; }

        readonly Material material;
        readonly Dictionary<long, Plot> plots = new Dictionary<long, Plot>();
        readonly List<long> gone = new List<long>();
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<int> corners = new List<int>();
        string placedOn = "";
        public int Plants, Plots;                 // how many plants stand, in how many squares (for the report)

        static readonly int idSway = Shader.PropertyToID("_WeedSway"), idClock = Shader.PropertyToID("_WeedClock"), idLight = Shader.PropertyToID("_WeedLight"), idSky = Shader.PropertyToID("_WeedSky"),
            idWater = Shader.PropertyToID("_WeedWater"), idMirrorX = Shader.PropertyToID("_OceanMirrorX");

        public Weed(Shader shader)
        {
            material = new Material(shader) { name = "Natural Ocean: seaweed", renderQueue = 2450 };
        }

        // ---------------------------------------------------------------- where the beds are

        static float Chance(int x, int y, int z)
        {
            unchecked
            {
                uint a = (uint)(x + 1048576) * 1664525u + 1013904223u, b = (uint)(y + 1048576) * 1664525u + 1013904223u, c = (uint)(z + 1048576) * 1664525u + 1013904223u;
                a += b * c; b += c * a; c += a * b;
                a ^= a >> 16; b ^= b >> 16; c ^= c >> 16;
                a += b * c; b += c * a; c += a * b;
                return (a >> 8) / 16777216f;
            }
        }

        /// <summary>A slow pattern in space, 0 to 1: random numbers at the corners of cubes one unit across, smoothed between.</summary>
        static float Slow(double x, double y, double z)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            int ix = (int)fx, iy = (int)fy, iz = (int)fz;
            float a = (float)(x - fx), b = (float)(y - fy), c = (float)(z - fz);
            a = a * a * (3f - 2f * a); b = b * b * (3f - 2f * b); c = c * c * (3f - 2f * c);
            float near = Mathf.Lerp(Mathf.Lerp(Chance(ix, iy, iz), Chance(ix + 1, iy, iz), a), Mathf.Lerp(Chance(ix, iy + 1, iz), Chance(ix + 1, iy + 1, iz), a), b);
            float far = Mathf.Lerp(Mathf.Lerp(Chance(ix, iy, iz + 1), Chance(ix + 1, iy, iz + 1), a), Mathf.Lerp(Chance(ix, iy + 1, iz + 1), Chance(ix + 1, iy + 1, iz + 1), a), b);
            return Mathf.Lerp(near, far, c);
        }

        /// <summary>
        /// How much of a bed there is at a place, 0 to 1: `where` is the place from the planet's middle, in the planet's own
        /// turning frame (m). The same sum is in the sea's shader (ocean.glsl, weedBed): keep the two alike.
        /// </summary>
        public static float Bed(Vector3d where)
        {
            float slow = 0.65f * Slow(where.x / 95.0, where.y / 95.0, where.z / 95.0) + 0.35f * Slow(where.x / 27.0, where.y / 27.0, where.z / 27.0);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((slow - 0.5f) / 0.14f));
        }

        // ---------------------------------------------------------------- the plants

        static long Key(int row, int column) => ((long)row << 32) ^ (uint)column;

        /// <summary>
        /// Once a frame: squares come and go with the camera, at most one made, and the shader told the light and the sway.
        /// `eye`: the camera in the scene; `high`: how far it is above the sea; `sway`: which way the water goes to and fro
        /// (in the scene) and how much; `clock`: the sea's clock, s; `sun`, `sky`: the light at the surface (as light).
        /// </summary>
        public void Step(CelestialBody body, Vector3d eye, float high, bool under, bool marked, Vector3 sway, float much, double clock, Color sun, Color sky, Color water, Material sea)
        {
            if (placedOn != body.bodyName) { Clear(); placedOn = body.bodyName; }
            bool wanted = high < 160f && body.pqsController != null;
            double R = body.Radius;
            double perDegree = R * Math.PI / 180.0, step = Square / perDegree;
            double lat = body.GetLatitude(eye), lon = body.GetLongitude(eye);
            if (wanted && Math.Abs(lat) < 72.0)
            {
                // (the nearest square not yet made, within reach)
                int rowHere = (int)Math.Floor(lat / step);
                long best = 0; double nearest = double.MaxValue; int bestRow = 0, bestColumn = 0;
                int span = (int)Math.Ceiling(Reach / Square) + 1;
                for (int row = rowHere - span; row <= rowHere + span; row++)
                {
                    double wide = step / Math.Cos((row + 0.5) * step * Math.PI / 180.0);
                    int columnHere = (int)Math.Floor(lon / wide);
                    for (int column = columnHere - span; column <= columnHere + span; column++)
                    {
                        long key = Key(row, column);
                        if (plots.ContainsKey(key)) continue;
                        Vector3d middle = body.GetWorldSurfacePosition((row + 0.5) * step, (column + 0.5) * wide, 0.0);
                        double far = (middle - eye).magnitude;
                        if (far < Reach + 12.0 && far < nearest) { nearest = far; best = key; bestRow = row; bestColumn = column; }
                    }
                }
                if (nearest < double.MaxValue) plots[best] = Make(body, bestRow, bestColumn, step);
            }
            // (squares left behind are let go)
            gone.Clear(); Plants = 0; Plots = 0;
            foreach (var pair in plots)
            {
                Plot p = pair.Value;
                Vector3d middle = p.Holder != null ? (Vector3d)p.Holder.transform.position : body.GetWorldSurfacePosition(p.Middle.x, p.Middle.y, 0.0);
                double far = (middle - eye).magnitude;
                if (!wanted || far > Kept) gone.Add(pair.Key);
                else if (p.Holder != null) { Plants += p.Plants; Plots++; }
            }
            foreach (long key in gone) { Drop(plots[key]); plots.Remove(key); }

            material.SetVector(idSway, new Vector4(sway.x, sway.y, sway.z, much));
            material.SetVector(idClock, new Vector4((float)((clock * 0.9) % (2.0 * Math.PI)), (float)((clock * 3.1) % (2.0 * Math.PI)), Ribbons, Rows));
            material.SetVector(idLight, new Vector4(sun.r, sun.g, sun.b, QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1f : 0f));
            material.SetVector(idSky, new Vector4(sky.r, sky.g, sky.b, 45f));
            material.SetVector("_WeedEye", new Vector4((float)eye.x, (float)eye.y, (float)eye.z, (float)(Reach / 1.5)));
            material.SetVector(idWater, new Vector4(water.r, water.g, water.b, under ? 1f : 0f));
            material.SetVector(idMirrorX, sea.GetVector("_OceanMirrorX")); material.SetVector("_OceanMirrorY", sea.GetVector("_OceanMirrorY")); material.SetVector("_OceanMirrorW", sea.GetVector("_OceanMirrorW"));
            Vector3 upHere = (Vector3)FlightGlobals.getUpAxis(body, eye);
            material.SetVector("_WeedUpHere", new Vector4(upHere.x, upHere.y, upHere.z, marked ? 1f : 0f));
        }

        Plot Make(CelestialBody body, int row, int column, double step)
        {
            double R = body.Radius;
            double wide = step / Math.Cos((row + 0.5) * step * Math.PI / 180.0);
            double lat0 = row * step, lon0 = column * wide;
            var plot = new Plot { Middle = new Vector3d((row + 0.5) * step, (column + 0.5) * wide, 0.0) };
            points.Clear(); corners.Clear();
            int each = Ribbons * Rows * 2;
            for (int b = 0; b < Across; b++)
                for (int a = 0; a < Across; a++)
                {
                    int seed = unchecked(row * 73856093 ^ column * 19349663);
                    float u = (a + Chance(seed, a, b)) / Across, v = (b + Chance(seed, b + 977, a)) / Across;
                    double lat = lat0 + v * step, lon = lon0 + u * wide;
                    Vector3d radial = body.GetRelSurfaceNVector(lat, lon);
                    // (first the pattern, which costs nothing; then the depth, which the game must be asked)
                    float bed = Bed(radial * R);
                    if (bed <= 0f || Chance(seed + 3, a, b) > bed) continue;
                    float deep = (float)(R - body.pqsController.GetSurfaceHeight(radial));
                    if (deep < Shallowest || deep > Deepest) continue;
                    // (fewer where it is nearly too shallow or too deep)
                    float fit = Mathf.Clamp01((deep - Shallowest) / 0.8f) * Mathf.Clamp01((Deepest - deep) / 3f);
                    if (Chance(seed + 5, a, b) > fit) continue;
                    // (the place to two centimetres, and under that three numbers of the plant's own: see the shader)
                    float x = Mathf.Floor((float)(u * Square) * 50f) / 50f + Chance(seed + 7, a, b) * 0.0195f;
                    float z = Mathf.Floor((float)(v * Square) * 50f) / 50f + Chance(seed + 9, a, b) * 0.0195f;
                    float y = -(Mathf.Floor(deep * 50f) / 50f + Chance(seed + 11, a, b) * 0.0195f);
                    var foot = new Vector3(x, y, z);
                    int first = points.Count;
                    for (int n = 0; n < each; n++) points.Add(foot);
                    for (int ribbon = 0; ribbon < Ribbons; ribbon++)
                        for (int r = 0; r < Rows - 1; r++)
                        {
                            int at = first + ribbon * Rows * 2 + r * 2;
                            corners.Add(at); corners.Add(at + 2); corners.Add(at + 1);
                            corners.Add(at + 1); corners.Add(at + 2); corners.Add(at + 3);
                        }
                    plot.Plants++;
                }
            if (plot.Plants == 0) return plot;
            plot.Mesh = new Mesh { name = "Natural Ocean: seaweed" };
            plot.Mesh.SetVertices(points);
            plot.Mesh.SetTriangles(corners, 0, false);
            plot.Mesh.bounds = new Bounds(new Vector3((float)Square * 0.5f, -6f, (float)Square * 0.5f), new Vector3((float)Square + 8f, 18f, (float)Square + 8f));
            plot.Holder = new GameObject("Natural Ocean: seaweed") { layer = 15 };
            plot.Holder.AddComponent<MeshFilter>().sharedMesh = plot.Mesh;
            var drawn = plot.Holder.AddComponent<MeshRenderer>();
            drawn.sharedMaterial = material;
            drawn.shadowCastingMode = ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            drawn.lightProbeUsage = LightProbeUsage.Off;
            drawn.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // (the square's own corner at the sea's level, east along its first axis, up along its second, north along its third;
            // then made the terrain's child, to turn and move with the planet as the ground does)
            Vector3d corner = body.GetWorldSurfacePosition(lat0, lon0, 0.0);
            Vector3d up = (corner - body.position).normalized;
            Vector3d north = (body.GetWorldSurfacePosition(lat0 + step, lon0, 0.0) - corner).normalized;
            north = (north - up * Vector3d.Dot(north, up)).normalized;
            plot.Holder.transform.position = (Vector3)corner;
            plot.Holder.transform.rotation = Quaternion.LookRotation((Vector3)north, (Vector3)up);
            plot.Holder.transform.SetParent(body.pqsController.transform, true);
            return plot;
        }

        static void Drop(Plot p)
        {
            if (p.Holder != null) UnityEngine.Object.Destroy(p.Holder);
            if (p.Mesh != null) UnityEngine.Object.Destroy(p.Mesh);
        }

        public void Clear()
        {
            foreach (var pair in plots) Drop(pair.Value);
            plots.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (material != null) UnityEngine.Object.Destroy(material);
        }
    }
}
