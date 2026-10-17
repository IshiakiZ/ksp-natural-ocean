using System;
using UnityEngine;

namespace NaturalOcean
{
    /// <summary>
    /// Spray. Water that breaks throws spray, and in a strong wind the tops of breaking crests are torn off and blown down
    /// the wind as spindrift (from about a fresh gale; in a storm the air over the sea is full of it). Where the water is
    /// breaking is what the white water's maps hold (Foam: the running fronts of the open sea and the surf's alike): a
    /// small copy of them is read back each frame, and every point of it that is breaking that moment throws two things.
    ///
    /// The fall: water going off the lip of a breaking crest, forward as fast as the wave runs and a little faster, nearly
    /// level, and then down as water falls: "a mini sideways waterfall" (the user, 2026-10-11, of what spray off a crest
    /// is). Thin bright strands, each drawn out along its own way, so many where they leave the lip that they are one
    /// white sheet, coming apart into threads as they fall. They last until they are in the water: half a second.
    ///
    /// Mist: what is left in the air. Soft round wisps, large, thin, many, each one of four shapes turned its own way, taken
    /// by the wind, growing and thinning over some seconds: a veil streaming down the wind from the crest.
    ///
    /// (As it first was there was one kind: soft puffs drawn out along the way they went. Thrown upward they were upright
    /// streaks standing on the sea, and the user: "The mist doesn't look that good. I want it to look like real mist and
    /// splashes and everything." Before that, places on the water were looked at at random and the waves' own numbers
    /// asked whether each was past breaking: three bursts a second in a near gale, none where the eye saw a crest break.)
    /// (Both are lit as the foam is. The air's own haze of spray in a gale is not these but the sea's shader, which lets
    /// less of the far sea be seen as the wind rises.)
    /// </summary>
    sealed class Spray : IDisposable
    {
        readonly GameObject mistHolder, dropHolder;
        readonly ParticleSystem mist, drops;
        readonly Material mistMaterial, dropMaterial;
        readonly Texture2D wisps, dot;
        readonly System.Random random = new System.Random(12345);
        ParticleSystem.EmitParams one;
        /// <summary>Bursts let go in the last second or so, and puffs and drops alive (for the report).</summary>
        public int Bursts, Alive;
        int burstsNow; float counted;

