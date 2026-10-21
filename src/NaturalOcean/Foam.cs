using System;
using System.Collections.Generic;
using System.Threading;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalOcean
{
    /// <summary>
    /// The white water. A breaking crest is white for a second or two, and then for many seconds more there is the foam it
    /// left behind, thinning into a lace with growing holes: at sea in a gale that is most of the white there is. So foam
    /// has to be remembered, and remembered as lying ON THE WATER (which swings to and fro under a passing crest and takes
    /// its foam with it), not at a place in space.
    ///
    /// It is kept by the graphics card: one picture of four maps of the sea round the camera, one in each of its four numbers,
    /// each with points four times as far apart as the last (an eighth of a metre, half a metre, two and eight when the
    /// camera is low; four times as much each for a camera above 120 m, 500 m and 2 km). A place on the water keeps its
    /// point of a map for as long as the camera is near (the picture wraps round), so nothing is copied as the camera
    /// moves. Every frame the two finer maps are worked out anew, and one of the two coarser: white at once
    /// where the waves' own numbers say the water is squeezed past breaking, fading everywhere else
    /// (tools/shaderpack/oceanfoam.glsl). The sea's shader reads the map that suits how far off a pixel is, and breaks the
    /// white up by a pattern of lace made here once (see Lace).
    /// </summary>
    sealed class Foam : IDisposable
    {
        public const int Across = 512, Maps = 4;
        readonly Material work;
        /// <summary>The material the foam is worked out with (the seabed's maps are handed to it too).</summary>
        public Material Work => work;
        readonly RenderTexture[] kept = new RenderTexture[2];
        // (the second picture: where breakers' fronts are running. See the shader.)
        readonly RenderTexture[] fronts = new RenderTexture[2];
        // (A small copy of the white, one point in every eight each way, taken each time and read back at once: where the
        // water is breaking this moment, for the spray. See Seen.)
        public const int SeenAcross = 64;
        readonly RenderTexture seen;
        readonly Texture2D seenRead;
        readonly long[] seenX = new long[Maps], seenZ = new long[Maps];
        int seenShiftX, seenShiftZ;
        bool seenTaken;
        readonly CommandBuffer commands = new CommandBuffer { name = "Natural Ocean: the white water" };
        readonly long[] cellX = new long[Maps], cellZ = new long[Maps];
        readonly bool[] begun = new bool[Maps];
        readonly double[] waited = new double[Maps];
        public readonly float[] Texel = new float[Maps];
        int scale = int.MinValue, frame, shownAt;
        static Mesh sheet;
        /// <summary>One triangle that covers the whole of whatever is drawn into (its corners given as the picture has them).</summary>
        public static Mesh Sheet
        {
            get
            {
                if (sheet == null)
                {
                    sheet = new Mesh { name = "Natural Ocean sheet" };
                    sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
                    sheet.triangles = new[] { 0, 1, 2 };
                    sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
                }
                return sheet;
            }
        }
        public Texture2D Lace;
        float laceWay = float.NaN, laceNext, laceTurned;
        /// <summary>How many times longer the lace is along the wind than across it.</summary>
        public const float LaceStretch = 2.3f;
        /// <summary>How much less squeezed than where a breaker begins a crest still carries its front (the steep stretch of the
        /// crest it runs along); and how long a front runs, as a share of its wave's period.</summary>
        public static float Eased = 0.2f, Runs = 0.5f;
        /// <summary>How steeply down the wind the forward face of a crest slopes where the band of froth on it ends (the band is at
        /// the crest and a little way down its face: about a thirtieth of the wave's length deep).</summary>
        public static float Band = 0.05f;
        /// <summary>How uneven the squeeze for breaking is from place to place (what makes a patch of foam's edge ragged).</summary>
        public static float Ragged = 0.14f;
        /// <summary>Over how much squeeze past its level a wave goes from not breaking to breaking outright.</summary>
        public static float Soft = 0.05f;
        /// <summary>(For finding faults: 1 to 4, the maps hold one patch's steep crests in place of the white; 9, where fronts are running. See the shader.)</summary>
        public static float Show;
        /// <summary>How much of the streaks down the wind that are drawn as a pattern (0: none; they look like ropes, and are off until the foam itself is drawn into lines).</summary>
        public static float Streaks = 0f;
        /// <summary>How strongly the wind's rolls gather the foam into rows (1 as worked out from the wind; 0: none).</summary>
        public static float Rows = 1f;
        /// <summary>How fast the last thin foam goes in a whole gale, against 1 in light airs (the long-lived foam of water that has been breaking for hours is what the rows are made of).
        /// 1 since 2026-10-20 (the owner: "way too much white water from looking from above"): at 0.25 the pale foam lay over half to three
        /// quarters of a storm's sea (straight down from 490 m, foam over a fifth white on 24 and 36% of it at 24 and 28 m/s, any foam on 51
        /// and 72%), where real seas have no more than about a tenth white (Callaghan 2008, Brumer 2017); at 1, 11 and 17% (4.5% at 20 m/s).</summary>
        public static float Lasts = 1f;
        readonly System.Random chance = new System.Random(7);
        byte[] laceMade;
        Thread laceMaker;

        static readonly int idPrev = Shader.PropertyToID("_FoamPrev"), idTexel = Shader.PropertyToID("_FoamTexel"), idCellA = Shader.PropertyToID("_FoamCellA"), idCellB = Shader.PropertyToID("_FoamCellB"),
            idMovedA = Shader.PropertyToID("_FoamMovedA"), idMovedB = Shader.PropertyToID("_FoamMovedB"), idSnapA = Shader.PropertyToID("_FoamSnapA"), idSnapB = Shader.PropertyToID("_FoamSnapB"),
            idAtA = Shader.PropertyToID("_FoamAtA"), idAtB = Shader.PropertyToID("_FoamAtB"), idBreaks = Shader.PropertyToID("_FoamBreaks"), idTime = Shader.PropertyToID("_FoamTime"),
            idCount = Shader.PropertyToID("_FoamCount"), idMode = Shader.PropertyToID("_FoamMode"), idMap = Shader.PropertyToID("_OceanFoamMap"), idLace = Shader.PropertyToID("_OceanLace"),
            idRows = Shader.PropertyToID("_FoamRows"), idRowsAt = Shader.PropertyToID("_FoamRowsAt"),
            idFronts = Shader.PropertyToID("_FoamFronts"), idCoarse = Shader.PropertyToID("_FoamCoarse"), idFront = Shader.PropertyToID("_FoamFront"), idPass = Shader.PropertyToID("_FoamPass"),
            idReach = Shader.PropertyToID("_FoamReach"), idLaceAt = Shader.PropertyToID("_OceanLaceAt"), idLaceAtB = Shader.PropertyToID("_OceanLaceAtB"), idLaceTurn = Shader.PropertyToID("_OceanLaceTurn"), idLaceMix = Shader.PropertyToID("_OceanLaceMix"), idRagged = Shader.PropertyToID("_FoamRagged"), idStreak = Shader.PropertyToID("_OceanStreak"), idStreakAt = Shader.PropertyToID("_OceanStreakAt");

        public Foam(Shader shader)
        {
            work = new Material(shader) { name = "Natural Ocean: white water" };
            for (int n = 0; n < 2; n++)
            {
                kept[n] = new RenderTexture(Across, Across, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Natural Ocean: white water " + n, useMipMap = true, autoGenerateMips = false, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
                };
                kept[n].Create();
                fronts[n] = new RenderTexture(Across, Across, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Natural Ocean: breaking fronts " + n, useMipMap = false, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point
                };
                fronts[n].Create();
            }
            seen = new RenderTexture(SeenAcross, SeenAcross, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "Natural Ocean: where it is breaking", useMipMap = false, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            seen.Create();
            seenRead = new Texture2D(SeenAcross, SeenAcross, TextureFormat.RGBA32, false, true) { name = "Natural Ocean: where it is breaking, read" };
            Mesh made = Sheet;
            laceMaker = new Thread(() => { try { laceMade = Lacework.Make(LaceSize); } catch (Exception ex) { Debug.LogError("[Natural Ocean] the foam's lace could not be made: " + ex); } })
            { IsBackground = true, Name = "Natural Ocean: lace", Priority = System.Threading.ThreadPriority.BelowNormal };
            laceMaker.Start();
        }

        public const int LaceSize = 512;
        /// <summary>The two sizes the lace is laid on the sea at, m: its largest holes a sixth of that across.</summary>
        public const float LaceLarge = 5.7f, LaceSmall = 1.3f;

        public RenderTexture Shown => kept[shownAt];

        static float Part(double where, double side) => (float)((where / side) - Math.Floor(where / side));

        public const float StreakWide = 46f, StreakLong = 18f;

        /// <summary>Where the point of the sea under the camera is in the streaks' pattern, laid a given way.</summary>
        static Vector4 StreakAt(Vector2d at, float way)
        {
            double c = Math.Cos(way), s = Math.Sin(way);
            double along = (at.x * c + at.y * s) / (LaceStretch * StreakLong), across = -at.x * s + at.y * c;
            return new Vector4(Part(along, StreakWide), Part(across, StreakWide), 0f, 0f);
        }

        /// <summary>How many whole times the lace laid a given way has come round between where the sea's axes begin and the point
        /// under the camera, at its two sizes, counted to 64 and begun again (the sea's shader reads each square of the lace its
        /// own way, by which square it is: see laceVaried there).</summary>
        static Vector4 LaceCount(Vector2d at, float way)
        {
            double c = Math.Cos(way), s = Math.Sin(way);
            double along = (at.x * c + at.y * s) / LaceStretch, across = -at.x * s + at.y * c;
            return new Vector4(Whole(along, LaceLarge), Whole(across, LaceLarge), Whole(along, LaceSmall), Whole(across, LaceSmall));
        }
        static float Whole(double where, double side) { double n = Math.Floor(where / side); return (float)(n - Math.Floor(n / 64.0) * 64.0); }

        /// <summary>Where the point of the sea under the camera is in the lace laid a given way, at its two sizes.</summary>
        static Vector4 LaceAt(Vector2d at, float way)
        {
            double c = Math.Cos(way), s = Math.Sin(way);
            double along = (at.x * c + at.y * s) / LaceStretch, across = -at.x * s + at.y * c;
            return new Vector4(Part(along, LaceLarge), Part(across, LaceLarge), Part(along, LaceSmall), Part(across, LaceSmall));
        }

        /// <summary>
        /// Once a frame, after the waves have been handed to the graphics card. `at`: where the point of the sea under the
        /// camera is on the sea's two axes (m); `high`: the camera above the sea (m); `seconds`: how far the sea's clock has
        /// moved on; `ready`: how readily crests whiten in this wind (0 none). The sea's material is told where the maps are.
        /// </summary>
        // ---- the marks of what floats (Floating.cs): where a hull churns the water white. Laid by the physics, several times a
        // frame; taken up once a frame, the strongest of them.
        public const int Hulls = 12;
        struct Marked { public Vector2d Place; public float Wide, White; }
        static readonly List<Marked> marks = new List<Marked>();
        static readonly int[] idHull = MakeHullIds();
        static int[] MakeHullIds() { var ids = new int[Hulls]; for (int n = 0; n < Hulls; n++) ids[n] = Shader.PropertyToID("_FoamHull" + n); return ids; }
        public static int MarksLaid;

        /// <summary>White water at a place on the still sea (the sea's two axes, m), so wide (m), so white (0 to 1), from now until it fades.</summary>
        public static void Mark(Vector2d place, float wide, float white)
        {
            // (one part marks the water at every step of the physics: the same place within a frame is one mark, the whitest)
            for (int n = 0; n < marks.Count; n++)
            {
                Marked m = marks[n];
                double dx = m.Place.x - place.x, dz = m.Place.y - place.y;
                if (dx * dx + dz * dz < 0.09 * wide * wide)
                {
                    if (white > m.White) { m.Place = place; m.White = white; m.Wide = Mathf.Max(m.Wide, wide); marks[n] = m; }
                    return;
                }
            }
            if (marks.Count < 256) marks.Add(new Marked { Place = place, Wide = wide, White = white });
        }

#if DEV
        /// <summary>(Development build, for finding what the sea costs: the white water's passes left out, the last picture kept.)</summary>
        public static bool SkipPasses;
        public static string Skip(string on) { SkipPasses = on == "1"; return "the white water's passes " + (SkipPasses ? "left out" : "drawn"); }
#endif
        /// <summary>The sea's two axes begun again at the camera's new place (Sea.cs): every map begun afresh at the next step, the
        /// white water gone with the place it lay on.</summary>
        public void Forget() { for (int l = 0; l < Maps; l++) begun[l] = false; }

        public void Step(Waves waves, Vector2d at, float high, double seconds, float ready, float windTo, float wind, float gravity, Material sea)
        {
#if DEV
            if (SkipPasses) { marks.Clear(); return; }
#endif
            // ---- the way the lace is laid: the wind's, in steps of twenty degrees. It is pinned to the water, so it cannot be
            // turned as the wind veers (far from where the sea's axes begin, the least turn would slide it metres): when the
            // wind has gone fifteen degrees from it, a second lace laid the new way is faded in over a quarter of a minute.
            const float Step20 = 0.349066f;
            if (float.IsNaN(laceWay)) { laceWay = Mathf.Round(windTo / Step20) * Step20; laceNext = laceWay; }
            if (laceTurned <= 0f && Mathf.Abs(Mathf.DeltaAngle(windTo * Mathf.Rad2Deg, laceWay * Mathf.Rad2Deg)) > 15f)
            {
                laceNext = Mathf.Round(windTo / Step20) * Step20;
                if (Mathf.Abs(Mathf.DeltaAngle(laceNext * Mathf.Rad2Deg, laceWay * Mathf.Rad2Deg)) > 1f) laceTurned = 1e-4f;
            }
            if (laceTurned > 0f)
            {
                laceTurned += Time.unscaledDeltaTime / 15f;
                if (laceTurned >= 1f) { laceWay = laceNext; laceTurned = 0f; }
            }

            if (laceMade != null && Lace == null)
            {
                Lace = new Texture2D(LaceSize, LaceSize, TextureFormat.RGBA32, true, true) { name = "Natural Ocean: lace", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                Lace.SetPixelData(laceMade, 0);
                Lace.Apply(true, true);
                laceMade = null;
            }
            // ---- how far apart the finest map's points are: about a pixel under a camera at this height, in steps of four
            float wanted = Mathf.Log(Mathf.Max(high, 1f) / 31.25f, 4f);
            int mode = 0;
            if (scale == int.MinValue) scale = Mathf.Clamp(Mathf.RoundToInt(wanted), 0, 3);
            else if (Mathf.Abs(wanted - scale) > 0.75f)
            {
                int now = Mathf.Clamp(Mathf.RoundToInt(wanted), 0, 3);
                if (now == scale + 1)
                {
                    // (each map becomes the next finer one; the coarsest begins afresh)
                    mode = 1;
                    for (int l = 0; l < Maps - 1; l++) { cellX[l] = cellX[l + 1]; cellZ[l] = cellZ[l + 1]; begun[l] = begun[l + 1]; waited[l] = waited[l + 1]; }
                    begun[Maps - 1] = false;
                }
                else if (now == scale - 1)
                {
                    mode = -1;
                    for (int l = Maps - 1; l > 0; l--) { cellX[l] = cellX[l - 1]; cellZ[l] = cellZ[l - 1]; begun[l] = begun[l - 1]; waited[l] = waited[l - 1]; }
                    begun[0] = false;
                }
                else if (now != scale) for (int l = 0; l < Maps; l++) begun[l] = false;
                scale = now;
            }
            var texel = new Vector4(); var time = new Vector4();
            var cell = new float[8]; var moved = new float[8]; var snap = new float[8]; var where = new float[8];
            for (int l = 0; l < Maps; l++)
            {
                double t = 0.125 * Math.Pow(4.0, scale + l);
                Texel[l] = (float)t; texel[l] = (float)t;
                long cx = (long)Math.Floor(at.x / t), cz = (long)Math.Floor(at.y / t);
                waited[l] += seconds;
                // (the two finer maps every frame, the two coarser ones turn about)
                bool due = !begun[l] || mode != 0 || l < 2 || (frame & 1) == (l & 1);
                if (due)
                {
                    time[l] = (float)Math.Max(waited[l], 1e-5);
                    moved[2 * l] = begun[l] ? Mathf.Clamp(cx - cellX[l], -9999, 9999) : 9999f;
                    moved[2 * l + 1] = begun[l] ? Mathf.Clamp(cz - cellZ[l], -9999, 9999) : 9999f;
                    cellX[l] = cx; cellZ[l] = cz; begun[l] = true; waited[l] = 0.0;
                }
                cell[2 * l] = (float)(((cx % Across) + Across) % Across); cell[2 * l + 1] = (float)(((cz % Across) + Across) % Across);
                snap[2 * l] = (float)(cx * t - at.x); snap[2 * l + 1] = (float)(cz * t - at.y);
                where[2 * l] = Part(at.x, Across * t); where[2 * l + 1] = Part(at.y, Across * t);
            }
            frame++;
            var atA = new Vector4(where[0], where[1], where[2], where[3]); var atB = new Vector4(where[4], where[5], where[6], where[7]);
            RenderTexture from = kept[shownAt], into = kept[1 - shownAt];
            work.SetTexture(idPrev, from);
            work.SetTexture("_OceanSlope", waves.Slope);
            work.SetVector("_OceanLay", new Vector4(1f / Waves.Longest, Waves.Ratio, Waves.Size, Waves.Large));
            work.SetVector("_OceanTwin", new Vector4(Waves.TwinSize, Waves.TwinCos, Waves.TwinSin, Waves.TwinWeight));
            for (int n = 0; n < 5; n++) work.SetVector(Sea.IdAt[n], sea.GetVector(Sea.IdAt[n]));
            work.SetVector(idTexel, texel);
            work.SetVector(idCellA, new Vector4(cell[0], cell[1], cell[2], cell[3])); work.SetVector(idCellB, new Vector4(cell[4], cell[5], cell[6], cell[7]));
            work.SetVector(idMovedA, new Vector4(moved[0], moved[1], moved[2], moved[3])); work.SetVector(idMovedB, new Vector4(moved[4], moved[5], moved[6], moved[7]));
            work.SetVector(idSnapA, new Vector4(snap[0], snap[1], snap[2], snap[3])); work.SetVector(idSnapB, new Vector4(snap[4], snap[5], snap[6], snap[7]));
            work.SetVector(idAtA, atA); work.SetVector(idAtB, atB);
            // (the squeezes at which each size of wave breaks, the second patch to the fifth: Waves.BreakLevel)
            work.SetVector(idBreaks, new Vector4(waves.Levels[1], waves.Levels[2], waves.Levels[3], waves.Levels[4])); work.SetVector(idTime, time);
            work.SetVector(idCoarse, new Vector4(waves.Plain[1], waves.Plain[2], waves.Plain[3], waves.Plain[4]));
            work.SetVector(idCount, new Vector4(0f, Waves.Longer, Soft, 0f));
            work.SetVector(idFront, new Vector4(Eased, Runs, (float)chance.NextDouble(), Mathf.Max(gravity, 0.1f)));
            if (Lace != null) work.SetTexture(idLace, Lace);
            work.SetVector(idMode, new Vector4(mode, ready, Across, 0f));
            work.SetVector(idRagged, new Vector4(Show, Lace != null ? Ragged : 0f, -Mathf.Sin(windTo), Mathf.Cos(windTo)));
            // (The windrows: the rolls the wind sets up in the top of the sea, which gather old foam into streaks: see the
            // shader. From a fresh breeze on, the water running sideways into the rows at up to a hundredth and a bit of the
            // wind's speed. Their pattern is pinned to the water and laid the way the lace is: when the wind has veered and
            // the lace is laid afresh, so are they, and the streaks form again along the new wind in a quarter of a minute.)
            float rows = Mathf.Clamp01((wind - 7f) / 15f) * 0.018f * wind * Rows;
            double rc = Math.Cos(laceWay), rs = Math.Sin(laceWay);
            double rAlong = at.x * rc + at.y * rs, rAcross = -at.x * rs + at.y * rc;
            work.SetVector(idRows, new Vector4(rows, (float)rc, (float)rs, Mathf.Lerp(1f, Lasts, Mathf.Clamp01((wind - 12f) / 12f))));
            work.SetVector(idRowsAt, new Vector4(Part(rAlong, 1024.0 / 7.0), Part(rAcross, 12.8), Part(rAlong, 512.0) + 0.37f, Part(rAcross, 51.2) + 0.37f));
            // (what floats: the dozen whitest of its marks since the last time, each as far from the point of the sea under the camera)
            marks.Sort((x, y) => y.White.CompareTo(x.White));
            for (int n = 0; n < Hulls; n++)
                work.SetVector(idHull[n], n < marks.Count ? new Vector4((float)(marks[n].Place.x - at.x), (float)(marks[n].Place.y - at.y), marks[n].Wide, marks[n].White) : Vector4.zero);
            MarksLaid = marks.Count;
            marks.Clear();
            // (first the fronts, from the fronts as they were; then the white, from the fronts as they now are)
            RenderTexture frontsFrom = fronts[shownAt], frontsInto = fronts[1 - shownAt];
            work.SetVector(idPass, new Vector4(0f, Band, 0f, 0f));
            work.SetTexture(idFronts, frontsFrom);
            commands.Clear();
            commands.SetRenderTarget(frontsInto);
            commands.DrawMesh(sheet, Matrix4x4.identity, work, 0, 0);
            Graphics.ExecuteCommandBuffer(commands);
            work.SetVector(idPass, new Vector4(1f, Band, 0f, 0f));
            work.SetTexture(idFronts, frontsInto);
            commands.Clear();
            commands.SetRenderTarget(into);
            commands.DrawMesh(sheet, Matrix4x4.identity, work, 0, 0);
            Graphics.ExecuteCommandBuffer(commands);
            into.GenerateMips();
            shownAt = 1 - shownAt;
            // (The small copy: one point of each eight by eight, a different one each time, and exactly that point: the
            // picture's smaller copies, which a copying down to an eighth would otherwise read, hold the mean of the sixty-four,
            // and a thin bright front is lost in it.)
            int step = Across / SeenAcross;
            seenShiftX = chance.Next(step); seenShiftZ = chance.Next(step);
            float bias = into.mipMapBias;
            into.mipMapBias = -8f;
            Graphics.Blit(into, seen, Vector2.one, new Vector2((seenShiftX + 0.5f - step * 0.5f) / Across, (seenShiftZ + 0.5f - step * 0.5f) / Across));
            into.mipMapBias = bias;
            for (int l = 0; l < Maps; l++) { seenX[l] = cellX[l]; seenZ[l] = cellZ[l]; }
            seenTaken = true;

            sea.SetTexture(idMap, into);
            if (Lace != null) sea.SetTexture(idLace, Lace);
            sea.SetVector(idAtA, atA); sea.SetVector(idAtB, atB);
            sea.SetVector(idTexel, texel);
            // (how far from the camera each map can be trusted: not out to where it wraps round)
            sea.SetVector(idReach, new Vector4(Lace != null ? 1f : 0f, Across, 1f / LaceLarge, 1f / LaceSmall));
            sea.SetVector(idLaceAt, LaceAt(at, laceWay));
            sea.SetVector(idLaceAtB, LaceAt(at, laceNext));
            sea.SetVector("_OceanLaceCount", LaceCount(at, laceWay));
            sea.SetVector("_OceanLaceCountB", LaceCount(at, laceNext));
            sea.SetVector(idLaceTurn, new Vector4(Mathf.Cos(laceWay), Mathf.Sin(laceWay), Mathf.Cos(laceNext), Mathf.Sin(laceNext)));
            sea.SetVector(idLaceMix, new Vector4(Mathf.SmoothStep(0f, 1f, laceTurned), LaceStretch, 0f, 0f));
            // (the streaks down the wind: none below 13 m/s, a seventh of the sea in a storm; their pattern 46 m wide and
            // eighteen times as long again as the lace is)
            sea.SetVector(idStreak, new Vector4(Mathf.Clamp01((wind - 13f) / 17f) * 0.14f * Streaks, 1f / StreakWide, 1f / StreakLong, 0f));
            Vector4 a = StreakAt(at, laceWay), b = StreakAt(at, laceNext);
            sea.SetVector(idStreakAt, new Vector4(a.x, a.y, b.x, b.y));
        }

        /// <summary>
        /// The small copy of the white as just worked out, read back from the graphics card: `SeenAcross` points each way,
        /// four numbers a point (one for each map, 0 to 255). Read here, before the frame's drawing has begun, it costs next
        /// to nothing (in the middle of a frame the same reading waits for the graphics card to finish everything). Null
        /// until there is one.
        /// </summary>
        public Unity.Collections.NativeArray<Color32> Seen(out bool there)
        {
            there = seenTaken;
            if (!seenTaken) return default(Unity.Collections.NativeArray<Color32>);
            // (Reading a picture back from the graphics card at once makes the game wait until the card has done everything it
            // was given: measured, 2 to 6 ms of every frame, the dearest thing the sea did, and 6 to 12 ms of the main thread every
            // fourth frame on the Mac. So it goes through Keystone's Readback: asked for, and taken some frames later, where the
            // game can (Unity's own asynchronous read on Direct3D and Metal; on OpenGL, as on a Mac, Volumetric Explosions'
            // library when it is installed), the last answer used meanwhile; where it cannot, read at once every fourth frame, as
            // before. Either way the spray is placed by where the water was breaking a few frames before, and each answer carries
            // where the picture's points stood when it was asked for: see SeenWhere.)
            if (seenReader == null) seenReader = new Readback(SeenAcross, SeenAcross, false);
            Taken();
            if (askedAt != Time.frameCount && (seenReader.Later || !seenHad || ++seenSkipped >= 4)
#if DEV
                && (!SkipSeen || !seenHad)
#endif
                )
            {
                askedAt = Time.frameCount;
                seenSkipped = 0;
                SeenAt at = asks[askNext]; askNext = (askNext + 1) % asks.Length;
                at.ShiftX = seenShiftX; at.ShiftZ = seenShiftZ; Array.Copy(seenX, at.X, Maps); Array.Copy(seenZ, at.Z, Maps);
                seenReader.Ask(seen, 0, 0, SeenAcross, SeenAcross, at);
                if (!seenReader.Later) Taken();
            }
            there = seenHad;
            return seenHad ? seenRead.GetRawTextureData<Color32>() : default(Unity.Collections.NativeArray<Color32>);
        }

        /// <summary>Where the picture's points stood when it was asked for (kept round, as many as can be in flight).</summary>
        sealed class SeenAt { public int ShiftX, ShiftZ; public readonly long[] X = new long[Maps], Z = new long[Maps]; }
        readonly SeenAt[] asks = { new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(),
                                   new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt(), new SeenAt() };
        int askNext, askedAt = -1;
        Readback seenReader;

        /// <summary>An answer that has come back, taken in.</summary>
        void Taken()
        {
            if (!seenReader.Take(out object tag) || !(tag is SeenAt at)) return;
            seenRead.LoadRawTextureData(seenReader.Bytes);          // (not sent back to the card: it is only read here)
            readShiftX = at.ShiftX; readShiftZ = at.ShiftZ;
            Array.Copy(at.X, readX, Maps); Array.Copy(at.Z, readZ, Maps);
            seenHad = true;
        }
        bool seenHad;
#if DEV
        /// <summary>(For the development build: the white water's read-back skipped after the first: to measure what it costs.)</summary>
        internal static bool SkipSeen;
#endif
        int seenSkipped, readShiftX, readShiftZ;
        readonly long[] readX = new long[Maps], readZ = new long[Maps];

        /// <summary>Where on the sea's two axes (m) the point (i, j) of the small copy lies in map `l`, and how far apart its points are there (m).</summary>
        public float SeenWhere(int l, int i, int j, out double x, out double z)
        {
            // (by where the picture's points stood when the reading in hand was made, which may be some frames back: see Seen)
            int step = Across / SeenAcross;
            double t = Texel[l];
            long px = i * step + readShiftX, pz = j * step + readShiftZ;
            long cx = ((readX[l] % Across) + Across) % Across, cz = ((readZ[l] % Across) + Across) % Across;
            long fromX = ((px - cx + Across / 2) % Across + Across) % Across - Across / 2, fromZ = ((pz - cz + Across / 2) % Across + Across) % Across - Across / 2;
            x = (readX[l] + fromX + 0.5) * t; z = (readZ[l] + fromZ + 0.5) * t;
            return (float)(t * step);
        }

#if DEV
        /// <summary>(Development build: how much of each map is white just now: solid, thin, any.)</summary>
        public string Measure()
        {
            var read = new Texture2D(Across, Across, TextureFormat.RGBAFloat, false, true);
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = Shown;
            read.ReadPixels(new Rect(0, 0, Across, Across), 0, 0, false);
            RenderTexture.active = before;
            Color[] px = read.GetPixels();
            UnityEngine.Object.Destroy(read);
            var b = new System.Text.StringBuilder("Foam (scale " + scale + "):");
            for (int l = 0; l < Maps; l++)
            {
                int thick = 0, thin = 0, any = 0; double sum = 0.0;
                int now = 0;
                for (int n = 0; n < px.Length; n++) { float v = px[n][l]; if (v > 0.95f) now++; if (v > 0.6f) thick++; if (v > 0.2f) thin++; if (v > 0.01f) any++; sum += v; }
                b.Append("\n  map ").Append(l).Append(" (").Append(Texel[l].ToString("F3")).Append(" m): ").Append((100.0 * now / px.Length).ToString("F3")).Append("% breaking now, ").Append((100.0 * thick / px.Length).ToString("F2")).Append("% thick, ")
                 .Append((100.0 * thin / px.Length).ToString("F2")).Append("% over a fifth, ").Append((100.0 * any / px.Length).ToString("F2")).Append("% any; mean ").Append((sum / px.Length).ToString("F4"));
            }
            return b.ToString();
        }
#endif

#if DEV
        /// <summary>(Development build: the four maps as they are, side by side in one picture, the finest on the left, the
        /// camera's own point in the middle of each: the white above, grey; below it the running fronts (red: how much of a
        /// front's time is left; green: the size of wave, brightest the longest).)</summary>
        public string Dump(string path)
        {
            var picture = new Texture2D(Across * Maps, Across * 2, TextureFormat.RGB24, false);
            var all = new Color32[Across * Maps * Across * 2];
            for (int row = 0; row < 2; row++)
            {
                var read = new Texture2D(Across, Across, TextureFormat.RGBAFloat, false, true);
                RenderTexture before = RenderTexture.active;
                RenderTexture.active = row == 0 ? fronts[shownAt] : Shown;
                read.ReadPixels(new Rect(0, 0, Across, Across), 0, 0, false);
                RenderTexture.active = before;
                Color[] px = read.GetPixels();
                UnityEngine.Object.Destroy(read);
                for (int l = 0; l < Maps; l++)
                {
                    long cx = cellX[l], cz = cellZ[l];
                    int ox = (int)(((cx % Across) + Across) % Across), oz = (int)(((cz % Across) + Across) % Across);
                    for (int z = 0; z < Across; z++)
                        for (int x = 0; x < Across; x++)
                        {
                            // (the picture wraps: the camera's own point is put in the middle)
                            int sx = ((x - Across / 2 + ox) % Across + Across) % Across, sz = ((z - Across / 2 + oz) % Across + Across) % Across;
                            float v = px[sz * Across + sx][l];
                            Color32 c;
                            if (row == 1) { byte g = (byte)Mathf.Clamp(v * 255f, 0f, 255f); c = new Color32(g, g, g, 255); }
                            else c = v >= 1f ? new Color32((byte)(60f + 195f * (v - Mathf.Floor(v))), (byte)(60f * Mathf.Floor(v)), 40, 255) : new Color32(0, 0, 0, 255);
                            all[(row * Across + z) * Across * Maps + l * Across + x] = c;
                        }
                }
            }
            picture.SetPixels32(all);
            picture.Apply();
            System.IO.File.WriteAllBytes(path, picture.EncodeToPNG());
            UnityEngine.Object.Destroy(picture);
            return path;
        }
#endif

        public void Dispose()
        {
            for (int n = 0; n < 2; n++) if (kept[n] != null) { kept[n].Release(); UnityEngine.Object.Destroy(kept[n]); }
            for (int n = 0; n < 2; n++) if (fronts[n] != null) { fronts[n].Release(); UnityEngine.Object.Destroy(fronts[n]); }
            if (seen != null) { seen.Release(); UnityEngine.Object.Destroy(seen); }
            if (seenRead != null) UnityEngine.Object.Destroy(seenRead);
            if (seenReader != null) { seenReader.Free(); seenReader = null; }
            if (work != null) UnityEngine.Object.Destroy(work);
            if (Lace != null) UnityEngine.Object.Destroy(Lace);
            commands.Release();
        }
    }

    /// <summary>
    /// The pattern foam breaks up by. Fresh foam is a solid white froth; as its bubbles burst it opens into holes that grow
    /// until only a net of thin lines is left between them, and then that goes. So the sea's shader needs, for every point of
    /// a patch of foam, WHEN it goes: this is a picture of that order, made once. Its first number: 1 on the lines of the net
    /// (the last to go), 0 in the middle of the largest holes (the first). The net is at two sizes at once, large holes with
    /// a finer net between them (Worley's cells, 1996: a point is as far into a hole as its nearest scattered dot is nearer
    /// than its second nearest). The numbers are spread evenly from 0 to 1, so that foam asked to be so much there covers
    /// that share of the patch. The second number: the same from other dots; the third: the grain of the bubbles; the
    /// fourth: slow blotches. The picture repeats without a seam.
    /// </summary>
    static class Lacework
    {
        static float[] Cells(int size, int grid, int seed)
        {
            var random = new System.Random(seed);
            var dotX = new float[grid * grid]; var dotY = new float[grid * grid];
            for (int n = 0; n < dotX.Length; n++) { dotX[n] = (float)random.NextDouble(); dotY[n] = (float)random.NextDouble(); }
            var into = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + 0.5f) / size * grid, py = (y + 0.5f) / size * grid;
                    int cx = (int)px, cy = (int)py;
                    float first = 1e9f, second = 1e9f;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int gx = cx + i, gy = cy + j;
                            int at = ((gy % grid + grid) % grid) * grid + (gx % grid + grid) % grid;
                            float dx = gx + dotX[at] - px, dy = gy + dotY[at] - py;
                            float d = dx * dx + dy * dy;
                            if (d < first) { second = first; first = d; } else if (d < second) second = d;
                        }
                    // (0 on the line between two dots' cells, up to about 1 at a dot)
                    into[y * size + x] = Mathf.Sqrt(second) - Mathf.Sqrt(first);
                }
            return into;
        }

        /// <summary>Numbers spread evenly from 0 to 1 by their order.</summary>
        static void Even(float[] values)
        {
            int n = values.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var keys = (float[])values.Clone();
            Array.Sort(keys, order);
            for (int i = 0; i < n; i++) values[order[i]] = i / (float)(n - 1);
        }

        static float[] Net(int size, int large, int small, int seed)
        {
            float[] a = Cells(size, large, seed), b = Cells(size, small, seed + 101);
            var into = new float[size * size];
            // (nearest the large net's lines and the small net's lines both: the last to go; the middles of the large holes the first, small net and all)
            for (int n = 0; n < into.Length; n++) into[n] = -(0.62f * Mathf.Min(a[n] * 1.25f, 1f) + 0.38f * Mathf.Min(b[n] * 1.25f, 1f));
            Even(into);
            return into;
        }

        static float[] Blotches(int size, int seed)
        {
            var into = new float[size * size];
            float weight = 1f;
            for (int grid = 4; grid <= 32; grid *= 2)
            {
                var random = new System.Random(seed + grid);
                var v = new float[grid * grid];
                for (int n = 0; n < v.Length; n++) v[n] = (float)random.NextDouble();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float px = (x + 0.5f) / size * grid, py = (y + 0.5f) / size * grid;
                        int cx = (int)px, cy = (int)py;
                        float fx = px - cx, fy = py - cy;
                        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
                        int x1 = (cx + 1) % grid, y1 = (cy + 1) % grid;
                        into[y * size + x] += weight * Mathf.Lerp(Mathf.Lerp(v[cy * grid + cx], v[cy * grid + x1], fx), Mathf.Lerp(v[y1 * grid + cx], v[y1 * grid + x1], fx), fy);
                    }
                weight *= 0.55f;
            }
            Even(into);
            return into;
        }

        public static byte[] Make(int size)
        {
            float[] first = Net(size, 6, 23, 311), second = Net(size, 9, 31, 733), blotch = Blotches(size, 97);
            var grain = new float[size * size];
            var random = new System.Random(5);
            for (int n = 0; n < grain.Length; n++) grain[n] = (float)random.NextDouble();
            var into = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int n = y * size + x;
                    // (the grain a little smoothed: each point with its four neighbours)
                    float g = 0.4f * grain[n] + 0.15f * (grain[y * size + (x + 1) % size] + grain[y * size + (x + size - 1) % size] + grain[((y + 1) % size) * size + x] + grain[((y + size - 1) % size) * size + x]);
                    into[4 * n] = (byte)Mathf.RoundToInt(255f * first[n]); into[4 * n + 1] = (byte)Mathf.RoundToInt(255f * second[n]);
                    into[4 * n + 2] = (byte)Mathf.RoundToInt(255f * g); into[4 * n + 3] = (byte)Mathf.RoundToInt(255f * blotch[n]);
                }
            return into;
        }
    }
}
