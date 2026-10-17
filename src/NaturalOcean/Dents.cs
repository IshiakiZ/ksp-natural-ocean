using System;
using UnityEngine;

namespace NaturalOcean
{
    /// <summary>
    /// The water itself pushed in by a craft that strikes it (the owner, 2026-10-13: "It should be able to deform the water
    /// like that, just like the volumetric smoke and clouds. It should predict the rocket's trajectory and then set that
    /// area of water to be able to be deformed based on a whole bunch of parameters like speed, angle, velocity, air
    /// resistance, weight, length, all of it").
    ///
    /// What happens, as read (the water entry of bodies; impact craters in liquids; the Cauchy-Poisson problem of waves from
    /// a disturbance at rest): a body that hits water fast blasts a crater out of it, as deep as it is wide or nearly, its
    /// rim heaped with the water thrown out of it (the crown above is spray, Spray.cs). The crater forms in a fraction of a
    /// second and stands for a moment, then gravity takes over: its walls run back in, meet in the middle (the jet, Spray.cs)
    /// and the water overshoots and rings, sending out a train of waves. Deep water sorts that train as it goes, each length
    /// running at its own speed (omega squared = g k): the longest waves first and fastest, shorter ones following, the rings
    /// closing up behind and growing fainter as they spread. A slanting blow digs a longer crater, deepest downrange, the
    /// water thrown mostly forward; a long craft coming in at a slant ploughs a longer furrow still.
    ///
    /// So: a patch of water a few dozen crater radii across, its surface's height on a grid of 128 by 128; at the blow it is
    /// given the crater's shape (sized by the blow's energy as the spray is, stretched along the way the craft came by its
    /// slant and its length, the rim heavier downrange), lets it open over a fraction of a second, and from then on carries
    /// each of its waves on at its own speed, exactly as deep water does (summed by the sea's own Fourier transform). The
    /// sea's shader adds the patch's height to the water and its slope to the light. One blow at a time (the latest).
    ///
    /// Before the blow: the active craft's fall is followed ahead each frame (gravity and the game's own drag on it, read
    /// from its parts, scaled by the air's density as it falls) to where and when it will meet the sea, with what speed and
    /// at what slant. A blow is then expected there, and the patch is made ready on that spot (Expected; the report says so).
    /// </summary>
    sealed class Dents
    {
        const int N = 128;
        static Dents instance;
        readonly Fourier fourier = new Fourier(N);
        readonly float[] startRe = new float[N * N], startIm = new float[N * N], re = new float[N * N], im = new float[N * N], omega = new float[N * N];
        // (the water's moves sideways: the second transform, and each wave's way, kx / k and ky / k)
        readonly float[] sideRe = new float[N * N], sideIm = new float[N * N], wayX = new float[N * N], wayY = new float[N * N];
        readonly float[] hx = new float[N * N], hy = new float[N * N], steepness = new float[N * N];
        readonly Color[] cells = new Color[N * N], moves = new Color[N * N];
        Texture2D picture, movesPicture;
        /// <summary>How large a breaking ring's lip is against how high the ring stands (the sea's own lip is a third of its
        /// waves' height from crest to trough: about this of the height above the level).</summary>
        public static float Lip = 0.6f;

        // the blow in hand
        bool live;
        Vector2d middle;                 // on the sea's two axes, m (as Foam keeps places)
        string onBody = "";
        float side = 200f;               // m across the patch
        double struckAt;                 // the game's time
        float opens, lasts, crater;      // s for the crater to open; s the patch is kept; its radius, m
        float g = 9.81f;
        Vector2 way = new Vector2(1f, 0f);   // the way the craft came, on the sea's two axes
        float longer = 1f, shift, jetAt = 1f; // the crater's shape (as Make lays it); s from the blow to the jet (as the spray has it)

        // the blow expected (from the craft's fall followed ahead)
        public static string Expected = "";
        static bool expecting;
        static Vector2d expectedAt;
        static double expectedWhen;

        public Dents() { instance = this; }

        public Texture Picture => picture;

