using Unity.Collections;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace NaturalOcean
{
    /// <summary>
    /// The near fade (2026-11-08): a puff of mist or a torn sheet of spray thinned out as it comes near the camera, by how near
    /// it is for its size, to nothing before it reaches the lens. Drawn whole up to the lens, a hull's puff passing through
    /// the camera was there one frame and gone the next: a flash on 24 % of the picture in seaplane1off (TODO item 20). Unity's
    /// own particle shader's camera fade was tried first and does nothing in the game's build of it (the same item).
    ///
    /// Each one's alpha as it was let go is kept in the lowest byte of its random seed (Spray.Let), so that each frame it is
    /// thinned from that and never from what it was thinned to the frame before; the system's own particle job does it, after
    /// the system's own update and before it is drawn. Gone within a quarter metre and half its size of the camera, whole from
    /// 2 m and one and a half times its size, eased between: a puff 1.2 m across from 3.8 m in to nothing at 0.85 m, seven
    /// frames at 60 a second for a camera running 25 m/s through it. (Its size as it is drawn that frame: the job is handed
    /// each one's size with its size over its life already in it, 2.88 for a puff let go 2.88 across when it is a third through
    /// a life whose curve is 1 there, 1.01 a twentieth through, where the curve is a third.) What it costs, on a worker while
    /// the game's own LateUpdate goes on (development build, its counts and clock in it as well): 0.48 ms the longest for
    /// 2,000 puffs, 0.57 ms for 6,000, 1.28 ms for 15,000, the mist's most being 16,000.
    ///
    /// Tried (development build, by night, the puffs lit white against the black sky): a puff 1.0 m across held in front of
    /// the camera added nothing to the picture at 0.5 m, 2 levels at 0.8 m and 1.2 m, where without the fade it added 93, 71
    /// and 168; five flying through the camera at 15 m/s, two flashes and a jump in the motion tool without it (26 and 19 %
    /// of the picture), none with it; behind the Gull ploughing and planing, no flashes or jumps.
    /// </summary>
    sealed class NearFade : MonoBehaviour
    {
        ParticleSystem system;
        /// <summary>(Off: each drawn as it was let go, near or not; development build, Sea.SprayNear.)</summary>
        public static bool On = true;
#if DEV
        NativeArray<float> seen;
        static readonly System.Collections.Generic.Dictionary<string, string> seenBy = new System.Collections.Generic.Dictionary<string, string>();
        // (the longest a job took since the last report, and how many it worked then: for what the fade costs)
        static readonly System.Collections.Generic.Dictionary<string, float[]> worstBy = new System.Collections.Generic.Dictionary<string, float[]>();
        /// <summary>(Development build: what each system's job saw last, and the longest one took since the last asking, for Sea.SprayNear.)</summary>
        public static string Seen()
        {
            var b = new System.Text.StringBuilder();
            foreach (var kv in seenBy)
            {
                if (b.Length > 0) b.Append("; ");
                b.Append(kv.Value);
                if (worstBy.TryGetValue(kv.Key, out float[] w)) { b.Append("; the longest job since last asked ").Append(w[0].ToString("F3")).Append(" ms, working ").Append(w[1].ToString("F0")); w[0] = 0f; w[1] = 0f; }
            }
            return b.ToString();
        }
#endif

        /// <summary>Put on a spray system's own object.</summary>
        public static void Put(ParticleSystem on)
        {
            NearFade f = on.gameObject.AddComponent<NearFade>();
            f.system = on;
#if DEV
            f.seen = new NativeArray<float>(9, Allocator.Persistent);
#endif
        }

        // (Where the camera will be drawn from this frame: the particles are worked out before the game's LateUpdate moves the
        // camera on, so it is put on as far as it went the frame before. A jump, as when the world is moved back to the origin,
        // is not carried on.)
        static int eyeFrame = -1;
        static bool eyeThere;
        static Vector3 eye, eyeLast;
        static Vector3 Eye(out bool there)
        {
            if (eyeFrame != Time.frameCount)
            {
                eyeFrame = Time.frameCount;
                Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
                if (cam == null) cam = Camera.main;
                eyeThere = cam != null;
                if (eyeThere)
                {
                    Vector3 now = cam.transform.position, step = now - eyeLast;
                    eyeLast = now;
                    eye = step.sqrMagnitude < 30f * 30f ? now + step : now;
                }
            }
            there = eyeThere;
            return eye;
        }

        void OnParticleUpdateJobScheduled()
        {
            if (system == null || system.particleCount == 0) return;
#if DEV
            if (seen.IsCreated && seen[3] > 0f)
            {
                if (!worstBy.TryGetValue(system.name, out float[] w)) worstBy[system.name] = w = new float[2];
                if (seen[8] > w[0]) { w[0] = seen[8]; w[1] = seen[3]; }
            }
            if (seen.IsCreated && seen[3] > 0f)
                seenBy[system.name] = system.name + ": " + seen[3].ToString("F0") + " worked, " + seen[1].ToString("F0") + " thinned, the nearest " +
                                      seen[2].ToString("F2") + " m, " + seen[4].ToString("F3") + " m across, at " + seen[6].ToString("F2") + " % of its life, thinned to " +
                                      seen[5].ToString("F3") + " of its alpha " + seen[7].ToString("F0") + "; the oldest at " + seen[0].ToString("F2") + " %";
#endif
            Vector3 e = Eye(out bool there);
            new Thin
            {
                Eye = e, On = On && there,
#if DEV
                Seen = seen,
#endif
            }.Schedule(system);
        }

        void OnDestroy()
        {
#if DEV
            if (seen.IsCreated) seen.Dispose();
#endif
        }

        struct Thin : IJobParticleSystem
        {
            public Vector3 Eye;
            public bool On;
#if DEV
            public NativeArray<float> Seen;
#endif

            public void Execute(ParticleSystemJobData data)
            {
#if DEV
                long began = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                int n = data.count;
                NativeArray<Color32> colours = data.startColors;
                NativeArray<uint> seeds = data.randomSeeds;
                NativeArray<float> px = data.positions.x, py = data.positions.y, pz = data.positions.z;
                // (each one's size as it is drawn this frame; sizes.y and .z are not there for particles of one size)
                NativeArray<float> sizes = data.sizes.x;
#if DEV
                NativeArray<float> ages = data.aliveTimePercent;
                float oldest = 0f, nearest = 1e9f; int thinned = 0;
#endif
                for (int i = 0; i < n; i++)
                {
                    byte own = (byte)(seeds[i] & 0xFFu), a = own;
#if DEV
                    if (ages[i] > oldest) oldest = ages[i];
#endif
                    if (On && own > 0)
                    {
                        float dx = px[i] - Eye.x, dy = py[i] - Eye.y, dz = pz[i] - Eye.z;
                        float d2 = dx * dx + dy * dy + dz * dz;
                        float s = sizes[i], whole = 2f + 1.5f * s;
                        float f = 1f;
                        if (d2 < whole * whole)
                        {
                            float gone = 0.25f + 0.5f * s, d = Mathf.Sqrt(d2);
                            f = Mathf.Clamp01((d - gone) / (whole - gone));
                            f = f * f * (3f - 2f * f);
                            a = (byte)(own * f + 0.5f);
                        }
#if DEV
                        if (a != own) thinned++;
                        if (d2 < nearest * nearest) { nearest = Mathf.Sqrt(d2); Seen[4] = s; Seen[5] = f; Seen[6] = ages[i]; Seen[7] = own; }
#endif
                    }
                    Color32 c = colours[i];
                    if (c.a != a) { c.a = a; colours[i] = c; }
                }
#if DEV
                if (Seen.IsCreated)
                {
                    Seen[0] = oldest; Seen[1] = thinned; Seen[2] = nearest; Seen[3] = n;
                    Seen[8] = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                }
#endif
            }
        }
    }
}
