using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NaturalOcean
{
    /// <summary>
    /// One square patch of sea that repeats, carrying the waves of one band of lengths: N by N of them, each with a size
    /// from the spectrum and a random start that it keeps for good. Asked for a moment, it gives for every point of its grid
    /// how high the water stands, how far it has moved sideways (towards the crests: what sharpens them), how the surface
    /// slopes, and how much it is squeezed (to nothing and past it: folded over itself, a breaking crest).
    /// </summary>
    sealed class Cascade
    {
        public readonly int N, Reach;
        public readonly float Side, Lo, Hi;      // metres across; the wave numbers carried: from Lo up to Hi (radians a metre)
        readonly float[] bellA, bellB;           // each wave's own two random numbers (bell curves), fixed
        readonly float[] windE, swellE;          // each wave's energy (m2) as last shaped: from the wind's sea (falling off as the fifth power) and from the swell
        readonly float[] startRe, startIm;       // each wave's size and start, for the sea as last shaped
        readonly float[] omega;                  // how fast each swings, radians a second
        readonly float[] kx, kz, kOne;           // each wave's vector, and one over its length
        readonly float[] hr, hi;                 // each wave now
        readonly float[] r1, i1, r2, i2, r3, i3, r4, i4;
        readonly Fourier fourier;
        readonly int[] live;                     // the waves this patch carries: those whose length is in its band (a twelfth of all it has room for)
        /// <summary>Four numbers a point: moved along the first axis (m), height (m), moved along the second, and how squeezed (0 not at all, -1 to nothing, below that folded over).</summary>
        public float[] Moved;
        /// <summary>The same, and the slopes, ready for the graphics card (four half floats a point each; the slopes' four: along each axis, the squeeze, and the two slopes' squares added).</summary>
        public readonly ushort[] RawMoved, RawSlope;
        public float Roughness;                  // the mean square slope of this patch's waves, both ways together
        public float Height;                     // four times the root mean square height of this patch's waves

        public Cascade(int n, float side, float least, float most, int seed)
        {
            N = n; Side = side; Lo = least; Hi = most;
            Reach = Mathf.Min(n / 2 - 1, Mathf.CeilToInt(most * side / (2f * Mathf.PI)) + 1);
            int all = n * n;
            bellA = new float[all]; bellB = new float[all]; windE = new float[all]; swellE = new float[all];
            startRe = new float[all]; startIm = new float[all]; omega = new float[all];
            kx = new float[all]; kz = new float[all]; kOne = new float[all]; hr = new float[all]; hi = new float[all];
            r1 = new float[all]; i1 = new float[all]; r2 = new float[all]; i2 = new float[all]; r3 = new float[all]; i3 = new float[all]; r4 = new float[all]; i4 = new float[all];
            Moved = new float[all * 4];
            RawMoved = new ushort[all * 4]; RawSlope = new ushort[all * 4];
            fourier = new Fourier(n);
            var inBand = new System.Collections.Generic.List<int>();
            var random = new System.Random(seed);
            float unit = 2f * Mathf.PI / side;
            for (int m = 0; m < n; m++)
                for (int c = 0; c < n; c++)
                {
                    int at = m * n + c;
                    // (two numbers from a bell curve, by Box and Muller)
                    double u = Math.Max(random.NextDouble(), 1e-12), v = random.NextDouble();
                    double r = Math.Sqrt(-2.0 * Math.Log(u));
                    bellA[at] = (float)(r * Math.Cos(2.0 * Math.PI * v)); bellB[at] = (float)(r * Math.Sin(2.0 * Math.PI * v));
                    float x = unit * (c < n / 2 ? c : c - n), z = unit * (m < n / 2 ? m : m - n);
                    kx[at] = x; kz[at] = z;
                    float k = Mathf.Sqrt(x * x + z * z);
                    kOne[at] = k > 1e-9f ? 1f / k : 0f;
                    if (k >= least && k < most && k > 1e-9f) inBand.Add(at);
                }
            live = inBand.ToArray();
        }

        /// <summary>Every wave's energy for a sea (any thread, not while At runs). Finish must follow.</summary>
        public void Shape(in SeaState sea, double scale, double peak)
        {
            double cell = 2.0 * Math.PI / Side, area = cell * cell;
            int all = N * N;
            for (int at = 0; at < all; at++)
            {
                double x = kx[at], z = kz[at], k = Math.Sqrt(x * x + z * z);
                if (k < Lo || k >= Hi || k < 1e-9) { windE[at] = 0f; swellE[at] = 0f; omega[at] = 0f; continue; }
                Spectrum.At(x, z, sea, scale, peak, out double wind, out double swell, out double swing);
                windE[at] = (float)(wind * area); swellE[at] = (float)(swell * area); omega[at] = (float)swing;
            }
        }

        /// <summary>
        /// The mean square slope this patch's waves would have with the wind sea's short waves raised: from the frequency
        /// `knee` up to `knee2` by the frequency over the knee (the fifth power made the fourth), above that by what that came
        /// to at knee2; and the whole wind sea by `gain`.
        /// </summary>
        public double Rough(double knee, double knee2, double gain)
        {
            double sum = 0.0;
            int all = N * N;
            for (int at = 0; at < all; at++)
            {
                float w = omega[at];
                if (w == 0f) continue;
                double k2 = (double)kx[at] * kx[at] + (double)kz[at] * kz[at];
                double raised = w <= knee ? 1.0 : Math.Min(w, knee2) / knee;
                sum += (windE[at] * raised * gain + swellE[at]) * k2;
            }
            return sum;
        }

        /// <summary>Every wave's size and start, from its energy as shaped and the raising of the short waves (see Rough).</summary>
        public void Finish(double knee, double knee2, double gain)
        {
            double rough = 0.0, high = 0.0;
            int all = N * N;
            for (int at = 0; at < all; at++)
            {
                float w = omega[at];
                if (w == 0f) { startRe[at] = 0f; startIm[at] = 0f; continue; }
                double raised = w <= knee ? 1.0 : Math.Min(w, knee2) / knee;
                double energy = windE[at] * raised * gain + swellE[at];
                // (half of it to this wave and half to the one that is its mirror, which the sum makes of them both: see At)
                float size = (float)Math.Sqrt(0.5 * energy);
                startRe[at] = bellA[at] * size * 0.70710678f; startIm[at] = bellB[at] * size * 0.70710678f;
                high += energy; rough += energy * ((double)kx[at] * kx[at] + (double)kz[at] * kz[at]);
            }
            Height = 4f * (float)Math.Sqrt(high);
            Roughness = (float)rough;
        }

        /// <summary>The patch at a moment (seconds), its crests sharpened by so much (1: as much as real water's). Any one thread.</summary>
        public void At(double time, float sharp)
        {
            int n = N, all = n * n;
            int[] waves = live;
            for (int l = 0; l < waves.Length; l++)
            {
                int at = waves[l];
                float w = omega[at];
                // (the wave itself, turned back by its swing, and its mirror's twin turned forward: together a real surface whose
                // waves run the way their vectors point)
                double turn = (w * time) % (2.0 * Math.PI);
                float c = (float)Math.Cos(turn), s = (float)Math.Sin(turn);
                int row = at / n, col = at - row * n;
                int twin = ((n - row) % n) * n + (n - col) % n;
                float ar = startRe[at], ai = startIm[at], br = startRe[twin], bi = -startIm[twin];
                hr[at] = ar * c + ai * s + br * c - bi * s;
                hi[at] = ai * c - ar * s + bi * c + br * s;
            }
            Array.Clear(r1, 0, all); Array.Clear(i1, 0, all); Array.Clear(r2, 0, all); Array.Clear(i2, 0, all);
            Array.Clear(r3, 0, all); Array.Clear(i3, 0, all); Array.Clear(r4, 0, all); Array.Clear(i4, 0, all);
            int[] list = live;
            for (int l = 0; l < list.Length; l++)
            {
                int at = list[l];
                float x = kx[at], z = kz[at], one = kOne[at], a = hr[at], b = hi[at];
                // (four transforms, each carrying two of the eight things wanted, one as its real part and one as its other: height
                // and the move along the first axis; the move along the second and the slope along the first; the slope along the
                // second and how the first move changes along its own axis; how the second does, and how each changes along the other)
                float f1 = 1f - x * one;
                r1[at] = a * f1; i1[at] = b * f1;
                float zk = z * one;
                r2[at] = -x * a - zk * b; i2[at] = zk * a - x * b;
                float f3 = z - x * x * one;
                r3[at] = -b * f3; i3[at] = a * f3;
                float p = z * z * one, q = x * z * one;
                r4[at] = -(a * p - b * q); i4[at] = -(a * q + b * p);
            }
            fourier.Inverse(r1, i1, Reach); fourier.Inverse(r2, i2, Reach); fourier.Inverse(r3, i3, Reach); fourier.Inverse(r4, i4, Reach);
            float[] moved = Moved;
            for (int at = 0; at < all; at++)
            {
                float dxx = sharp * i3[at], dzz = sharp * r4[at], dxz = sharp * i4[at];
                float squeeze = (1f + dxx) * (1f + dzz) - dxz * dxz - 1f;
                float dx = sharp * i1[at], h = r1[at], dz = sharp * r2[at];
                moved[4 * at] = dx; moved[4 * at + 1] = h; moved[4 * at + 2] = dz; moved[4 * at + 3] = squeeze;
                // (the slope of the surface as it lies after the sideways move, which squeezes it at the crests)
                float sx = i2[at] / Mathf.Max(1f + dxx, 0.25f), sz = r3[at] / Mathf.Max(1f + dzz, 0.25f);
                RawMoved[4 * at] = Half(dx); RawMoved[4 * at + 1] = Half(h); RawMoved[4 * at + 2] = Half(dz); RawMoved[4 * at + 3] = Half(squeeze);
                RawSlope[4 * at] = Half(sx); RawSlope[4 * at + 1] = Half(sz); RawSlope[4 * at + 2] = Half(squeeze); RawSlope[4 * at + 3] = Half(sx * sx + sz * sz);
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Bits { [FieldOffset(0)] public float f; [FieldOffset(0)] public uint u; }

        static readonly ushort[] halfBase = new ushort[512];
        static readonly byte[] halfShift = new byte[512];

        static Cascade()
        {
            // (van der Zijp's tables: for each sign and exponent of a float, the half float's own sign and exponent and how far
            // the float's fraction is to be shifted down)
            for (int i = 0; i < 256; i++)
            {
                int e = i - 127;
                if (e < -24) { halfBase[i] = 0x0000; halfBase[i | 0x100] = 0x8000; halfShift[i] = 24; halfShift[i | 0x100] = 24; }
                else if (e < -14) { halfBase[i] = (ushort)(0x0400 >> (-e - 14)); halfBase[i | 0x100] = (ushort)((0x0400 >> (-e - 14)) | 0x8000); halfShift[i] = (byte)(-e - 1); halfShift[i | 0x100] = (byte)(-e - 1); }
                else if (e <= 15) { halfBase[i] = (ushort)((e + 15) << 10); halfBase[i | 0x100] = (ushort)(((e + 15) << 10) | 0x8000); halfShift[i] = 13; halfShift[i | 0x100] = 13; }
                else { halfBase[i] = 0x7BFF; halfBase[i | 0x100] = 0xFBFF; halfShift[i] = 24; halfShift[i | 0x100] = 24; }
            }
        }

        /// <summary>A number as a half float (the kind of texture that this Mac's graphics card will smooth between points).</summary>
        internal static ushort Half(float value)
        {
            var b = new Bits { f = value };
            uint x = b.u, top = x >> 23;
            return (ushort)(halfBase[top] + ((x & 0x007FFFFFu) >> halfShift[top]));
        }
    }

    /// <summary>
    /// The sea's waves: eight patches of different sizes at once, the largest 1800 m across and each next one 3.5 times
    /// smaller (514, 147, 42, 12, 3.4, 1 and 0.28 m), 64 points each way, each carrying the waves that fit from 3 to 10.5
    /// times across it: so there is detail from the longest swell to ripples three centimetres long. Each patch is laid
    /// on the sea twice, the second time four fifths of the size and turned eleven degrees (see Twin): one patch alone
    /// repeats, and from the air its repeat shows as a grid; two of different sizes, turned against each other, never come
    /// round to the same pattern. They are worked out on other threads, for the moment at which they will be shown, and
    /// handed to the graphics card as two stacks of textures; what floats reads the same numbers.
    /// (Eight small patches cost under half of what four of 128 points did, which each carried a band seven times as wide
    /// and so stood nine tenths empty.)
    /// </summary>
    sealed class Waves : IDisposable
    {
        public const int Size = 64, Patches = 8;
        /// <summary>The patches whose waves can break white and lift a boat: those of a third of a metre and longer.</summary>
        public const int Large = 6;
        public const float Longest = 1800f, Ratio = 3.5f, Fewest = 3f;
        /// <summary>
        /// The second laying of every patch: `TwinSize` of the size in every way (lengths and heights alike, so its slopes are
        /// the same), turned by `TwinTurn`. The two are added and the sum multiplied by `TwinWeight`, which keeps the waves'
        /// heights as the spectrum has them; their slopes come out `TwinSlopes` times too large in the square, which is
        /// allowed for where the short waves are set by measure.
        /// </summary>
        public const float TwinSize = 0.8f, TwinTurn = 0.192f;
        public static readonly float TwinWeight = 1f / Mathf.Sqrt(1f + TwinSize * TwinSize), TwinCos = Mathf.Cos(TwinTurn), TwinSin = Mathf.Sin(TwinTurn);
        public static readonly float TwinSlopes = 2f / (1f + TwinSize * TwinSize);
        public static readonly float[] Sides = MakeSides();
        public readonly Cascade[] Patch = new Cascade[Patches];
        public readonly Texture2DArray Moved, Slope;
        public SeaState Sea;                      // the sea asked for; taken up at the next working-out
        public float Sharp = 0.9f;                // how much the crests are sharpened
        public double ShownTime;                  // the moment the textures show
        public float Took;                        // milliseconds the last working-out took, on its threads
        public float HeightNow;                   // the mean of the highest third of all the waves now, m
        public float RoughNow;                    // the mean square slope of all of them
        /// <summary>How the short waves were set at the last shaping: where the fourth power's stretch ends (in peak frequencies) and what the ripples were multiplied by.</summary>
        public float Stretch, Ripples;
        readonly Thread worker, helper;
        readonly AutoResetEvent go = new AutoResetEvent(false), helpGo = new AutoResetEvent(false), helpDone = new AutoResetEvent(false);
        double helpTime;
        float helpSharp;
        volatile bool busy, ready, stop;
        double askedTime;
        int rounds;
        const int Steps = 512;
        readonly float[][] counts = { new float[Steps], new float[Steps], new float[Steps], new float[Steps], new float[Steps] };
        /// <summary>How much whiter or less white than the wind makes it (1 as measured at sea).</summary>
        public float White = 1f;
        /// <summary>Not a number until the squeezes at which the water breaks have first been found (see Levels); then the third patch's, to show.</summary>
        public float Breaks = float.NaN;
        /// <summary>
        /// The squeeze below which a breaker of each of the five larger patches' own waves begins just now (0 not squeezed, -1
        /// to nothing; -9: that size of wave does not break in this wind). What is held against it is the patch's own squeeze
        /// (both its layings) and `Longer` of the squeeze of all the longer patches together.
        /// </summary>
        public readonly float[] Levels = { -9f, -9f, -9f, -9f, -9f };
        /// <summary>The squeeze below which as much of the sea lies as that size of wave has breaking at any moment: what a map too
        /// coarse to follow the size's fronts goes by, and the spray.</summary>
        public readonly float[] Plain = { -9f, -9f, -9f, -9f, -9f };
        /// <summary>How much of the white each of the five larger patches' waves make, by the wind (they add up to 1).</summary>
        public readonly float[] Shares = new float[5];
        /// <summary>How much the squeeze of the longer waves counts towards a shorter wave's breaking: short waves break on the crests of long ones.</summary>
        public static float Longer = 0.5f;
        /// <summary>How much of the water that is breaking at a moment is where breakers begin (squeezed past the level); the rest is fronts running on (see the foam).</summary>
        public static float Outright = 0.05f;
        /// <summary>How many times the share of the sea that is breaking a map too coarse for fronts has white at a moment: a front
        /// sweeps water as it runs, a speck does not, and this makes up the white that the sweeping would have left.</summary>
        public static float PlainTimes = 4f;
        /// <summary>How much of the sea is breaking at any moment, in such a wind.</summary>
        public float Breaking;
        SeaState askedSea, shaped;
        bool everShaped;
        // (what floats reads: the heights and moves last handed to the graphics card, kept apart from what is being worked out)
        readonly float[][] shown = new float[Patches][];
        // (and those handed over the time before, with when: from the two, how fast the water itself is moving)
        float[][] before = new float[Patches][];
        double beforeTime = double.NaN, nowTime = double.NaN;
        readonly object swapLock = new object();

        static float[] MakeSides()
        {
            var sides = new float[Patches];
            for (int n = 0; n < Patches; n++) sides[n] = Longest / Mathf.Pow(Ratio, n);
            return sides;
        }

        /// <summary>Where the second laying of patch n begins, in its own sides: anywhere, so long as it is not where the first does.</summary>
        public static Vector2 TwinShift(int n) => new Vector2(0.37f + 0.21f * n, 0.71f + 0.13f * n);

        public Waves()
        {
            for (int n = 0; n < Patches; n++)
            {
                // (each patch carries the waves that fit from three to ten and a half times across it; the largest takes all that are longer too)
                float lo = n == 0 ? 0f : 2f * Mathf.PI * Fewest / Sides[n], hi = 2f * Mathf.PI * Fewest * Ratio / Sides[n];
                Patch[n] = new Cascade(Size, Sides[n], lo, hi, 7919 * (n + 1));
                shown[n] = new float[Size * Size * 4];
                before[n] = new float[Size * Size * 4];
            }
            Moved = Stack("moved"); Slope = Stack("slopes");
            worker = new Thread(Work) { IsBackground = true, Name = "Natural Ocean: waves", Priority = System.Threading.ThreadPriority.BelowNormal };
            helper = new Thread(Help) { IsBackground = true, Name = "Natural Ocean: waves (2)", Priority = System.Threading.ThreadPriority.BelowNormal };
            helper.Start();
            worker.Start();
        }

        static Texture2DArray Stack(string name) => new Texture2DArray(Size, Size, Patches, TextureFormat.RGBAHalf, true, true)
        {
            name = "Natural Ocean: " + name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
        };

        void Help()
        {
            while (!stop)
            {
                helpGo.WaitOne();
                if (stop) break;
                try { for (int n = 1; n < Patches; n += 2) Patch[n].At(helpTime, helpSharp); }
                catch (Exception ex) { Debug.LogError("[Natural Ocean] the waves could not be worked out: " + ex); }
                helpDone.Set();
            }
        }

        void Work()
        {
            while (!stop)
            {
                go.WaitOne();
                if (stop) break;
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    SeaState sea = askedSea;
                    if (!everShaped || !sea.Near(shaped)) { Reshape(sea); shaped = sea; everShaped = true; }
                    double time = askedTime;
                    float sharp = Sharp;
                    // (every other patch here and the rest on a second thread of the sea's own: shared out among the game's pool of
                    // threads, the waiting between them cost as much again as the work)
                    helpTime = time; helpSharp = sharp;
                    helpGo.Set();
                    for (int n = 0; n < Patches; n += 2) Patch[n].At(time, sharp);
                    helpDone.WaitOne();
                    // (how squeezed the water must be to break, for as much of the sea to be breaking as does in such a wind: found
                    // afresh now and then, the waves' squeezes being what they are just now)
                    if (rounds++ % 6 == 0)
                    {
                        // (Monahan and Woolf, 1989: 2.9e-7 U^3.2 of the sea is breaking at any one moment: two thousandths at 15 m/s.
                        // The white each breaker leaves behind it, fading, is most of what is seen: see Foam.)
                        // Measured from photographs of the sea, whitecap cover (all the white, fresh and fading) at 9, 15 and 22 m/s
                        // is 0.7, 3.9 and 14.6 hundredths (Monahan and O'Muircheartaigh, 1980) or 0.5, 2.4 and 6.6 (Callaghan and
                        // others, 2008).
                        // (In a storm more than that formula gives, which was fitted below 20 m/s: up to twice by 28 m/s. Measured
                        // through hurricanes the breaking part levels off near four hundredths (Holthuijsen and others, 2012),
                        // which is where this comes to at 30 m/s; with the formula alone a storm's sea was a gale's.)
                        Breaking = Mathf.Clamp(2.92e-7f * Mathf.Pow(Mathf.Max(sea.Wind, 0.1f), 3.204f) * (1f + Mathf.SmoothStep(0f, 1f, (sea.Wind - 18f) / 10f)), 0f, 0.05f) * White;
                        BreakLevel(Breaking, sea);
                    }
                    Took = (float)watch.Elapsed.TotalMilliseconds;
                }
                catch (Exception ex) { Debug.LogError("[Natural Ocean] the waves could not be worked out: " + ex); }
                ready = true; busy = false;
            }
        }

        /// <summary>
        /// Every wave's size for a sea. The spectrum gives the dominant waves; how much there is in the short ones is set by
        /// two things measured at sea from the sun's glitter (Cox and Munk, 1954). With oil poured on the water to kill the
        /// ripples, the slopes of what was left (waves longer than a foot or so) came to 0.008 + 0.00156 U in the mean square:
        /// the stretch of the spectrum over which it falls as the fourth power (Toba) instead of the fifth is made as long as
        /// gives that. On clean water all the slopes came to 0.003 + 0.00512 U: the ripples (the two smallest patches) are
        /// multiplied by what brings the whole to that.
        /// </summary>
        void Reshape(SeaState sea)
        {
            double scale = Spectrum.Scale(sea), peak = Spectrum.Peak(sea);
            Parallel.For(0, Patches, n => Patch[n].Shape(sea, scale, peak));
            double u = Math.Max(sea.Wind, 0.0), knee = 2.5 * peak;
            double wantLarge = (0.008 * Math.Min(1.0, u / 2.0) + 0.00156 * u) / TwinSlopes, wantAll = (0.003 * Math.Min(1.0, u / 2.0) + 0.00512 * u) / TwinSlopes;
            double least = 0.0, most = Math.Log(400.0);
            for (int round = 0; round < 18; round++)
            {
                double mid = 0.5 * (least + most), sum = 0.0;
                for (int n = 0; n < Large; n++) sum += Patch[n].Rough(knee, knee * Math.Exp(mid), 1.0);
                if (sum > wantLarge) most = mid; else least = mid;
            }
            double knee2 = knee * Math.Exp(0.5 * (least + most));
            double large = 0.0, small = 0.0;
            for (int n = 0; n < Patches; n++) { double r = Patch[n].Rough(knee, knee2, 1.0); if (n < Large) large += r; else small += r; }
            double gain = small > 1e-12 ? Math.Min(Math.Max((wantAll - large) / small, 0.02), 40.0) : 1.0;
            Parallel.For(0, Patches, n => Patch[n].Finish(knee, knee2, n < Large ? 1.0 : gain));
            Stretch = (float)(knee2 / peak); Ripples = (float)gain;
        }

        static uint Mix(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }

        /// <summary>A patch's squeeze at a place between four of its points (which, and where between them, from one random number): as the graphics card reads it, smoothed, which is a little less than the points' own at the extremes.</summary>
        static float Between(float[] m, uint random)
        {
            int c0 = (int)(random & 63u), r0 = (int)((random >> 6) & 63u), c1 = (c0 + 1) & 63, r1 = (r0 + 1) & 63;
            float a = ((random >> 12) & 1023u) / 1024f, b = ((random >> 22) & 1023u) / 1024f;
            return m[(r0 * Size + c0) * 4 + 3] * (1f - a) * (1f - b) + m[(r0 * Size + c1) * 4 + 3] * a * (1f - b) + m[(r1 * Size + c0) * 4 + 3] * (1f - a) * b + m[(r1 * Size + c1) * 4 + 3] * a * b;
        }

        /// <summary>
        /// Which waves break white, and how squeezed each size must be to break.
        ///
        /// Each size of wave (each patch, with its second laying) breaks BY ITSELF, where its own crests are squeezed past a
        /// level of its own: a stretch of one crest, which goes on breaking as that crest runs on, sweeping out a patch of
        /// white behind a curved front. (As it first was, all the patches' squeezes were added and the sum held against one
        /// level: then the water broke where crests of different sizes happened to cross, which they do for a tenth of a
        /// second, in a spot. A whitecap was a round fleck that flashed; drawn out along the wind's cross-line to mend that, it
        /// was a rectangle.) Short waves break on the crests of long ones: half of the longer patches' squeeze counts with a
        /// patch's own.
        ///
        /// How much of the white each size makes goes by the wind: most of the breaking crest in a wind sea runs at a third to
        /// a half of the dominant waves' speed (Phillips, 1985; Kleiss and Melville, 2010), so the share of a patch falls off
        /// either side of four tenths of that speed (evenly in the logarithm, over a factor of 1.8). The largest patch, the
        /// dominant waves and the swell, does not break at all: in a grown sea those waves seldom do, and a breaker half a
        /// kilometre long is not a whitecap. In a breeze the white horses are three or four metres long and many; in a gale
        /// they are tens of metres and fewer.
        ///
        /// Two levels for each patch. `Plain`: the squeeze that its share of the sea's breaking part lies below. `Levels`:
        /// the squeeze a tenth of that lies below (`Outright`), where a breaker begins; from there its front runs on along
        /// the crest (the foam's doing), which makes up the rest. Both are counted over places taken at random (a patch and
        /// its second laying never line up, nor one patch with another, so any place in each will do), each read between four
        /// points as the graphics card will read it, sixteen thousand at a time, the counts of earlier times fading (the
        /// shares are thousandths and less: one time's places hold too few to tell).
        /// </summary>
        void BreakLevel(float share, in SeaState sea)
        {
            const int Places = 16384;
            double fast = sea.Gravity / Math.Max(Spectrum.Peak(sea), 1e-3), all = 0.0;
            var part = new double[5];
            for (int n = 1; n < 5; n++)
            {
                // (the middle of the lengths a patch carries: its side over 5.6)
                double speed = Math.Sqrt(sea.Gravity * Sides[n] / 5.6 / (2.0 * Math.PI)), off = Math.Log(speed / (0.4 * fast)) / 0.6;
                part[n] = Math.Exp(-0.5 * off * off);
                all += part[n];
            }
            for (int n = 0; n < 5; n++) Shares[n] = all > 1e-9 ? (float)(part[n] / all) : 0f;
            uint seed = (uint)rounds * 2654435761u;
            for (int n = 0; n < 5; n++) { float[] c = counts[n]; for (int k = 0; k < Steps; k++) c[k] *= 0.8f; }
            for (int p = 0; p < Places; p++)
            {
                // (the foam lets the water break a little sooner here and later there, by a pattern pinned to the water: two even
                // numbers added, as here. Left out of this count, that unevenness broke three times the water meant.)
                uint r = Mix(seed + (uint)(p * 16 + 15));
                float uneven = Foam.Ragged * (((r & 0xFFFFu) + ((r >> 16) & 0xFFFFu)) / 65535f - 1f), longer = 0f;
                for (int n = 0; n < 5; n++)
                {
                    float[] m = Patch[n].Moved;
                    uint a = Mix(seed + (uint)(p * 16 + n * 2)), b = Mix(seed + (uint)(p * 16 + n * 2 + 1));
                    float own = TwinWeight * (Between(m, a) + Between(m, b));
                    counts[n][Mathf.Clamp((int)((own + Longer * longer - uneven + 2f) * 128f), 0, Steps - 1)] += 1f;
                    longer += own;
                }
            }
            for (int n = 0; n < 5; n++)
            {
                float[] c = counts[n];
                float total = 0f;
                for (int k = 0; k < Steps; k++) total += c[k];
                for (int which = 0; which < 2; which++)
                {
                    float want = share * Shares[n] * total * (which == 0 ? Outright : PlainTimes), sum = 0f, level = -9f;
                    // (a size whose share is next to nothing does not break: its level would be found from a place or two)
                    if (Shares[n] > 0.02f && want > 0.5f)
                        for (int k = 0; k < Steps; k++)
                        {
                            if (sum + c[k] >= want) { level = (k + (c[k] > 0f ? (want - sum) / c[k] : 0.5f)) / 128f - 2f; break; }
                            sum += c[k];
                        }
                    // (eased: found afresh from other places each time, it would flicker)
                    float[] into = which == 0 ? Levels : Plain;
                    into[n] = into[n] < -8f || level < -8f ? level : Mathf.Lerp(into[n], level, 0.3f);
                }
            }
            Breaks = Levels[2];
        }

        /// <summary>
        /// Called every frame (the game's own thread): what was worked out is handed to the graphics card, and the next
        /// moment asked for. `now` is the sea's clock, seconds; `ahead` how far on the next should be (about a frame).
        /// </summary>
        public void Step(double now, double ahead)
        {
            if (ready)
            {
                ready = false;
                float high = 0f, rough = 0f;
                lock (swapLock)
                {
                    for (int n = 0; n < Patches; n++)
                    {
                        Moved.SetPixelData(Patch[n].RawMoved, 0, n);
                        Slope.SetPixelData(Patch[n].RawSlope, 0, n);
                        float[] last = before[n]; before[n] = shown[n]; shown[n] = last;
                        Buffer.BlockCopy(Patch[n].Moved, 0, shown[n], 0, shown[n].Length * sizeof(float));
                        high += Patch[n].Height * Patch[n].Height; rough += Patch[n].Roughness;
                    }
                }
                Moved.Apply(true, false); Slope.Apply(true, false);
                HeightNow = Mathf.Sqrt(high); RoughNow = rough * TwinSlopes;
                lock (swapLock) { beforeTime = nowTime; nowTime = askedTime; }
                ShownTime = askedTime;
            }
            if (!busy)
            {
                busy = true;
                askedTime = now + ahead; askedSea = Sea;
                go.Set();
            }
        }

        /// <summary>
        /// How high the water stands over a place on the sea's two axes (metres): from the larger patches (ripples do not
        /// lift a boat). The place is where the water IS, so the sideways move is undone first (the water at a place came
        /// from a little way off). `deep`: how deep the still water is there (m): in the shallows each patch's waves are
        /// kept only as far as the depth allows, as the sea's shader keeps them.
        /// </summary>
        public float HeightAt(double x, double z, float deep = 1000f)
        {
            double fromX = x, fromZ = z;
            float height = 0f;
            lock (swapLock)
            {
                for (int n = 0; n < Large; n++) keptNow[n] = Mathf.Clamp01(0.45f * deep / Mathf.Max(Patch[n].Height, 1e-3f));
                for (int round = 0; round < 2; round++)
                {
                    float mx = 0f, mz = 0f; height = 0f;
                    for (int n = 0; n < Large; n++) if (keptNow[n] > 0f) Both(n, fromX, fromZ, keptNow[n], ref mx, ref height, ref mz);
                    fromX = x - mx; fromZ = z - mz;
                }
                {
                    float mx = 0f, mz = 0f; height = 0f;
                    for (int n = 0; n < Large; n++) if (keptNow[n] > 0f) Both(n, fromX, fromZ, keptNow[n], ref mx, ref height, ref mz);
                }
            }
            return height;
        }

        readonly float[] keptNow = new float[Large];

        /// <summary>
        /// The water at a place on the sea's two axes: how high it stands (m) and how fast the water itself is moving there
        /// (m/s: along the first axis, up, along the second). Water in a wave goes round: up as the crest comes, forward
        /// under it, down behind it, back in the trough. What floats is carried by that, not only lifted.
        /// </summary>
        public float WaterAt(double x, double z, float deep, out Vector3 flow) => WaterAt(x, z, deep, out flow, out double _, out double _);

        /// <summary>The same, and where on the still sea the water now at the place belongs (`fromX`, `fromZ`: foam is kept by that, not by where the wave has carried the water to).</summary>
        public float WaterAt(double x, double z, float deep, out Vector3 flow, out double fromX, out double fromZ)
        {
            fromX = x; fromZ = z;
            float height = 0f;
            flow = Vector3.zero;
            lock (swapLock)
            {
                for (int n = 0; n < Large; n++) keptNow[n] = Mathf.Clamp01(0.45f * deep / Mathf.Max(Patch[n].Height, 1e-3f));
                float mx = 0f, mz = 0f;
                for (int round = 0; round < 3; round++)
                {
                    mx = 0f; mz = 0f; height = 0f;
                    for (int n = 0; n < Large; n++) if (keptNow[n] > 0f) Both(n, fromX, fromZ, keptNow[n], ref mx, ref height, ref mz);
                    if (round < 2) { fromX = x - mx; fromZ = z - mz; }
                }
                double took = nowTime - beforeTime;
                if (took > 1e-4 && took < 0.5)
                {
                    // (the same water a moment before: where it was then against where it is now)
                    float[][] now = shownNow; shownNow = before;
                    float bx = 0f, bh = 0f, bz = 0f;
                    for (int n = 0; n < Large; n++) if (keptNow[n] > 0f) Both(n, fromX, fromZ, keptNow[n], ref bx, ref bh, ref bz);
                    shownNow = now;
                    flow = new Vector3((mx - bx) / (float)took, (height - bh) / (float)took, (mz - bz) / (float)took);
                }
            }
            return height;
        }

        float[][] shownNow;

        /// <summary>
        /// A point of the water itself, given by where it rests on the sea's two axes (m): how far past breaking the water is
        /// there (by so much squeeze past the level that as much of the sea lies below as is breaking: see BreakLevel; above
        /// nought it is among the most squeezed water there is), and where the waves have it now: moved so far along the
        /// first axis, up, and along the second.
        /// </summary>
        public float BreakingAt(double x, double z, out Vector3 moved) => BreakingAt(x, z, out moved, out int _);

        /// <summary>The same, and which size of wave (which patch) is furthest past breaking there.</summary>
        public float BreakingAt(double x, double z, out Vector3 moved, out int which)
        {
            float past = -9f, longer = 0f, mx = 0f, mh = 0f, mz = 0f;
            which = 1;
            lock (swapLock)
            {
                for (int n = 0; n < Large; n++)
                {
                    Both(n, x, z, 1f, ref mx, ref mh, ref mz);
                    if (n >= Levels.Length) continue;
                    float own = TwinWeight * (SqueezeOf(n, x / Sides[n], z / Sides[n]) + SqueezeOf(n, (TwinCos * x - TwinSin * z) / (TwinSize * Sides[n]) + TwinShift(n).x, (TwinSin * x + TwinCos * z) / (TwinSize * Sides[n]) + TwinShift(n).y));
                    if (Plain[n] > -8f && Plain[n] - own - Longer * longer > past) { past = Plain[n] - own - Longer * longer; which = n; }
                    longer += own;
                }
            }
            moved = new Vector3(mx, mh, mz);
            return past;
        }

        /// <summary>A patch's squeeze at a place given in its own sides, smoothed between its four nearest points.</summary>
        float SqueezeOf(int n, double u, double v)
        {
            float[] from = shown[n];
            u *= Size; v *= Size;
            double fu = Math.Floor(u), fv = Math.Floor(v);
            float a = (float)(u - fu), b = (float)(v - fv);
            int c0 = (int)(((long)fu % Size + Size) % Size), r0 = (int)(((long)fv % Size + Size) % Size), c1 = (c0 + 1) % Size, r1 = (r0 + 1) % Size;
            return from[(r0 * Size + c0) * 4 + 3] * (1f - a) * (1f - b) + from[(r0 * Size + c1) * 4 + 3] * a * (1f - b) + from[(r1 * Size + c0) * 4 + 3] * (1f - a) * b + from[(r1 * Size + c1) * 4 + 3] * a * b;
        }

        /// <summary>A patch and its second laying at a place: the water's move along the first axis, its height, its move along the second, added on.</summary>
        void Both(int n, double x, double z, float kept, ref float mx, ref float height, ref float mz)
        {
            float ax = 0f, ah = 0f, az = 0f, bx = 0f, bh = 0f, bz = 0f;
            double side = Sides[n];
            Read(n, x / side, z / side, ref ax, ref ah, ref az);
            Vector2 shift = TwinShift(n);
            Read(n, (TwinCos * x - TwinSin * z) / (TwinSize * side) + shift.x, (TwinSin * x + TwinCos * z) / (TwinSize * side) + shift.y, ref bx, ref bh, ref bz);
            // (the second laying's moves turned back, and all of it as much smaller as it is laid)
            mx += kept * TwinWeight * (ax + TwinSize * (TwinCos * bx + TwinSin * bz));
            height += kept * TwinWeight * (ah + TwinSize * bh);
            mz += kept * TwinWeight * (az + TwinSize * (TwinCos * bz - TwinSin * bx));
        }

        /// <summary>A patch at a place given in its own sides, smoothed between its four nearest points.</summary>
        void Read(int n, double u, double v, ref float mx, ref float height, ref float mz)
        {
            float[] from = (shownNow ?? shown)[n];
            u *= Size; v *= Size;
            double fu = Math.Floor(u), fv = Math.Floor(v);
            float a = (float)(u - fu), b = (float)(v - fv);
            int c0 = (int)(((long)fu % Size + Size) % Size), r0 = (int)(((long)fv % Size + Size) % Size), c1 = (c0 + 1) % Size, r1 = (r0 + 1) % Size;
            int p00 = (r0 * Size + c0) * 4, p10 = (r0 * Size + c1) * 4, p01 = (r1 * Size + c0) * 4, p11 = (r1 * Size + c1) * 4;
            float w00 = (1f - a) * (1f - b), w10 = a * (1f - b), w01 = (1f - a) * b, w11 = a * b;
            mx += from[p00] * w00 + from[p10] * w10 + from[p01] * w01 + from[p11] * w11;
            height += from[p00 + 1] * w00 + from[p10 + 1] * w10 + from[p01 + 1] * w01 + from[p11 + 1] * w11;
            mz += from[p00 + 2] * w00 + from[p10 + 2] * w10 + from[p01 + 2] * w01 + from[p11 + 2] * w11;
        }

        public void Dispose()
        {
            stop = true; go.Set(); helpGo.Set();
            if (Moved != null) UnityEngine.Object.Destroy(Moved);
            if (Slope != null) UnityEngine.Object.Destroy(Slope);
        }
    }
}
