using System;
using System.Threading;
using UnityEngine;

namespace NaturalOcean
{
    /// <summary>
    /// The waves a hull makes going through the water (since 0.3.0: until then what floated left foam and nothing else, the
    /// README's "what floats leaves no wake").
    ///
    /// What happens, as read (Kelvin 1887; Lighthill's "Waves in Fluids"; Rabaud and Moisy, "Ship wakes: Kelvin or Mach
    /// angle?", 2013): a hull pushes the water aside and down where it floats, and as it moves, that push moves over the
    /// water. Deep water answers each push with waves that run each at its own speed (omega squared = g k), and the ones
    /// a steadily moving push keeps feeding are those whose crests keep pace with it along their own way: the transverse
    /// waves across the track, as long as 2 pi U squared over g, and the divergent waves slanting out from the bow. Their
    /// envelope is the Kelvin wedge, 19.5 degrees either side of the track, whatever the speed; the crests are steepest on
    /// its edge (the cusps, where the two kinds meet). A long hull going fast feeds the long waves little, and its wake is
    /// then seen narrower than Kelvin's, as aerial photographs measure it (Rabaud and Moisy): the angle falls as one over
    /// the Froude number from about 0.6. In a turn the wake curves, in shallow water its waves are slower and the wedge
    /// widens, and behind a hull that stops the waves run on and spread.
    ///
    /// None of that is drawn here as a shape: it comes out of the water's own sum. A patch of water is kept round the craft
    /// being flown while it is in the water (sixteen of its transverse waves across, eight points to each, and from twenty to
    /// thirty-two hull lengths), its surface's height and the potential of its surface's velocity on a grid of 128 by 128.
    /// Each step the hull's pressure is laid on it where the hull floats (its wet parts' waterline as an ellipse, as deep as
    /// they displace), and every wave of the patch is carried on exactly as deep water carries it (a turn of each wave's
    /// height and potential at its own speed, by the sea's own Fourier transform; finite depth where the bottom is near); the
    /// patch follows the hull (moved by a turn of each wave's phase, so nothing is smeared), and what runs out of it is taken
    /// up at its edge. The sea's shader adds the patch's height and slope to the water, as it adds a blow's dent (Dents). On
    /// another thread, twenty times a second of the game's time, some 15 ms a step on this Mac.
    /// </summary>
    sealed class Wakes
    {
        const int N = 128;
        static Wakes instance;
        public static bool On = true;

        readonly Fourier fourier = new Fourier(N);
        // the water's state, in the patch (rows along the sea's second axis, columns along its first): its height (m) and the
        // potential of its surface's velocity (m^2/s)
        readonly float[] eta = new float[N * N], phi = new float[N * N];
        readonly float[] zr = new float[N * N], zi = new float[N * N];
        readonly float[] kx = new float[N * N], ky = new float[N * N], kk = new float[N * N];
        readonly float[] sponge = new float[N * N], edge = new float[N * N];
        readonly float[] hx = new float[N * N], hy = new float[N * N], steep = new float[N * N];
        Color[] cellsA = new Color[N * N], movesA = new Color[N * N], cellsB = new Color[N * N], movesB = new Color[N * N];
        Texture2D picture, movesPicture;

        // the patch
        bool live;
        string onBody = "";
        float side = 200f, g = 9.81f;
        Vector2d middle;                 // where its middle is, on the sea's two axes (m): where the next step will have it
        double lastUT;
        float quiet;                     // s since a hull last pushed it
        uint craftId;

        // a step in hand on the other thread, and what it was asked
        volatile bool busy, done;
        struct Ask { public float dt, depth, length, beam, draft, cosT, sinT; public double dx, dy; public bool pushed, settle; public Vector2d middle; }
        Ask asked;
        bool settled;
        Vector2d shownMiddle;            // where the patch's middle was for the picture now shown
        bool shown;
        float stepMs;
        static float Highest;
        static Vector2d lastAt;
        public static string Said = "no wake";

        public Wakes() { instance = this; }