        public Spray()
        {
            Shader s = Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Alpha Blended");

            // ---- the mist
            mistHolder = new GameObject("Natural Ocean: spray (mist)") { layer = 0 };
            mist = mistHolder.AddComponent<ParticleSystem>();
            mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = mist.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 16000;
            main.playOnAwake = false;
            main.loop = true;
            main.gravityModifier = 0.015f;
            main.startSpeed = 0f;
            ParticleSystem.EmissionModule emission = mist.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = mist.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = mist.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 2.2f)));
            ParticleSystem.ColorOverLifetimeModule colour = mist.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.4f, 0.35f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            ParticleSystem.RotationOverLifetimeModule turns = mist.rotationOverLifetime;
            turns.enabled = true;
            turns.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            // (four shapes of wisp on one picture: each puff keeps the one it is given)
            ParticleSystem.TextureSheetAnimationModule sheet = mist.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 2; sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
            sheet.cycleCount = 1;
            var mistDrawn = mistHolder.GetComponent<ParticleSystemRenderer>();
            // (drawn out a little along the way the wind takes it: a veil streaming off a crest. Thrown upward and drawn out,
            // as they first were, the puffs were upright streaks; these leave nearly level.)
            mistDrawn.renderMode = ParticleSystemRenderMode.Stretch;
            mistDrawn.velocityScale = 0.03f;
            mistDrawn.lengthScale = 1f;
            mistDrawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mistDrawn.receiveShadows = false;
            mistDrawn.sortMode = ParticleSystemSortMode.Distance;
            wisps = Wisps();
            mistMaterial = new Material(s) { name = "Natural Ocean: spray (mist)", mainTexture = wisps };
            if (mistMaterial.HasProperty("_TintColor")) mistMaterial.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            // (soft where it meets the water: a wisp cut off in a hard line by a wave is a card)
            if (mistMaterial.HasProperty("_InvFade")) mistMaterial.SetFloat("_InvFade", 0.35f);
            mistDrawn.sharedMaterial = mistMaterial;
            mist.Play(false);
            try { FloatingOrigin.RegisterParticleSystem(mist); } catch (Exception) { }

            // ---- the drops
            dropHolder = new GameObject("Natural Ocean: spray (drops)") { layer = 0 };
            drops = dropHolder.AddComponent<ParticleSystem>();
            drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            main = drops.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 26000;
            main.playOnAwake = false;
            main.loop = true;
            main.gravityModifier = 1f;
            main.startSpeed = 0f;
            emission = drops.emission;
            emission.enabled = false;
            shape = drops.shape;
            shape.enabled = false;
            colour = drops.colorOverLifetime;
            colour.enabled = true;
            var falls = new Gradient();
            falls.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = falls;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = drops.limitVelocityOverLifetime;
            drag.enabled = true; drag.drag = 0.35f; drag.limit = 80f; drag.dampen = 0f;
            var dropDrawn = dropHolder.GetComponent<ParticleSystemRenderer>();
            // (each drawn out along the way it is going: thrown water is seen as short bright strokes, not as dots)
            dropDrawn.renderMode = ParticleSystemRenderMode.Stretch;
            dropDrawn.velocityScale = 0.045f;
            dropDrawn.lengthScale = 3f;
            dropDrawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dropDrawn.receiveShadows = false;
            dropDrawn.sortMode = ParticleSystemSortMode.None;
            dot = Dot();
            dropMaterial = new Material(s) { name = "Natural Ocean: spray (drops)", mainTexture = dot };
            // (brighter than the mist they fly through: thrown water takes the light, and grey drops against white mist were pepper)
            if (dropMaterial.HasProperty("_TintColor")) dropMaterial.SetColor("_TintColor", new Color(0.7f, 0.7f, 0.7f, 0.5f));
            if (dropMaterial.HasProperty("_InvFade")) dropMaterial.SetFloat("_InvFade", 2f);
            dropDrawn.sharedMaterial = dropMaterial;
            drops.Play(false);
            try { FloatingOrigin.RegisterParticleSystem(drops); } catch (Exception) { }
            one = new ParticleSystem.EmitParams();
        }

        /// <summary>
        /// Four wisps of mist on one picture, two by two: each thick in places and gone in others, torn at its edge, none
        /// of them round (noise at four sizes over a round fall-off, the fall-off itself pushed about by the coarsest).
        /// </summary>
        static Texture2D Wisps()
        {
            const int N = 128;
            var t = new Texture2D(2 * N, 2 * N, TextureFormat.RGBA32, true) { name = "Natural Ocean: wisps of mist", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[4 * N * N];
            for (int which = 0; which < 4; which++)
            {
                var r = new System.Random(7 + 31 * which);
                int[] sides = { 4, 7, 13, 25 };
                var grids = new float[4][];
                for (int g = 0; g < 4; g++) { grids[g] = new float[sides[g] * sides[g]]; for (int n = 0; n < grids[g].Length; n++) grids[g][n] = (float)r.NextDouble(); }
                float At(int g, float x, float y)
                {
                    int side = sides[g];
                    x = Mathf.Clamp01(x) * (side - 1); y = Mathf.Clamp01(y) * (side - 1);
                    int i = Mathf.Min((int)x, side - 2), j = Mathf.Min((int)y, side - 2);
                    float a = Mathf.SmoothStep(0f, 1f, x - i), b = Mathf.SmoothStep(0f, 1f, y - j);
                    float[] v = grids[g];
                    return Mathf.Lerp(Mathf.Lerp(v[j * side + i], v[j * side + i + 1], a), Mathf.Lerp(v[(j + 1) * side + i], v[(j + 1) * side + i + 1], a), b);
                }
                int ox = (which & 1) * N, oy = (which >> 1) * N;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                        // (the middle pushed about, so that the wisp is lopsided)
                        float du = u - 0.5f + (At(0, u, v) - 0.5f) * 0.22f, dv = v - 0.5f + (At(0, v, u) - 0.5f) * 0.22f;
                        float d = Mathf.Sqrt(du * du + dv * dv) * 2f;
                        float noise = 0.42f * At(1, u, v) + 0.3f * At(2, u, v) + 0.28f * At(3, u, v);
                        float body = Mathf.Clamp01(1.15f - d * 1.25f);
                        float a = Mathf.Clamp01(body * 1.7f - 1.05f * (1f - noise)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - d) * 2.2f));
                        // (and never to the picture's edge: the next wisp is beside it)
                        float rim = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                        a *= Mathf.SmoothStep(0f, 1f, rim * 12f);
                        px[(oy + y) * 2 * N + ox + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a)));
                    }
            }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>A drop: a small soft dot.</summary>
        static Texture2D Dot()
        {
            const int N = 16;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Natural Ocean: a drop of spray", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f).magnitude * 2f;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(1.25f * (1f - d * d))));
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        float Chance() => (float)random.NextDouble();

        // ---- what comes down into the water (Floating.cs): a crown of spray thrown up round it
        struct Thrown { public Vector3 Where, Up; public float Speed, Wide; }
        static readonly System.Collections.Generic.List<Thrown> thrown = new System.Collections.Generic.List<Thrown>();

        /// <summary>A thing so wide (m) has come down into the water at a place in the scene, so fast (m/s): spray is thrown up round it at the next frame.</summary>
        public static void Splash(Vector3 where, Vector3 up, float speed, float wide)
        {
            if (thrown.Count < 24) thrown.Add(new Thrown { Where = where, Up = up, Speed = speed, Wide = wide });
        }

        void Splashes(Color light)
        {
            for (int n = 0; n < thrown.Count; n++)
            {
                Thrown t = thrown[n];
                // (a ring of water thrown up and outward, higher the faster the thing came down; the middle goes up last and highest)
                int count = Mathf.Clamp((int)(120f + 26f * t.Speed * Mathf.Max(t.Wide, 0.5f)), 120, 700);
                Vector3 a = Vector3.Cross(t.Up, Vector3.right);
                if (a.sqrMagnitude < 0.01f) a = Vector3.Cross(t.Up, Vector3.forward);
                a.Normalize();
                Vector3 b = Vector3.Cross(t.Up, a);
                float rise = Mathf.Clamp(0.45f * t.Speed, 2f, 16f);
                for (int k = 0; k < count; k++)
                {
                    float turn = Chance() * 6.2832f, out_ = Mathf.Sqrt(Chance());
                    Vector3 way = a * Mathf.Cos(turn) + b * Mathf.Sin(turn);
                    one.position = t.Where + way * (out_ * t.Wide) + t.Up * (0.1f + 0.2f * Chance());
                    one.velocity = t.Up * (rise * (0.25f + 1.0f * Chance()) * (1.15f - 0.5f * out_)) + way * ((0.8f + 3.2f * Chance()) * (0.4f + out_) * Mathf.Clamp(0.25f * t.Speed, 0.6f, 3f));
                    one.startSize = (0.05f + 0.1f * Chance()) * Mathf.Clamp(0.7f + 0.2f * t.Wide, 0.8f, 1.6f);
                    one.startLifetime = 0.7f + 1.3f * Chance();
                    one.rotation = 0f;
                    one.startColor = new Color(light.r, light.g, light.b, 0.5f + 0.4f * Chance());
                    drops.Emit(one, 1);
                }
                // (and the mist that hangs where it went in)
                int puffs = Mathf.Clamp(count / 12, 10, 50);
                for (int k = 0; k < puffs; k++)
                {
                    float turn = Chance() * 6.2832f, out_ = Mathf.Sqrt(Chance());
                    Vector3 way = a * Mathf.Cos(turn) + b * Mathf.Sin(turn);
                    one.position = t.Where + way * (out_ * t.Wide) + t.Up * (0.3f + 0.6f * Chance());
                    one.velocity = t.Up * (rise * (0.1f + 0.25f * Chance())) + way * (0.5f + 1.5f * Chance());
                    one.startSize = (0.9f + 1.2f * Chance()) * Mathf.Clamp(0.6f + 0.3f * t.Wide, 0.7f, 1.8f);
                    one.startLifetime = 1.6f + 2.4f * Chance();
                    one.rotation = Chance() * 360f;
                    one.startColor = new Color(light.r, light.g, light.b, 0.09f + 0.08f * Chance());
                    mist.Emit(one, 1);
                }
            }
            thrown.Clear();
        }

        // ---- a craft crashing into the sea: a splash as big as the blow
        // What was read first (2026-10-13: the water entry of bodies, the collapse of the cavity behind them, and the scaling
        // of impact craters and of the waves they send out): a body that hits water fast blasts a cavity out of it; its
        // energy lifts the water it throws out against gravity, so the cavity's radius goes with the fourth root of the
        // energy (a quarter or so of it going into the cavity: R = (E / 4 rho g) ^ 1/4, ten metres for twenty tonnes at
        // 200 m/s, two for a capsule at 30). A crown of water rises round its rim at once, a steep sheet that breaks into
        // strands, about one and a half times the radius high, and falls back. The cavity closes in a second or two (some
        // times sqrt(R / g)), and where its walls meet a jet of water shoots up the middle, two or three radii high, and
        // falls. Mist hangs over it all and drifts with the wind; the water where it went in is churned white. Several
        // parts of one craft coming in together are one blow.
        sealed class Blow
        {
            public Vector3d FromBody;      // where, from the planet's middle (the scene moves under the floating origin)
            public CelestialBody Body;
            public double At;              // when it struck (the game's time)
            public float Energy;           // J
            public Vector3d Velocity;      // how it was going as it struck (the scene's axes), the parts' weighed by their energy
            public float Length;           // m, the craft's length along that way
            public bool Crowned, Jetted;
            public System.Collections.Generic.List<Boil> Boils;   // its white water, made once the crater is open (Churned)
            public Vector2d Way;           // the way the craft came, on the sea's two axes (along it the boils are laid)
            public int Turn;               // (which of the boils are laid this frame, when there are many: Impacts)
            public int JetCount, JetSent;  // the jet's drops, and how many of them are out (it streams out over a moment)
        }

        /// <summary>One event of a blow's white water (see Churned): where it is laid from the blow's middle (m, along the way the
        /// craft came and across it), how far it drifts out while it lasts, when it begins (s after the blow) and how long it is
        /// laid, how wide it is at the end (it grows to that from Opens of it), and how white at first.</summary>
        struct Boil
        {
            public double Along, Across, DriftAlong, DriftAcross;
            public float Born, Life, Wide, Opens, White;
        }
        static readonly System.Collections.Generic.List<Blow> impacts = new System.Collections.Generic.List<Blow>();
        /// <summary>(For the report: the last blow's radius of cavity, crown and jet, m.)</summary>
        public static string LastBlow = "";

        /// <summary>A part has struck the water (the game's crash on splashdown): so heavy (t), so fast (m/s), at a place in the scene.</summary>
        public static void Struck(CelestialBody body, Vector3d where, float tonnes, float speed, Vector3 velocity = default(Vector3), float length = 0f)
        {
            if (body == null || speed < 5f || tonnes <= 0f) return;
            float energy = 0.5f * tonnes * 1000f * speed * speed;
            double now = Planetarium.GetUniversalTime();
            Vector3d from = where - body.position;
            // (the same blow: another part of the same craft coming in within half a second and thirty metres)
            for (int n = 0; n < impacts.Count; n++)
            {
                Blow b = impacts[n];
                if (b.Body == body && !b.Crowned && now - b.At < 0.5 && (b.FromBody - from).sqrMagnitude < 900.0)
                {
                    b.FromBody = (b.FromBody * b.Energy + from * energy) / (b.Energy + energy);
                    b.Velocity = (b.Velocity * b.Energy + (Vector3d)velocity * energy) / (b.Energy + energy);
                    b.Length = Mathf.Max(b.Length, length);
                    b.Energy += energy;
                    return;
                }
            }
            if (impacts.Count < 8) impacts.Add(new Blow { FromBody = from, Body = body, At = now, Energy = energy, Velocity = velocity, Length = length });
        }

        void Impacts(Color light, float wind, float windTo, Vector3d first, Vector3d second)
        {
            double now = Planetarium.GetUniversalTime();
            for (int n = impacts.Count - 1; n >= 0; n--)
            {
                Blow b = impacts[n];
                if (b.Body == null || b.Body != FlightGlobals.currentMainBody || now - b.At > 60.0 || now < b.At) { impacts.RemoveAt(n); continue; }
                float g = (float)(b.Body.GeeASL * 9.80665);
                float R = Mathf.Clamp(Mathf.Pow(b.Energy / (4f * 1000f * g), 0.25f), 0.8f, 60f);
                float crown = Mathf.Min(1.5f * R, 150f), jet = Mathf.Min(2.6f * R, 260f);
                float closes = 2.2f * Mathf.Sqrt(R / g);
                Vector3d world = b.Body.position + b.FromBody;
                Vector3 up = (Vector3)FlightGlobals.getUpAxis(b.Body, world);
                // (on the water's surface there)
                float level = Sea.WaterAt(world, out Vector3 _, out Vector2d place);
                Vector3 at = (Vector3)world + up * (level - (float)b.Body.GetAltitude(world));
                Vector3 a = Vector3.Cross(up, Vector3.right);
                if (a.sqrMagnitude < 0.01f) a = Vector3.Cross(up, Vector3.forward);
                a.Normalize();
                Vector3 c = Vector3.Cross(up, a);
                Vector3 windWay = (Vector3)(first * Math.Cos(windTo) + second * Math.Sin(windTo)) * wind;
                float scale = Mathf.Clamp(R / 4f, 0.6f, 6f);
                if (!b.Crowned && now - b.At >= 0.04)
                {
                    b.Crowned = true;
                    LastBlow = "cavity " + R.ToString("F1") + " m, crown " + crown.ToString("F0") + " m, jet " + jet.ToString("F0") + " m, from " + (b.Energy / 1e6f).ToString("F1") + " MJ";
                    Debug.Log("[Natural Ocean] a craft struck the sea: " + LastBlow);
                    // (No white water yet: the crater stands open for a moment, dark inside the crown (Dents); the water is churned
                    // white as its walls fall back in, below.)
                    // (and the water itself dented: the crater, its walls falling back in and the rings it sends out: Dents)
                    {
                        Vector3d going = b.Velocity;
                        double downward = -Vector3d.Dot(going, (Vector3d)up);
                        var along = new Vector2((float)Vector3d.Dot(going, first), (float)Vector3d.Dot(going, second));
                        float slant = going.sqrMagnitude > 1.0 ? Mathf.Atan2((float)downward, along.magnitude) : 1.5708f;
                        Dents.Struck(b.Body, place, R, along, slant, b.Length);
                    }
                    // (the crown: a steep sheet thrown up and out from the rim, breaking into strands. From a slanting blow its rim is
                    // the crater's, drawn out along the way the craft came (Dents), and most of the water goes forward and low: the
                    // sheet leans downrange, and little is thrown back the way the craft came.)
                    Vector3 goingWay = (Vector3)b.Velocity, flatWay = Vector3.ProjectOnPlane(goingWay, up);
                    float slantDown = goingWay.sqrMagnitude > 1f ? Mathf.Atan2(-Vector3.Dot(goingWay, up), flatWay.magnitude) : 1.5708f;
                    float lying = Mathf.Cos(Mathf.Clamp(slantDown, 0f, 1.5708f));
                    Vector3 ahead = flatWay.sqrMagnitude > 1e-4f ? flatWay.normalized : a;
                    float longer = Mathf.Clamp(1f + 0.9f * lying + 0.5f * lying * Mathf.Min(b.Length, 4f * R) / R, 1f, 3.2f);
                    int count = Mathf.Clamp((int)(400f + 45f * R * R * Mathf.Sqrt(longer)), 400, 7000);
                    for (int k = 0; k < count; k++)
                    {
                        float turn = Chance() * 6.2832f;
                        Vector3 way = a * Mathf.Cos(turn) + c * Mathf.Sin(turn);
                        float forward = Vector3.Dot(way, ahead);
                        // (more of it forward: a share of the drops thrown back the way the craft came are left out)
                        if (Chance() > (1f + 0.8f * lying * forward) / (1f + 0.8f * lying)) continue;
                        // (a crown flares outward as it rises, like a tulip: fifty to seventy-five degrees; lower, forward, from a slant)
                        float steep = (50f + 25f * Chance() - 25f * lying * Mathf.Max(forward, 0f)) * Mathf.Deg2Rad;
                        float rise = Mathf.Sqrt(2f * g * crown) * (0.45f + 0.6f * Chance());
                        float speed = rise / Mathf.Sin(steep);
                        Vector3 rimAt = way * (R * (0.75f + 0.45f * Chance())) + ahead * (forward * (longer - 1f) * R + 0.35f * R * lying);
                        one.position = at + rimAt + up * (0.2f * Chance() * scale);
                        one.velocity = up * rise + way * (speed * Mathf.Cos(steep) * (0.7f + 0.6f * Chance())) + ahead * (speed * 0.35f * lying) + windWay * 0.2f;
                        one.startSize = (0.06f + 0.14f * Chance()) * scale;
                        one.startLifetime = 2f * rise / g + 0.4f;
                        one.rotation = 0f;
                        // (a thin sheet tearing into strands: seen through, not a white wall; and each strand lit a little more or less)
                        float lit = 0.72f + 0.28f * Chance();
                        one.startColor = new Color(light.r * lit, light.g * lit, light.b * lit, 0.2f + 0.35f * Chance());
                        drops.Emit(one, 1);
                    }
                    Puffs(at, up, a, c, R, R * 1.1f, crown * 0.6f, Mathf.Clamp((int)(50f + 5f * R), 50, 260), light, windWay);
                }
                if (b.Crowned && !b.Jetted && now - b.At >= closes)
                {
                    b.Jetted = true;
                    // (fewer drops than the crown: thrown all together they were a white pillar; and fewer still from a slanting
                    // craft, whose cavity falls in less evenly and sends up a weaker jet)
                    Vector3 cameIn = (Vector3)b.Velocity, cameFlat = Vector3.ProjectOnPlane(cameIn, up);
                    float cameLying = cameIn.sqrMagnitude > 1f ? Mathf.Cos(Mathf.Clamp(Mathf.Atan2(-Vector3.Dot(cameIn, up), cameFlat.magnitude), 0f, 1.5708f)) : 0f;
                    b.JetCount = Mathf.RoundToInt(Mathf.Clamp(120f + 12f * R * R, 120f, 2000f) * (1f - 0.5f * cameLying * cameLying));
                    Puffs(at, up, a, c, R, R * 0.4f, jet * 0.8f, Mathf.Clamp((int)(40f + 4f * R), 40, 220), light, windWay);
                }
                // (The jet: where the cavity's walls meet, water comes up the middle, and goes on coming for a moment while the
                // walls run in, the first of it the fastest: a column that grows up out of the water, its tip highest, few of
                // its drops reaching the top. Thrown all in one frame it stood for a second as a white block. From a slanting
                // craft it comes back out along the cavity the craft dug, leaning back the way the craft came. The water
                // churned white: below.)
                if (b.Jetted && b.JetSent < b.JetCount)
                {
                    float jetFor = Mathf.Max(0.3f * closes, 0.15f);
                    int upTo = Mathf.Min(b.JetCount, Mathf.CeilToInt(b.JetCount * Mathf.Clamp01((float)(now - b.At - closes) / jetFor)));
                    Vector3 goingWay = (Vector3)b.Velocity, flatWay = Vector3.ProjectOnPlane(goingWay, up);
                    float slantDown = goingWay.sqrMagnitude > 1f ? Mathf.Atan2(-Vector3.Dot(goingWay, up), flatWay.magnitude) : 1.5708f;
                    float lying = Mathf.Cos(Mathf.Clamp(slantDown, 0f, 1.5708f));
                    Vector3 ahead = flatWay.sqrMagnitude > 1e-4f ? flatWay.normalized : a;
                    // (leaning back by up to some thirty degrees, as much as the craft came in slanting)
                    float lean = 0.5f * lying * lying;
                    Vector3 column = (up * Mathf.Cos(lean) - ahead * Mathf.Sin(lean)).normalized;
                    Vector3 foot = at + ahead * (0.35f * R * lying);
                    for (int k = b.JetSent; k < upTo; k++)
                    {
                        float when = k / (float)b.JetCount;
                        float turn = Chance() * 6.2832f, out_ = Mathf.Sqrt(Chance());
                        Vector3 way = a * Mathf.Cos(turn) + c * Mathf.Sin(turn);
                        float rise = Mathf.Sqrt(2f * g * jet) * (0.3f + 0.7f * (1f - when) * (1f - when) * (0.6f + 0.4f * Chance()));
                        // (thick at its foot and thinning upward: the slow water spreads, the fast keeps to the middle)
                        float slow = 1f - rise / Mathf.Sqrt(2f * g * jet);
                        one.position = foot + way * (R * (0.18f + 0.22f * slow) * out_) + up * (0.3f * Chance() * scale);
                        one.velocity = column * rise + way * (rise * (0.04f + 0.18f * out_ * (0.5f + slow))) + windWay * 0.15f;
                        one.startSize = (0.08f + 0.2f * Chance()) * scale;
                        one.startLifetime = 2f * rise * Mathf.Cos(lean) / g + 0.3f;
                        one.rotation = 0f;
                        // (a column of drops and strands, seen through at its edges: so many at once, as bright as these were, it was a white pillar)
                        // (and not one flat white: each drop lit a little more or less, the slow heavy water at its foot, much of it in
                        // the shade of the rest, dimmer and thinner)
                        float lit = (0.72f + 0.28f * Chance()) * (1f - 0.25f * slow);
                        one.startColor = new Color(light.r * lit, light.g * lit, light.b * lit, (0.16f + 0.3f * Chance()) * (1f - 0.4f * out_) * (1f - 0.45f * slow));
                        drops.Emit(one, 1);
                    }
                    b.JetSent = upTo;
                }
                // ---- The water churned white (Churned). Each of its events is laid in the white water's maps while it lasts, as a
                // hull's churn is, and then left there to thin by the maps' own time, into lace and then nothing. At most nine
                // a frame from one blow (the maps take a dozen marks a frame in all): when more are going, they take turns, and
                // a mark missed for a frame has barely faded.
                float age = (float)(now - b.At);
                if (b.Crowned && b.Boils == null) b.Boils = Churned(b, R, crown, jet, closes, g, first, second, up);
                float churnFor = 0f;
                if (b.Boils != null)
                {
                    Vector2d way = b.Way, side = new Vector2d(-way.y, way.x);
                    int going = 0;
                    for (int k = 0; k < b.Boils.Count; k++)
                    {
                        Boil o = b.Boils[k];
                        churnFor = Mathf.Max(churnFor, o.Born + o.Life);
                        if (age >= o.Born && age < o.Born + o.Life) going++;
                    }
                    int every = Mathf.Max(1, Mathf.CeilToInt(going / 9f)), seen = 0;
                    b.Turn++;
                    for (int k = 0; k < b.Boils.Count; k++)
                    {
                        Boil o = b.Boils[k];
                        float t = age - o.Born;
                        if (t < 0f || t >= o.Life) continue;
                        if ((seen++ + b.Turn) % every != 0) continue;
                        float f = t / o.Life;
                        // (it wells up and spreads, quickly at first; the froth on it thins as it goes)
                        float spread = 1f - Mathf.Exp(-3f * f);
                        double al = o.Along + o.DriftAlong * spread, ac = o.Across + o.DriftAcross * spread;
                        Foam.Mark(place + way * al + side * ac, o.Wide * Mathf.Lerp(o.Opens, 1f, Mathf.Sqrt(f)), o.White * (1f - 0.45f * Mathf.Pow(f, 0.7f)));
                    }
                }
                if (b.Crowned && b.Jetted && (b.Boils == null || age >= churnFor)) impacts.RemoveAt(n);
            }
        }

        /// <summary>
        /// A blow's white water, as the events it is made of. (The user, of the first try: "there should be a lot more white
        /// water, second scene there was none other than the particle splash". The second try laid one patch, evenly, every
        /// frame for half a minute: a flat white disc, the maps' lace too fine to show in it from a little way off.)
        ///
        /// What makes it, as read (the water entry of bodies; underwater blasts and the bubble plumes of breaking waves;
        /// photographs from above of bomb splashes round a ship): the crater's walls running back in, the jet coming down in the
        /// middle and the crown coming down round about all beat air into the water, each at its own time and place. The air
        /// goes down as a cloud of bubbles under where the crater was (along the furrow of a slanting blow) and comes back up
        /// for half a minute and more: not evenly but in boils, here and there, each a dome of froth that wells up, spreads
        /// and thins while others break out beside it, fewer, weaker and further out as the cloud rises and spreads. Between
        /// them the water is milky green with the bubbles under it (the sea's shader does that where the white is). So: the
        /// walls' white round the crater, the jet's in the middle, a ring of the crown's splashes, and the boils; each laid
        /// for a moment and then left to the maps, which thin it into lace. Made once for each blow, by its own chance.
        /// </summary>
        static System.Collections.Generic.List<Boil> Churned(Blow b, float R, float crown, float jet, float closes, float g, Vector3d first, Vector3d second, Vector3 up)
        {
            var alongSea = new Vector2d(Vector3d.Dot(b.Velocity, first), Vector3d.Dot(b.Velocity, second));
            double downward = -Vector3d.Dot(b.Velocity, (Vector3d)up);
            float lying = b.Velocity.sqrMagnitude > 1.0 ? (float)Math.Cos(Math.Min(Math.Atan2(downward, alongSea.magnitude), Math.PI / 2)) : 0f;
            float stretch = Mathf.Clamp(1f + 0.9f * lying + 0.5f * lying * Mathf.Min(b.Length, 4f * R) / R, 1f, 3.2f);
            b.Way = alongSea.magnitude > 1e-3 ? alongSea / alongSea.magnitude : new Vector2d(1.0, 0.0);
            var rnd = new System.Random(unchecked((int)(b.At * 1009.0) ^ (int)b.Energy));
            float F() => (float)rnd.NextDouble();
            var list = new System.Collections.Generic.List<Boil>();
            // (where the crater was deepest: a third of a radius downrange of where it struck, as Dents digs it)
            float ahead = 0.35f * lying * R;
            // ---- the walls running back in: white round the crater's rim, drawn in with them, from a little before the jet
            int walls = Mathf.Clamp(Mathf.RoundToInt(7f * Mathf.Sqrt(stretch)), 6, 13);
            for (int k = 0; k < walls; k++)
            {
                double turn = (k + 0.7 * F()) / walls * 2.0 * Math.PI, far = (0.75 + 0.35 * F()) * R;
                list.Add(new Boil
                {
                    Along = Math.Cos(turn) * far * stretch + ahead, Across = Math.Sin(turn) * far,
                    DriftAlong = -Math.Cos(turn) * 0.3 * R * stretch, DriftAcross = -Math.Sin(turn) * 0.3 * R,
                    Born = (0.4f + 0.35f * F()) * closes, Life = (0.45f + 0.4f * F()) * closes + 0.3f,
                    Wide = (0.35f + 0.25f * F()) * R, Opens = 0.6f, White = 0.82f + 0.12f * F()
                });
            }
            // ---- the jet coming down in the middle: most of its water a second or three after it went up
            float aloft = Mathf.Sqrt(2f * jet / g);
            int falls = Mathf.Clamp(Mathf.RoundToInt(5f * Mathf.Sqrt(stretch)), 4, 9);
            for (int k = 0; k < falls; k++)
            {
                double turn = F() * 2.0 * Math.PI, far = Math.Sqrt(F()) * 0.55 * R;
                list.Add(new Boil
                {
                    Along = Math.Cos(turn) * far * stretch + ahead, Across = Math.Sin(turn) * far,
                    DriftAlong = Math.Cos(turn) * 0.5 * R, DriftAcross = Math.Sin(turn) * 0.5 * R,
                    Born = closes + (0.5f + 1.1f * F()) * aloft, Life = 0.6f + 0.8f * F() + 0.08f * R,
                    Wide = (0.5f + 0.35f * F()) * R, Opens = 0.5f, White = 0.88f + 0.1f * F()
                });
            }
            // ---- the crown coming down round the crater, a few seconds in: a broken ring of splashes, one to three radii
            // out, thrown furthest and most of it downrange from a slant (as its drops are thrown)
            float crownAloft = Mathf.Sqrt(2f * crown / g);
            int ring = Mathf.Clamp(Mathf.RoundToInt(16f * Mathf.Sqrt(stretch)), 14, 30);
            for (int k = 0; k < ring; k++)
            {
                double turn = (k + F()) / ring * 2.0 * Math.PI;
                float forward = (float)Math.Cos(turn);
                if (F() > (1f + 0.8f * lying * forward) / (1f + 0.8f * lying)) continue;
                double far = (1.4 + 1.4 * F() * F()) * R + Math.Max(forward, 0f) * lying * R;
                list.Add(new Boil
                {
                    Along = Math.Cos(turn) * (far + (stretch - 1f) * R) + ahead, Across = Math.Sin(turn) * far,
                    DriftAlong = Math.Cos(turn) * 0.25 * R, DriftAcross = Math.Sin(turn) * 0.25 * R,
                    Born = (1.0f + 0.7f * F()) * crownAloft, Life = 0.35f + 0.5f * F(),
                    Wide = (0.18f + 0.3f * F()) * R, Opens = 0.7f, White = 0.62f + 0.25f * F()
                });
            }
            // ---- the boils: the air coming back up for half a minute and more (longer for a bigger blow), most of it early;
            // a boil wells up within a radius or so of the middle at first and further out later, spreads out and drifts
            // away from the middle as it goes, and is less white the later it comes
            float churnFor = Mathf.Clamp(14f + 2.5f * R, 14f, 48f);
            int boils = Mathf.Clamp(Mathf.RoundToInt((22f + 1.2f * R) * Mathf.Sqrt(stretch)), 20, 90);
            float from = 0.9f * closes, until = Mathf.Max(churnFor - 5f, from + 4f);
            for (int k = 0; k < boils; k++)
            {
                float when = Mathf.Pow(F(), 1.8f);
                double turn = F() * 2.0 * Math.PI, far = Math.Sqrt(F()) * R * (0.5 + 1.0 * Math.Sqrt(when));
                double out_ = (0.3 + 0.7 * F()) * R;
                list.Add(new Boil
                {
                    Along = Math.Cos(turn) * far * stretch + ahead, Across = Math.Sin(turn) * far,
                    DriftAlong = Math.Cos(turn) * out_ * stretch, DriftAcross = Math.Sin(turn) * out_,
                    Born = from + when * (until - from), Life = (2.5f + 3.5f * F()) * Mathf.Clamp(Mathf.Sqrt(R / 4f), 0.7f, 2f),
                    Wide = (0.3f + 0.4f * F()) * R, Opens = 0.35f, White = (0.62f + 0.25f * F()) * (1f - 0.45f * when)
                });
            }
            return list;
        }

        /// <summary>Mist over a blow: soft puffs from round about its middle up to so high, rising a little and drifting with the wind.</summary>
        void Puffs(Vector3 at, Vector3 up, Vector3 a, Vector3 c, float R, float spread, float high, int count, Color light, Vector3 windWay)
        {
            for (int k = 0; k < count; k++)
            {
                float turn = Chance() * 6.2832f, out_ = Mathf.Sqrt(Chance());
                Vector3 way = a * Mathf.Cos(turn) + c * Mathf.Sin(turn);
                one.position = at + way * (out_ * spread) + up * (high * Chance());
                one.velocity = up * (0.5f + 2.5f * Chance()) + way * (0.5f + 2f * Chance()) + windWay * 0.6f;
                one.startSize = (0.5f + 1f * Chance()) * Mathf.Clamp(R * 0.6f, 1.2f, 30f);
                one.startLifetime = 5f + 10f * Chance();
                one.rotation = Chance() * 360f;
                one.startColor = new Color(light.r, light.g, light.b, 0.07f + 0.09f * Chance());
                mist.Emit(one, 1);
            }
        }

        /// <summary>How much spray (1 as the wind has it; 0: none).</summary>
        public static float Much = 1f;
        /// <summary>(For finding faults: the light the puffs were last given.)</summary>
        public static Color LastLight;

        /// <summary>
        /// Once a frame, after the white water has been worked out. `at`: the point of the sea under the camera on the sea's
        /// two axes; `mid` that point in the scene with its axes; `wind` m/s blowing the waves towards `windTo` (radians from
        /// the first axis); `high`: the camera above the sea; `light`: what white things on the sea are lit with just now.
        /// </summary>
        public void Step(Foam foam, Waves waves, Vector2d at, Vector3d mid, Vector3d first, Vector3d second, Vector3d up, float wind, float windTo, float high, Color light, float seconds)
        {
            Alive = mist.particleCount + drops.particleCount;
            LastLight = light;
            if (thrown.Count > 0 && Much > 0f) Splashes(light); else thrown.Clear();
            if (impacts.Count > 0) Impacts(light, wind, windTo, first, second);
            counted += seconds;
            if (counted > 1f) { Bursts = burstsNow; burstsNow = 0; counted = 0f; }
            // (Any breaking water throws some spray; from a strong breeze the wind takes the tops off as well, and from a gale
            // it is all it can be.)
            float strength = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((wind - 9f) / 9f)) * Much;
            // (In a storm the tops of the breaking seas are torn off and driven down the wind: spindrift, veils of it streaming
            // from every breaking crest, more of it, larger, longer in the air and nearly as fast as the wind.)
            float storm = Mathf.Clamp01((wind - 16f) / 10f);
            if (strength <= 0f || foam == null || seconds <= 0f || high > 600f) return;
            Unity.Collections.NativeArray<Color32> seen = foam.Seen(out bool there);
            if (!there) return;
            Vector3 down = (Vector3)(first * Math.Cos(windTo) + second * Math.Sin(windTo));
            Vector3 blown = down * wind;
            Vector3 upward = (Vector3)up;
            Vector3 crest = (Vector3)(first * -Math.Sin(windTo) + second * Math.Cos(windTo));
            int across = Foam.SeenAcross;
            // (the three finer maps, each for the water the finer ones do not reach, and not out to its own rim, which is stale)
            for (int l = 0; l < 3; l++)
            {
                double reach = 0.38 * Foam.Across * foam.Texel[l], within = l > 0 ? 0.38 * Foam.Across * foam.Texel[l - 1] : -1.0;
                // (A point stands for a square metre of the finest map, sixteen of the next, 256 of the third, of which the
                // breaking front is a strip: so many drops and so many wisps a second from each. The waves a map holds the
                // fronts of are of a size: how fast they run, how long a lip they throw.)
                float perDrops = (l == 0 ? 260f : l == 1 ? 650f : 0f) * strength * seconds * (1f + 0.8f * storm);
                // (the mist is the lesser part: where the fall comes down, and what the wind carries on from there. Made the greater, a
                // breaking crest wore a bank of fog.)
                float perMist = (l == 0 ? 10f : l == 1 ? 26f : 50f) * strength * seconds * (1f + 1.5f * storm);
                float large = l == 0 ? 0.9f : l == 1 ? 1.9f : 3.4f;
                for (int j = 0; j < across; j++)
                    for (int i = 0; i < across; i++)
                    {
                        Color32 c = seen[j * across + i];
                        // (breaking this moment or within a few frames of it: the white is 1 there and falls from it at once)
                        if ((l == 0 ? c.r : l == 1 ? c.g : c.b) < 236) continue;
                        float wide = foam.SeenWhere(l, i, j, out double x, out double z);
                        double far = Math.Max(Math.Abs(x - at.x), Math.Abs(z - at.y));
                        if (far > reach || far < within) continue;
                        // (drops are seen as drops only from near by: from far off they were hairs standing on the horizon)
                        int some = l == 2 || far > 70.0 ? 0 : (int)(perDrops + Chance()), puffs = (int)(perMist + Chance());
                        if (some <= 0 && puffs <= 0) continue;
                        burstsNow++;
                        // (which size of wave is breaking here says how fast its crest runs and how long a lip it throws:
                        // the sea's shader throws the same, see lipShape there)
                        waves.BreakingAt(x, z, out Vector3 _, out int which);
                        float runs = Mathf.Sqrt(9.81f * Waves.Sides[which] / 35.2f);
                        float lip = Mathf.Clamp(1.15f * Mathf.Abs(Sea.Lip) * waves.Patch[which].Height, 0.12f, 3f);
                        for (int k = 0; k < some; k++)
                        {
                            double px = x + (Chance() - 0.5) * wide, pz = z + (Chance() - 0.5) * wide;
                            waves.BreakingAt(px, pz, out Vector3 moved);
                            // (off the lip's edge, which is ahead of where the waves' own numbers have the crest, and a little under its top)
                            float ahead = lip * (0.75f + 0.4f * Chance());
                            one.position = (Vector3)(mid + first * (px - at.x + moved.x) + second * (pz - at.y + moved.z) + up * (moved.y - lip * (0.05f + 0.25f * Chance()))) + down * ahead;
                            // (forward, level or nearly, faster than the wave: then it is water falling)
                            float lifts = Chance();
                            one.velocity = down * (runs * (1.1f + 0.5f * Chance())) + upward * ((-0.3f + 1.6f * lifts * lifts) * Mathf.Sqrt(lip)) + crest * ((Chance() - 0.5f) * 0.8f) + blown * 0.08f;
                            one.startSize = (0.04f + 0.07f * Chance()) * Mathf.Clamp(Mathf.Sqrt(lip), 0.6f, 1.6f);
                            one.startLifetime = (0.35f + 0.5f * Chance()) * Mathf.Clamp(Mathf.Sqrt(lip) * 1.2f, 0.6f, 1.7f);
                            one.rotation = 0f;
                            one.startColor = new Color(Mathf.Min(light.r * 1.25f, 1f), Mathf.Min(light.g * 1.25f, 1f), Mathf.Min(light.b * 1.25f, 1f), 0.4f + 0.45f * Chance());
                            drops.Emit(one, 1);
                        }
                        for (int k = 0; k < puffs; k++)
                        {
                            double px = x + (Chance() - 0.5) * wide, pz = z + (Chance() - 0.5) * wide;
                            waves.BreakingAt(px, pz, out Vector3 moved);
                            one.position = (Vector3)(mid + first * (px - at.x + moved.x) + second * (pz - at.y + moved.z) + up * (moved.y + large * (0.15f + 0.35f * Chance())));
                            // (the wind's from the first: a wisp is water so fine that the air carries it)
                            // (it begins where the fall comes down, ahead of the lip and below it)
                            one.position += down * (lip * (0.8f + 1.2f * Chance())) - upward * (lip * 0.3f);
                            one.velocity = blown * (0.4f + 0.3f * Chance() + 0.15f * storm) + down * (runs * 0.5f) + upward * ((0.1f + 0.9f * Chance() * Chance()) * (1f + 0.5f * l)) + crest * ((Chance() - 0.5f) * 1.2f);
                            one.startSize = (0.7f + 0.8f * Chance()) * large * (1f + 0.5f * storm);
                            one.startLifetime = (2.0f + 2.5f * Chance()) * (1f + 0.4f * storm);
                            one.rotation = Chance() * 360f;
                            // (white and close where it is torn off, thinning as it spreads)
                            // (Each thin, and many: a few thick ones were cotton balls standing on the sea when looked at along the wind.)
                            one.startColor = new Color(light.r, light.g, light.b, Mathf.Min((0.14f + 0.12f * Chance()) * (0.6f + 0.4f * Mathf.Min(strength, 1f)), 0.9f));
                            mist.Emit(one, 1);
                        }
                    }
            }
        }

        public void Dispose()
        {
            try { FloatingOrigin.UnregisterParticleSystem(mist); } catch (Exception) { }
            try { FloatingOrigin.UnregisterParticleSystem(drops); } catch (Exception) { }
            if (mistHolder != null) UnityEngine.Object.Destroy(mistHolder);
            if (dropHolder != null) UnityEngine.Object.Destroy(dropHolder);
            if (mistMaterial != null) UnityEngine.Object.Destroy(mistMaterial);
            if (dropMaterial != null) UnityEngine.Object.Destroy(dropMaterial);
            if (wisps != null) UnityEngine.Object.Destroy(wisps);
            if (dot != null) UnityEngine.Object.Destroy(dot);
        }
    }
}