        /// <summary>
        /// A blow has struck the sea: at `place` (the sea's two axes), digging a crater of radius `R` m (from its energy,
        /// as the spray works it out), the craft coming `along` (a way on the sea's two axes) `slant` radians below level, the
        /// craft `length` m long along its way.
        /// </summary>
        public static void Struck(CelestialBody body, Vector2d place, float R, Vector2 along, float slant, float length)
        {
            if (instance == null || body == null) return;
            instance.Make(body, place, R, along, slant, length);
        }

        void Make(CelestialBody body, Vector2d place, float R, Vector2 along, float slant, float length)
        {
            R = Mathf.Clamp(R, 0.8f, 60f);
            // (The parts of one craft often strike in two or three bunches a fraction of a second apart, a little way from one
            // another: a later, smaller bunch goes into the crater the first one dug, and must not wipe it out.)
            if (live && body.bodyName == onBody && Planetarium.GetUniversalTime() - struckAt < 2.0 && (place - middle).magnitude < 3.0 * crater && R <= crater) return;
            g = (float)(body.GeeASL * 9.80665);
            onBody = body.bodyName;
            middle = place;
            crater = R;
            // (big enough for the rings to run some way before the patch's edge, where they are let go)
            side = Mathf.Clamp(22f * R, 48f, 900f);
            struckAt = Planetarium.GetUniversalTime();
            opens = Mathf.Clamp(0.55f * Mathf.Sqrt(R / g), 0.15f, 2f);
            lasts = 50f;
            float cell = side / N;

            // ---- the crater. Its depth three quarters of its radius; stretched along the way the craft came by the slant (a
            // blow straight down is round, a grazing one twice as long) and by the craft's length lying along the water;
            // deepest a third of a radius downrange; the rim heaped with as much water as the crater lost, more of it downrange.
            if (along.sqrMagnitude < 1e-6f) along = new Vector2(1f, 0f);
            along.Normalize();
            Vector2 across = new Vector2(-along.y, along.x);
            float level = Mathf.Cos(Mathf.Clamp(slant, 0f, 1.5708f));                 // 1 grazing, 0 straight down
            longer = Mathf.Clamp(1f + 0.9f * level + 0.5f * level * Mathf.Min(length, 4f * R) / R, 1f, 3.2f);
            float deep = 0.75f * R, rimOut = 0.8f;
            shift = 0.35f * R * level;
            way = along;
            jetAt = 2.2f * Mathf.Sqrt(R / g);
            // (Never an even rim: the crown tears into fingers and the water thrown out lies thicker here than there, so the rim
            // is heaped unevenly round the crater, by chance, at some few lobes to a turn. Its rings are then higher in some
            // stretches than in others, and break there, and not all the way round, as a real one's do. Even, the first ring
            // broke all round at once and left a perfect white circle.)
            var chance = new System.Random(unchecked((int)(struckAt * 977.0) ^ (int)(R * 1000f)));
            var lobes = new float[9]; var lobesAt = new float[9];
            for (int m = 2; m <= 8; m++) { lobes[m] = (0.25f + 0.5f * (float)chance.NextDouble()) * 0.45f / Mathf.Pow(m, 0.6f); lobesAt[m] = (float)(chance.NextDouble() * 2.0 * Math.PI); }
            double hollow = 0.0, heaped = 0.0;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float x = (i - N / 2) * cell, y = (j - N / 2) * cell;
                    float a = x * along.x + y * along.y - shift, c = x * across.x + y * across.y;
                    float q = Mathf.Sqrt((a / (longer * R)) * (a / (longer * R)) + (c / R) * (c / R));
                    float h = 0f;
                    if (q < 1f) { h = -deep * (1f - q * q); hollow -= h; }
                    else if (q < 1f + rimOut)
                    {
                        float s = Mathf.Sin(Mathf.PI * (q - 1f) / rimOut);
                        float r = Mathf.Sqrt(x * x + y * y) + 1e-3f;
                        float forward = (x * along.x + y * along.y) / r;
                        float turn = Mathf.Atan2(c, a / longer), heap = 1f;
                        for (int m = 2; m <= 8; m++) heap += lobes[m] * Mathf.Cos(m * turn + lobesAt[m]);
                        h = s * s * (1f + 0.6f * level * forward) * Mathf.Max(heap, 0.25f);
                        heaped += h;
                    }
                    startRe[j * N + i] = h;
                    startIm[j * N + i] = 0f;
                }
            // (as much water heaped on the rim as went out of the crater)
            float rim = heaped > 1e-6 ? (float)(hollow / heaped) : 0f;
            for (int k = 0; k < N * N; k++) if (startRe[k] > 0f) startRe[k] *= rim;