        void Lay(float newSide)
        {
            side = newSide;
            float dk = 2f * Mathf.PI / side, cell = side / N;
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                {
                    int k = r * N + c;
                    float x = (c < N / 2 ? c : c - N) * dk, y = (r < N / 2 ? r : r - N) * dk;
                    kx[k] = x; ky[k] = y; kk[k] = Mathf.Sqrt(x * x + y * y);
                    // (taken up at the edge: the outer fifth of the patch, harder the further out, so that what runs out of
                    // it is not brought back in at the other side, nor thrown back off a wall)
                    float u = Mathf.Abs(c - N / 2 + 0.5f) / (N / 2f), v = Mathf.Abs(r - N / 2 + 0.5f) / (N / 2f);
                    float into = Mathf.Clamp01((Mathf.Max(u, v) - 0.72f) / 0.28f);
                    sponge[k] = 6f * into * into;
                    edge[k] = 1f - Mathf.SmoothStep(0f, 1f, (Mathf.Max(u, v) - 0.6f) / 0.3f);
                }
            Array.Clear(eta, 0, eta.Length); Array.Clear(phi, 0, phi.Length);
        }

        /// <summary>The patch made so wide instead, about the same middle, its water kept (read between the old points; nothing beyond them).</summary>
        void Resize(float newSide)
        {
            float oldCell = side / N, newCell = newSide / N;
            var e2 = new float[N * N]; var p2 = new float[N * N];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float x = (i - N / 2) * newCell / oldCell + N / 2, y = (j - N / 2) * newCell / oldCell + N / 2;
                    int i0 = Mathf.FloorToInt(x), j0 = Mathf.FloorToInt(y);
                    if (i0 < 0 || j0 < 0 || i0 >= N - 1 || j0 >= N - 1) continue;
                    float a = x - i0, b = y - j0;
                    int k00 = j0 * N + i0, k10 = k00 + 1, k01 = k00 + N, k11 = k01 + 1;
                    e2[j * N + i] = (eta[k00] * (1f - a) + eta[k10] * a) * (1f - b) + (eta[k01] * (1f - a) + eta[k11] * a) * b;
                    p2[j * N + i] = (phi[k00] * (1f - a) + phi[k10] * a) * (1f - b) + (phi[k01] * (1f - a) + phi[k11] * a) * b;
                }
            Lay(newSide);
            Array.Copy(e2, eta, N * N); Array.Copy(p2, phi, N * N);
        }

