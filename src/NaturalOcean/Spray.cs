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