            // ---- into waves: how much of each there is (the forward transform, from the inverse: conj(F^-1(conj(h))) / N^2)
            fourier.Inverse(startRe, startIm);
            float norm = 1f / (N * N);
            for (int k = 0; k < N * N; k++) { startRe[k] *= norm; startIm[k] = -startIm[k] * norm; }
            // each wave's speed of swing (deep water: omega^2 = g k), and the finest let go (no finer than the grid can carry)
            float dk = 2f * Mathf.PI / side, finest = 0.55f * Mathf.PI / cell;
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                {
                    float kx = (c < N / 2 ? c : c - N) * dk, ky = (r < N / 2 ? r : r - N) * dk;
                    float k = Mathf.Sqrt(kx * kx + ky * ky);
                    omega[r * N + c] = Mathf.Sqrt(g * k);
                    wayX[r * N + c] = k > 0f ? kx / k : 0f; wayY[r * N + c] = k > 0f ? ky / k : 0f;
                    float soft = k / finest;
                    float keep = Mathf.Exp(-soft * soft * soft * soft);
                    startRe[r * N + c] *= keep; startIm[r * N + c] *= keep;
                }
            live = true;
            if (expecting)
                Debug.Log("[Natural Ocean] the blow came " + (Planetarium.GetUniversalTime() - expectedWhen).ToString("F2") + " s from when it was expected, " + (place - expectedAt).magnitude.ToString("F1") + " m from where");
            expecting = false;
            Expected = "";
            Debug.Log("[Natural Ocean] the sea dented: a crater " + R.ToString("F1") + " m, " + longer.ToString("F2") + " times as long along the way the craft came, " + (slant * Mathf.Rad2Deg).ToString("F0") +
                      " degrees down, the craft " + length.ToString("F0") + " m long; the patch " + side.ToString("F0") + " m across");
        }