        /// <summary>The craft being flown, as far as the water goes: whether it is in it, where its wet parts' middle is, its way, and how long, wide and deep it floats.</summary>
        static bool Hull(Vessel v, out Vector3d middle, out Vector3 heading, out float length, out float beam, out float draft, out float speed)
        {
            middle = Vector3d.zero; heading = Vector3.forward; length = beam = draft = speed = 0f;
            if (v == null || !v.loaded || v.packed || v.parts == null || v.altitude > 30.0) return false;
            CelestialBody body = v.mainBody;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, v.CoMD);
            double weight = 0.0;
            Vector3d sum = Vector3d.zero;
            float volume = 0f;
            int wetParts = 0;
            foreach (Part p in v.parts)
            {
                if (p == null || p.partBuoyancy == null || p.submergedPortion < 0.02) continue;
                float lift = Vector3.Dot(p.partBuoyancy.effectiveForce, up);
                if (lift <= 0f) continue;
                Vector3d at = (Vector3d)p.transform.TransformPoint(p.CenterOfBuoyancy);
                sum += at * lift; weight += lift; wetParts++;
                // (what it displaces: its buoyancy over the water's weight, kN against kg and the world's pull)
                volume += lift * 1000f / (1025f * (float)(body.GeeASL * 9.80665));
            }
            if (wetParts == 0 || weight <= 0.0) return false;
            middle = sum / weight;
            // (the way it goes through the water; standing still, the way its longest wet stretch lies)
            Rigidbody rb = v.rootPart != null ? v.rootPart.rb : null;
            Vector3 moving = rb != null ? rb.velocity + (Vector3)Krakensbane.GetFrameVelocity() : Vector3.zero;
            moving -= up * Vector3.Dot(moving, up);
            speed = moving.magnitude;
            Vector3 along = speed > 0.3f ? moving / speed : Vector3.ProjectOnPlane(v.ReferenceTransform != null ? v.ReferenceTransform.up : Vector3.forward, up).normalized;
            if (along.sqrMagnitude < 0.5f) along = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            Vector3 across = Vector3.Cross(up, along);
            float a0 = 1e9f, a1 = -1e9f, b0 = 1e9f, b1 = -1e9f;
            foreach (Part p in v.parts)
            {
                if (p == null || p.partBuoyancy == null || p.submergedPortion < 0.02) continue;
                Vector3 d = (Vector3)((Vector3d)p.transform.TransformPoint(p.CenterOfBuoyancy) - middle);
                float r = Mathf.Sqrt(Mathf.Max((float)p.radiativeArea, 0.2f) / 12.566f);
                float a = Vector3.Dot(d, along), b = Vector3.Dot(d, across);
                a0 = Mathf.Min(a0, a - r); a1 = Mathf.Max(a1, a + r); b0 = Mathf.Min(b0, b - r); b1 = Mathf.Max(b1, b + r);
            }
            length = Mathf.Max(a1 - a0, 0.6f); beam = Mathf.Max(b1 - b0, 0.4f);
            // (as deep as it displaces, over the waterline's ellipse)
            draft = Mathf.Clamp(volume / (0.785f * length * beam), 0.02f, 6f);
            heading = along;
            return true;
        }

        /// <summary>
        /// Once a frame, from the sea's own step: the last step's water into the pictures and the sea's shader told where the
        /// patch lies (`at`: the point of the sea under the camera on the sea's two axes); and the next step asked of the other
        /// thread, with the hull as it is now.
        /// </summary>
        public void Step(CelestialBody body, Vector2d at, Material sea)
        {
            if (sea == null) return;
            if (done)
            {
                done = false;
                if (picture == null) picture = new Texture2D(N, N, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Natural Ocean: a wake" };
                if (movesPicture == null) movesPicture = new Texture2D(N, N, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Natural Ocean: a wake, sideways" };
                picture.SetPixels(cellsB); picture.Apply(false);
                movesPicture.SetPixels(movesB); movesPicture.Apply(false);
                shownMiddle = asked.middle;
                shown = true;
            }
            Vessel v = FlightGlobals.ActiveVessel;
            Vector3d mid = Vector3d.zero; Vector3 heading = Vector3.forward;
            float length = 0f, beam = 0f, draft = 0f, speed = 0f;
            bool hull = On && body != null && body.ocean && v != null && Hull(v, out mid, out heading, out length, out beam, out draft, out speed);
            double ut = Planetarium.GetUniversalTime();
#if DEV
            if (testing && body != null && v != null)
            {
                hull = true; length = testL; beam = testB; draft = testD; speed = testU;
                mid = v.CoMD;
            }
#endif
            if (hull)
            {
                Sea.SpotOf(mid, out Vector2d spot);
                Vector2 way = Sea.OnAxes(heading);
#if DEV
                if (testing)
                {
                    // (a hull that is not there: set going from where the craft is, straight on at its speed, or round a circle)
                    double t = ut - testFrom;
                    if (testTurn > 0f)
                    {
                        double r = testU / testTurn, a0 = testHeading;
                        double a = a0 + testTurn * t;
                        spot = testAt + new Vector2d(r * (Math.Sin(a) - Math.Sin(a0)), -r * (Math.Cos(a) - Math.Cos(a0)));
                        way = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                    }
                    else
                    {
                        way = new Vector2(Mathf.Cos(testHeading), Mathf.Sin(testHeading));
                        spot = testAt + new Vector2d(way.x, way.y) * (testU * t);
                    }
                    mid = Vector3d.zero;
                }
#endif
                // (Big enough to hold the wake some way behind the hull, fine enough to carry its waves: the transverse waves are
                // 2 pi U^2 / g long, and the patch is sixteen of them across, eight points to each; and never less than twenty hull
                // lengths, for a slow hull whose waves are shorter than it is and weak.)
                float waveLength = 6.2832f * speed * speed / Mathf.Max((float)(body.GeeASL * 9.80665), 0.1f);
                // (and never more than 32 hull lengths: a fast hull's wake is seen by its short slanting waves, which come off the
                // hull at its own size, and a patch sized for its long transverse waves lost them between its points)
                float wantSide = Mathf.Clamp(Mathf.Min(Mathf.Max(20f * length, 16f * waveLength), 32f * length), 24f, 900f);
                bool fresh = !live || body.bodyName != onBody || v.persistentId != craftId;
                // (a hull speeding up or slowing down: the patch made larger or smaller, its water kept)
                if (live && !fresh && !busy && (wantSide > side * 1.5f || wantSide < side / 1.5f)) Resize(wantSide);
                if (fresh && !busy)
                {
                    g = (float)(body.GeeASL * 9.80665);
                    onBody = body.bodyName; craftId = v.persistentId;
                    Lay(wantSide);
                    middle = spot; lastUT = ut; live = true; shown = false; quiet = 0f; settled = false;
                }
                if (live && !busy && !fresh)
                {
                    float dt = (float)(ut - lastUT);
                    if (dt >= 1f / 20f)
                    {
                        // (the bottom where the hull is: the waves are slower over it, as a wave feels the depth)
                        float depth = Mathf.Max(Sea.DepthAtSpot(spot), 0.5f);
                        asked = new Ask
                        {
                            dt = Mathf.Min(dt, 0.2f), depth = depth, length = length, beam = beam, draft = draft, cosT = way.x, sinT = way.y,
                            dx = spot.x - middle.x, dy = spot.y - middle.y, pushed = true, settle = !settled, middle = spot
                        };
                        middle = spot; lastUT = ut; quiet = 0f; settled = true;
                        busy = true;
                        ThreadPool.QueueUserWorkItem(Work);
                    }
                }
                Said = "a wake: the hull " + length.ToString("F1") + " by " + beam.ToString("F1") + " m, " + draft.ToString("F2") + " m deep, at " + speed.ToString("F1") + " m/s (Froude " + (speed / Mathf.Sqrt(g * length)).ToString("F2") + "); the patch " + side.ToString("F0") + " m, a step " + stepMs.ToString("F1") + " ms";
            }
            else if (live)
            {
                // (the hull gone from the water, lifted off or switched: the patch stays where it was and its waves run on and go)
                float dt = (float)(ut - lastUT);
                if (!busy && dt >= 1f / 20f)
                {
                    quiet += dt;
                    asked = new Ask { dt = Mathf.Min(dt, 0.2f), depth = 1000f, pushed = false, middle = middle };
                    lastUT = ut;
                    busy = true;
                    ThreadPool.QueueUserWorkItem(Work);
                }
                if (quiet > 40f) { live = false; shown = false; }
                Said = "a wake left behind, " + quiet.ToString("F0") + " s on";
            }
            else Said = "no wake";
            if (!shown || picture == null)
            {
                sea.SetVector("_OceanWakeAt", Vector4.zero);
                return;
            }
            Vector2d from = shownMiddle - at;
            lastAt = at;
            float fade = Mathf.Clamp01((40f - quiet) / 15f);
            sea.SetTexture("_OceanWake", picture);
            sea.SetTexture("_OceanWakeMove", movesPicture);
            sea.SetVector("_OceanWakeAt", new Vector4((float)from.x, (float)from.y, 1f / side, fade));
        }

        void Work(object _)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { Advance(asked); }
            catch (Exception ex) { Debug.LogError("[Natural Ocean] a wake's step failed: " + ex); live = false; }
            stepMs = (float)clock.Elapsed.TotalMilliseconds;
            done = true;
            busy = false;
        }

        /// <summary>One step of the patch's water, on the other thread.</summary>
        void Advance(Ask a)
        {
            float cell = side / N, dt = a.dt;
            // ---- the hull's push: its displacement laid on the water over the waterline's ellipse, smooth to its rim (a pressure
            // of the water's weight times that depth), along the potential (phi_t = -g (eta + d)). The patch's middle is where the
            // hull is now.
            if (a.pushed)
            {
                float halfL = 0.5f * a.length * 0.95f, halfB = 0.5f * a.beam * 0.95f;
                float reach = Mathf.Max(halfL, halfB) + 2f * cell;
                int span = Mathf.Min(N / 2 - 1, Mathf.CeilToInt(reach / cell));
                // (the shape's own sum, so that what is laid is the hull's whole displacement however coarse the grid is)
                double total = 0.0;
                for (int pass = 0; pass < 2; pass++)
                {
                    float scale = pass == 0 ? 0f : (float)(0.785 * a.length * a.beam * a.draft / Math.Max(total * cell * cell, 1e-6));
                    for (int j = -span; j <= span; j++)
                        for (int i = -span; i <= span; i++)
                        {
                            float x = i * cell, y = j * cell;
                            float along = x * a.cosT + y * a.sinT, across = -x * a.sinT + y * a.cosT;
                            float q = (along / Mathf.Max(halfL, 0.5f * cell)) * (along / Mathf.Max(halfL, 0.5f * cell)) + (across / Mathf.Max(halfB, 0.5f * cell)) * (across / Mathf.Max(halfB, 0.5f * cell));
                            if (q >= 1f) continue;
                            // (about as deep all along under the hull, as a hull's draft is, and coming up smoothly at its waterline)
                            float d = 1f - q * q * q;
                            if (pass == 0) { total += d; continue; }
                            int k = ((j + N / 2) & (N - 1)) * N + ((i + N / 2) & (N - 1));
                            // (the first step of a patch finds the hull already floating in the hollow it makes, at rest:
                            // laid there at once, it would send out a ring as if it had dropped in)
                            if (a.settle) eta[k] = -d * scale;
                            else phi[k] -= g * d * scale * dt;
                        }
                }
            }
            // ---- into waves (forward transform of eta + i phi: conj(F^-1(conj(z))) / N^2)
            for (int k = 0; k < N * N; k++) { zr[k] = eta[k]; zi[k] = -phi[k]; }
            fourier.Inverse(zr, zi);
            float norm = 1f / (N * N);
            for (int k = 0; k < N * N; k++) { zr[k] *= norm; zi[k] = -zi[k] * norm; }
            // ---- each wave carried on at its own speed, and the patch moved with the hull (a turn of every wave's phase)
            float finest = Mathf.PI / cell;
            float sx = (float)a.dx, sy = (float)a.dy;
            for (int r = 0; r < N; r++)
                for (int c = 0; c <= N / 2; c++)
                {
                    // (each wave and its twin running the other way, together: the height's and the potential's own parts)
                    int k = r * N + c, m = ((N - r) & (N - 1)) * N + ((N - c) & (N - 1));
                    if (c == N / 2 && r > N / 2) continue;
                    if (c == 0 && r > N / 2) continue;
                    float er = 0.5f * (zr[k] + zr[m]), ei = 0.5f * (zi[k] - zi[m]);           // eta_k
                    float pr = 0.5f * (zi[k] + zi[m]), pi = -0.5f * (zr[k] - zr[m]);          // phi_k
                    float K = kk[k];
                    if (K <= 0f) { zr[k] = 0f; zi[k] = 0f; continue; }
                    float w = Mathf.Sqrt(g * K * (float)Math.Tanh(Math.Min(K * a.depth, 20.0)));
                    float cs = Mathf.Cos(w * dt), sn = Mathf.Sin(w * dt), wg = w / g, gw = g / w;
                    float er2 = er * cs + pr * wg * sn, ei2 = ei * cs + pi * wg * sn;
                    float pr2 = pr * cs - er * gw * sn, pi2 = pi * cs - ei * gw * sn;
                    // (the shortest the grid can carry let go, and nothing much longer: a wave four points long keeps all but a
                    // fortieth of itself a second, one of two points goes in a fraction of one)
                    float soft = K / finest, s2 = soft * soft, s4 = s2 * s2, keep = Mathf.Exp(-dt * 6f * s4 * s4);
                    // (moved with the patch: the water that was at x + s is at x now)
                    float ph = kx[k] * sx + ky[k] * sy, pc = Mathf.Cos(ph) * keep, ps = Mathf.Sin(ph) * keep;
                    float Er = er2 * pc - ei2 * ps, Ei = er2 * ps + ei2 * pc, Pr = pr2 * pc - pi2 * ps, Pi = pr2 * ps + pi2 * pc;
                    // back as eta_k + i phi_k at k, and its twin's at -k (eta_-k = conj(eta_k), the same for phi)
                    // (the waves that are their own twins, at the grid's limit, are let go: a turn of phase would make them complex)
                    if (m == k) { zr[k] = 0f; zi[k] = 0f; continue; }
                    zr[k] = Er - Pi; zi[k] = Ei + Pr;
                    zr[m] = Er + Pi; zi[m] = -Ei + Pr;
                    // (Its moves sideways are left out: a wake's waves are gentle, and the transform for them cost a third of the step.)
                }
            fourier.Inverse(zr, zi);
            // ---- the state again, taken up at the edge
            for (int k = 0; k < N * N; k++)
            {
                float hold = sponge[k] > 0f ? Mathf.Exp(-sponge[k] * dt) : 1f;
                eta[k] = zr[k] * hold; phi[k] = zi[k] * hold;
            }
            // ---- what the shader is given: the height, the slope of the water as it now lies (moved sideways too, so that
            // the light agrees with the shape), how steep it is; and the moves sideways
            float inv2 = 1f / (2f * cell);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i, e = j * N + ((i + 1) & (N - 1)), wv = j * N + ((i - 1) & (N - 1)), n = ((j + 1) & (N - 1)) * N + i, s = ((j - 1) & (N - 1)) * N + i;
                    hx[k] = (eta[e] - eta[wv]) * inv2; hy[k] = (eta[n] - eta[s]) * inv2;
                    steep[k] = Mathf.Sqrt(hx[k] * hx[k] + hy[k] * hy[k]);
                }
            Color[] cells = cellsA, moves = movesA;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    float f = edge[k];
                    cells[k] = new Color(eta[k] * f, hx[k] * f, hy[k] * f, steep[k] * f);
                    moves[k] = new Color(0f, 0f, 0f, steep[k] * f);
                }
            float most = 0f; for (int k = 0; k < N * N; k++) { float h = Mathf.Abs(eta[k]); if (h > most) most = h; }
            Highest = most;
            // (the finished pictures swapped in for the main thread to take)
            cellsA = cellsB; movesA = movesB; cellsB = cells; movesB = moves;
        }

#if DEV
        /// <summary>(Development build: how the wake stands.)</summary>
        public static string Says() => Said + (instance != null && instance.live ? ", live" : "") + (instance != null ? "; highest " + Highest.ToString("F3") + " m; shown " + instance.shown + " at " + (instance.shownMiddle - lastAt).x.ToString("F0") + ", " + (instance.shownMiddle - lastAt).y.ToString("F0") + " from the camera's point" : "");

        static bool testing;
        static float testL, testB, testD, testU, testHeading, testTurn;
        static Vector2d testAt;
        static double testFrom;
        /// <summary>
        /// (Development build: a hull that is not there, set going from a place so far east and north of the craft: "length beam
        /// draft speed heading [turn [east north]]" (m, m, m, m/s, the compass's degrees, degrees a second, m, m), or "stop".)
        /// </summary>
        public static string Try(string words)
        {
            string[] w = (words ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 0 || w[0] == "stop") { testing = false; return "no test hull"; }
            float F(int n, float d) => w.Length > n ? float.Parse(w[n], System.Globalization.CultureInfo.InvariantCulture) : d;
            testL = F(0, 10f); testB = F(1, 3f); testD = F(2, 0.8f); testU = F(3, 5f); testTurn = F(5, 0f) * Mathf.Deg2Rad;
            float compass = F(4, 0f) * Mathf.Deg2Rad, east = F(6, 0f), north = F(7, 0f);
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            Vector3d up = (v.CoMD - v.mainBody.position).normalized;
            Vector3d northWay = Vector3d.Exclude(up, v.mainBody.transform.up).normalized, eastWay = Vector3d.Cross(up, northWay);
            if (!Sea.SpotOf(v.CoMD + eastWay * east + northWay * north, out testAt)) return "no sea";
            Vector2 way = Sea.OnAxes((Vector3)(northWay * Math.Cos(compass) + eastWay * Math.Sin(compass)));
            testHeading = Mathf.Atan2(way.y, way.x);
            testFrom = Planetarium.GetUniversalTime();
            testing = true;
            if (instance != null) instance.live = false;
            return "a test hull " + testL + " by " + testB + " m, " + testD + " m deep, at " + testU + " m/s (Froude " + (testU / Mathf.Sqrt(9.81f * testL)).ToString("F2") + "), from " + east + " m east and " + north + " m north of the craft";
        }
#endif
    }
}