        /// <summary>
        /// Once a frame: the patch's water as it is now, into its pictures, and the sea's shader and the white water's told
        /// where it lies (`at`: the point of the sea under the camera on the sea's two axes).
        ///
        /// The rings break. Deep water's rings carry their water round in circles as they pass, forward at a crest and back
        /// in a trough, which draws a steep wave's crests to a point and flattens its troughs; and a ring that leaves a big
        /// crater is steep: the first one's face stands at 26 to 28 degrees one and a half to two and a half crater radii
        /// out (for a ten-metre crater, three and a half to five seconds after the blow), at the steepest a wave can stand.
        /// As read of crater collapse in deep water: the water rebounding in the middle sends out a bore that breaks as it
        /// goes, then smooths into an undular bore and a train of rings. (The user, 2026-10-14, of the smooth rings of the
        /// first try: "the ripples should be more like the curls, especially for a rocket hitting water".) So the water's
        /// moves sideways are worked out with its heights, exactly as deep water moves (a wave's sideways move is its height
        /// a quarter of a wave on); its steepness from both (|slope| and how much the water is squeezed together: for one
        /// train of waves that is the height times the wave number, everywhere along it); a ring near breaking leans forward
        /// as the sea's own crests do; one past it is white along its face (the white water's maps take that up and keep
        /// the foam it leaves) and throws the sea's own lip (the sea's shader, where its mesh is fine enough to hold it).
        /// </summary>
        public void Step(CelestialBody body, Vector2d at, Material sea, Material foam)
        {
            if (sea == null) return;
            if (!live || body == null || body.bodyName != onBody)
            {
                sea.SetVector("_OceanDentAt", Vector4.zero);
                if (foam != null) foam.SetVector("_OceanDentAt", Vector4.zero);
                return;
            }
            float since = (float)(Planetarium.GetUniversalTime() - struckAt);
            if (since > lasts || since < 0f)
            {
                live = false;
                sea.SetVector("_OceanDentAt", Vector4.zero);
                if (foam != null) foam.SetVector("_OceanDentAt", Vector4.zero);
                return;
            }
            if (picture == null) picture = new Texture2D(N, N, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Natural Ocean: a blow's dent" };
            if (movesPicture == null) movesPicture = new Texture2D(N, N, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Natural Ocean: a blow's dent, sideways" };

            // the crater opening, then every wave carried on at its own speed from the crater at rest. Two transforms: the
            // height with the move along the first axis (h + i x: H (1 - kx/k)), and the move along the second (i y: -H ky/k).
            float tau = since - opens;
            float grown = tau < 0f ? Mathf.SmoothStep(0f, 1f, since / opens) : 1f;
            if (tau < 0f) tau = 0f;
            for (int k = 0; k < N * N; k++)
            {
                float swing = Mathf.Cos(omega[k] * tau) * grown;
                float hr = startRe[k] * swing, hi = startIm[k] * swing;
                re[k] = hr * (1f - wayX[k]); im[k] = hi * (1f - wayX[k]);
                sideRe[k] = -hr * wayY[k]; sideIm[k] = -hi * wayY[k];
            }
            fourier.Inverse(re, im);
            fourier.Inverse(sideRe, sideIm);

            // (let go towards the patch's edge, where the grid would bring the rings back in from the other side; and let go
            // at the end of its time)
            // (And the churned middle. Where the crater's walls met and the jet went up, the water is a turmoil of froth and
            // air for a long while, breaking every way, and that eats the waves there. The waves alone, which know nothing of
            // it, ring on in the middle for ten seconds and more, a smooth cone five metres high rising and falling in the white
            // (a ten-metre crater). So from a little after the jet the middle is let calm, over a second or two; the rings
            // already running out are kept, and those still to leave come out of the white fainter.)
            // (And the jet. As the walls meet, the water they bring in goes up the middle as a thin column, the jet, which
            // the spray draws; the waves alone raise it there as a broad smooth hill, which under the white was a snowy
            // mountain standing in the crater. So once the crater is filling, what rises in its middle is taken down to a
            // low heave.)
            float cell = side / N, fade = Mathf.Clamp01((lasts - since) / 10f);
            float calm = 1f - Mathf.Exp(-Mathf.Max(since - 1.2f * jetAt, 0f) / Mathf.Max(0.6f * jetAt, 0.3f));
            bool filling = since > opens;
            Vector2 athwart = new Vector2(-way.y, way.x);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    float x = Mathf.Abs(i - N / 2) / (float)N, y = Mathf.Abs(j - N / 2) / (float)N;
                    float edge = 1f - Mathf.SmoothStep(0f, 1f, (Mathf.Max(x, y) - 0.34f) / 0.14f);
                    float w = edge * fade;
                    if (calm > 0f || (filling && re[k] > 0f))
                    {
                        float px = (i - N / 2) * cell, py = (j - N / 2) * cell;
                        float a = px * way.x + py * way.y - shift, c = px * athwart.x + py * athwart.y;
                        float q = Mathf.Sqrt((a / (longer * crater)) * (a / (longer * crater)) + (c / crater) * (c / crater));
                        w *= 1f - calm * (1f - Mathf.SmoothStep(0f, 1f, (q - 1.3f) / 1.1f));
                        if (filling && re[k] > 0f) w *= 1f - 0.8f * (1f - Mathf.SmoothStep(0f, 1f, (q - 0.6f) / 0.5f));
                    }
                    re[k] *= w; im[k] *= w; sideIm[k] *= w;
                }
            // ---- how steep: from the slope and the squeeze (the moves' spread), before the lean
            float inv2 = 1f / (2f * cell);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i, e = j * N + ((i + 1) & (N - 1)), wv = j * N + ((i - 1) & (N - 1)), n = ((j + 1) & (N - 1)) * N + i, s = ((j - 1) & (N - 1)) * N + i;
                    hx[k] = (re[e] - re[wv]) * inv2; hy[k] = (re[n] - re[s]) * inv2;
                    float squeeze = (im[e] - im[wv]) * inv2 + (sideIm[n] - sideIm[s]) * inv2;
                    steepness[k] = Mathf.Sqrt(hx[k] * hx[k] + hy[k] * hy[k] + squeeze * squeeze);
                }
            // ---- the lean of a ring near breaking (its water carried outward the further the higher it stands, as the sea's
            // own crests are: the front closes up, the back opens out), and where it breaks: white on its upper face, outward
            // (White only near the top of the face: within some sixty degrees of the crest, where the water stands at more than
            // half its wave's height. That height, everywhere along a wave, is that of the water and of its move sideways
            // together, which deep water makes equal. Laid over the whole face, the white the rings left was a disc.)
            float leanFrom = 0.2f, leanFull = 0.36f, breaksFrom = 0.38f, breaksFull = 0.5f;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    float px = (i - N / 2) * cell, py = (j - N / 2) * cell;
                    float a = px * way.x + py * way.y - shift, c = px * athwart.x + py * athwart.y;
                    Vector2 outward = way * (a / (longer * longer)) + athwart * c;
                    float len = outward.magnitude;
                    outward = len > 1e-4f ? outward / len : way;
                    float h = re[k];
                    float wave = Mathf.Sqrt(h * h + im[k] * im[k] + sideIm[k] * sideIm[k]);
                    float nearly = Mathf.SmoothStep(0f, 1f, (steepness[k] - leanFrom) / (leanFull - leanFrom));
                    float lean = 1.6f * nearly * Mathf.Max(h, 0f);
                    im[k] += outward.x * lean; sideIm[k] += outward.y * lean;
                    float face = -(hx[k] * outward.x + hy[k] * outward.y);
                    float breaks = Mathf.SmoothStep(0f, 1f, (steepness[k] - breaksFrom) / (breaksFull - breaksFrom)) * Mathf.SmoothStep(0f, 1f, face / 0.08f) * Mathf.SmoothStep(0f, 1f, (h / Mathf.Max(wave, 0.05f) - 0.35f) / 0.3f);
                    moves[k] = new Color(im[k], sideIm[k], breaks, steepness[k]);
                }
            // ---- the slopes of the water as it now lies (moved sideways too: the height's slope over the stretch of the
            // still sea under it), so that the light agrees with the shape; and the height and its steepness, for the lip
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i, e = j * N + ((i + 1) & (N - 1)), wv = j * N + ((i - 1) & (N - 1)), n = ((j + 1) & (N - 1)) * N + i, s = ((j - 1) & (N - 1)) * N + i;
                    float xx = 1f + (moves[e].r - moves[wv].r) * inv2, xy = (moves[n].r - moves[s].r) * inv2;
                    float yx = (moves[e].g - moves[wv].g) * inv2, yy = 1f + (moves[n].g - moves[s].g) * inv2;
                    float J = Mathf.Max(xx * yy - xy * yx, 0.2f);
                    float sx = (yy * hx[k] - yx * hy[k]) / J, sy = (-xy * hx[k] + xx * hy[k]) / J;
                    cells[k] = new Color(re[k], sx, sy, steepness[k]);
                }
            picture.SetPixels(cells);
            picture.Apply(false);
            movesPicture.SetPixels(moves);
            movesPicture.Apply(false);
            Vector2d from = middle - at;
            var dentAt = new Vector4((float)from.x, (float)from.y, 1f / side, 1f);
            // (the crater's middle, where it was deepest, and the way the craft came: the rings run out from there)
            Vector2d mid = from + new Vector2d(way.x, way.y) * shift;
            // (the leading ring breaks hardest at about twice the time to the jet, and has come down by about three times it:
            // from then on what breaks spills, with no lip thrown far)
            var curl = new Vector4(Lip, cell, Mathf.SmoothStep(0f, 1f, (since - 2.0f * jetAt) / (0.8f * jetAt)), 1f / longer);
            sea.SetTexture("_OceanDent", picture);
            sea.SetTexture("_OceanDentMove", movesPicture);
            sea.SetVector("_OceanDentAt", dentAt);
            sea.SetVector("_OceanDentMid", new Vector4((float)mid.x, (float)mid.y, way.x, way.y));
            sea.SetVector("_OceanDentCurl", curl);
            if (foam != null)
            {
                foam.SetTexture("_OceanDentMove", movesPicture);
                foam.SetVector("_OceanDentAt", dentAt);
            }
        }

        /// <summary>
        /// Once a frame: the active craft's fall followed ahead, to where and when it will meet the sea, how fast and at what
        /// slant. The drag is the game's own on its parts now, as a share of the air's density and the speed squared, carried
        /// on with the density of the air it falls into.
        /// </summary>
        public static void Predict()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || !v.loaded || v.packed || v.mainBody == null || !v.mainBody.ocean || v.LandedOrSplashed || v.verticalSpeed > -5.0 || v.altitude > 8000.0)
            {
                if (expecting && Planetarium.GetUniversalTime() > expectedWhen + 2.0) { expecting = false; Expected = ""; }
                return;
            }
            CelestialBody body = v.mainBody;
            double mass = v.totalMass;
            if (mass <= 0.0) return;
            // (the drag on it now, from the game's own aerodynamics on each part, as an acceleration over density and speed squared)
            double drag = 0.0;
            foreach (Part p in v.parts) if (p != null) drag += p.dragScalar;
            double rho = Math.Max(v.atmDensity, 1e-6), speed = Math.Max(v.srfSpeed, 1.0);
            double k = drag / mass / (rho * speed * speed);
            Vector3d pos = v.CoMD - body.position, vel = v.srf_velocity;
            double mu = body.gravParameter, R = body.Radius, t = 0.0, dt = 0.05;
            for (int n = 0; n < 800; n++)
            {
                double r = pos.magnitude, alt = r - R;
                if (alt <= 0.0)
                {
                    Vector3d world = body.position + pos;
                    double ground = body.TerrainAltitude(body.GetLatitude(world), body.GetLongitude(world), true);
                    if (ground >= -1.0) { expecting = false; Expected = "the craft will come down on land"; return; }
                    Sea.WaterAt(world, out Vector3 _, out Vector2d place);
                    Vector3d up = pos / r;
                    double down = -Vector3d.Dot(vel, up), flat = (vel + up * down).magnitude;
                    if (!expecting || Math.Abs(Planetarium.GetUniversalTime() + t - expectedWhen) > 0.5)
                        Debug.Log("[Natural Ocean] a blow on the sea expected in " + t.ToString("F2") + " s, at " + place.x.ToString("F1") + ", " + place.y.ToString("F1") + " on the sea's axes, " + vel.magnitude.ToString("F0") + " m/s");
                    expecting = true;
                    expectedAt = place;
                    expectedWhen = Planetarium.GetUniversalTime() + t;
                    Expected = "a blow on the sea expected in " + t.ToString("F1") + " s at " + vel.magnitude.ToString("F0") + " m/s, " + (Math.Atan2(down, flat) * 57.2958).ToString("F0") + " degrees down, " + mass.ToString("F1") + " t";
                    return;
                }
                double density = body.GetDensity(body.GetPressure(alt), body.GetTemperature(alt));
                Vector3d a = -pos * (mu / (r * r * r)) - vel * (k * density * vel.magnitude);
                // (a step at a time, the speed's middle used for the place: good to a fraction of a metre over a few seconds)
                Vector3d half = vel + a * (0.5 * dt);
                pos += half * dt;
                vel += a * dt;
                t += dt;
            }
            expecting = false;
            Expected = "";
        }

#if DEV
        /// <summary>(Development build: a blow on the sea below the camera's look, without a craft: "R slant length heading" (m, degrees down, m, degrees from the first axis).)</summary>
        public static string Try(string words)
        {
            string[] w = (words ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float R = w.Length > 0 ? float.Parse(w[0], System.Globalization.CultureInfo.InvariantCulture) : 10f;
            float slant = (w.Length > 1 ? float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : 70f) * Mathf.Deg2Rad;
            float length = w.Length > 2 ? float.Parse(w[2], System.Globalization.CultureInfo.InvariantCulture) : 20f;
            float heading = (w.Length > 3 ? float.Parse(w[3], System.Globalization.CultureInfo.InvariantCulture) : 0f) * Mathf.Deg2Rad;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || instance == null) return "no craft or no sea";
            Sea.WaterAt(v.CoMD, out Vector3 _, out Vector2d place);
            instance.Make(v.mainBody, place, R, new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)), slant, length);
            return "dented under the craft: " + R + " m";
        }

        public static string Says() => (instance != null && instance.live ? "a dent " + instance.crater.ToString("F1") + " m, " + (Planetarium.GetUniversalTime() - instance.struckAt).ToString("F1") + " s old, the patch " + instance.side.ToString("F0") + " m" : "no dent") + (expecting ? "; " + Expected : "");
#endif
    }
}
