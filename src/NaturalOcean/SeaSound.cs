using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalOcean
{
    /// <summary>
    /// (Since 0.4.0.) What the sea sounds like, where the mod has its recordings (Sonniss's libraries, docs/sounds-plan.md; the
    /// mod's one packed file, read by Keystone's Sounds, Keystone 0.10.0):
    ///
    ///   * near its surface, the sea all round: calm water lapping, a rough sea, a storm's heavy waves and a huge sea, crossfaded by
    ///     how high the waves stand; over shallow water near a shore, the surf of a coast, and now and then a wave breaking there;
    ///     from higher up, the sea far off, which fades away by 600 m. Nothing over land: the sea is not heard inland;
    ///   * under water, its hush in place of all that, and a splash going in;
    ///   * something heavy striking the water (Spray.Struck): a splash from where it struck, as loud as the blow, and as late as
    ///     sound takes to come from there.
    ///
    /// All are asked for at the first use, so that none waits for its sound when it is wanted. Without the recordings (a
    /// repository's build) or with an older Keystone, the sea is silent, as before 0.4.0.
    /// </summary>
    static class SeaSound
    {
        const string Mod = "NaturalOcean";
        static bool asked, gone;
        static readonly List<string> names = new List<string>();
        static GameObject holder;
        static readonly Dictionary<string, AudioSource> loops = new Dictionary<string, AudioSource>();
        static readonly Dictionary<string, float> wanted = new Dictionary<string, float>();
        static readonly List<AudioSource> shots = new List<AudioSource>();
        static bool wasUnder;
        static int lastSplash = -1, lastSurf = -1;
        static float surfIn = 3f;
        static readonly List<string> ended = new List<string>();
        // (the loops faded out and paused, and since when: see Loops)
        static readonly Dictionary<string, float> resting = new Dictionary<string, float>();

        static void Ask()
        {
            if (asked) return;
            asked = true;
            try { AskAll(); }
            catch (Exception) { gone = true; }      // (a Keystone older than 0.10.0: no Sounds there)
        }

        static void AskAll()
        {
            foreach (string name in Keystone.Sounds.Names(Mod))
            {
                names.Add(name);
                // (the loops of the sea all round, one source each, streamed; splashes and breaking waves whole, as two may sound at once)
                if (name.StartsWith("sea-", StringComparison.Ordinal) || name == "under-ambience") Keystone.Sounds.Stream(Mod, name, _ => { });
                else Keystone.Sounds.Get(Mod, name, _ => { });
            }
        }

        // (those found kept here: asked for every frame until it is in, and a name put together for Keystone each time was garbage)
        static readonly Dictionary<string, AudioClip> found = new Dictionary<string, AudioClip>();

        static AudioClip Clip(string name)
        {
            if (found.TryGetValue(name, out AudioClip known) && known != null) return known;
            Ask();
            if (gone || names.Count == 0) return null;
            AudioClip clip;
            try { clip = Ready(name); }
            catch (Exception) { gone = true; return null; }
            if (clip != null) found[name] = clip;
            return clip;
        }

        static AudioClip Ready(string name) => Keystone.Sounds.Ready(Mod, name);

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>
        /// (Each frame the sea is drawn.) The camera so high over the sea (m), the waves standing so high (m), the ground under
        /// the camera against the sea's level (m: less than nothing, the sea's depth there), and whether the camera is under the
        /// water.
        /// </summary>
        public static void Step(float high, float waves, float ground, bool under, Vector3 camAt)
        {
            if (Hush.Paused) return;     // (docs/sound-rules.md, B1: nothing started or changed while the game is paused)
            Ask();
            if (gone || names.Count == 0) return;
            float vol = GameSettings.AMBIENCE_VOLUME * (TimeWarp.CurrentRate > 4.1f ? 0f : 1f);
            if (under)
            {
                if (!wasUnder) Shot("under-splash", camAt, 0.7f * GameSettings.SHIP_VOLUME, 1f, false);
                Want("under-ambience", 0.6f * vol);
            }
            else if (ground < 2f)
            {
                // (near the surface the sea all round, by how high its waves stand; from higher up the sea far off)
                float near = 1f - Smooth(15f, 250f, high), far = Smooth(30f, 90f, high) * (1f - Smooth(300f, 650f, high));
                float huge = Smooth(4f, 7f, waves), storm = Smooth(1.8f, 3.5f, waves) * (1f - huge), calm = 1f - Smooth(0.4f, 1.2f, waves);
                float rough = Mathf.Max(0f, 1f - calm - storm - huge);
                // (the surf of a coast where the water under the camera is shallow: within a few metres of its depth)
                float coast = 1f - Smooth(2f, 10f, -ground);
                float open = 1f - coast;
                float n = 0.65f * vol * near;
                Want("sea-calm", n * Mathf.Sqrt(calm * open));
                Want("sea-rough", n * Mathf.Sqrt(rough * open));
                Want("sea-storm", n * Mathf.Sqrt(storm * open));
                Want("sea-huge", n * Mathf.Sqrt(huge * open));
                Want("sea-coast", n * Mathf.Sqrt(coast));
                Want("sea-distant", 0.5f * vol * far);
                // (and now and then a wave breaking on the shore: big ones in a rough sea. The game's time, so none while paused)
                float surf = coast * near;
                if (surf > 0.3f && (surfIn -= Mathf.Min(Time.deltaTime, Time.unscaledDeltaTime)) <= 0f)
                {
                    surfIn = UnityEngine.Random.Range(5f, 11f);
                    string one = Pick(waves > 1f ? "wave-crash-" : "wave-small-", ref lastSurf);
                    if (one != null) Shot(one, camAt, 0.45f * vol * surf, UnityEngine.Random.Range(0.94f, 1.05f), false, 0f);
                }
            }
            wasUnder = under;
            Loops();
        }

        /// <summary>(The sea not drawn: too high, the map, another world.) Every loop let fade.</summary>
        public static void Quiet()
        {
            if (loops.Count == 0 || Hush.Paused) return;
            wasUnder = false;
            Loops();
        }

        static void Want(string loop, float volume) { wanted[loop] = Mathf.Max(0f, volume); }

        // (for the tests: how many loops have been made and started, woken from their pause, and let go)
        internal static int starts, wakes, ends;

#if DEV
        /// <summary>(For the tests.) Each loop's loudness and whether it plays or rests, and the counts.</summary>
        public static string Heard()
        {
            var b = new System.Text.StringBuilder("Natural Ocean's loops:");
            foreach (KeyValuePair<string, AudioSource> p in loops)
                b.Append(' ').Append(p.Key).Append(' ').Append(p.Value == null ? "gone" : p.Value.volume.ToString("F3") + (resting.ContainsKey(p.Key) ? " resting" : p.Value.isPlaying ? " playing" : " not playing"));
            return b.Append(" | started ").Append(starts).Append(", woken ").Append(wakes).Append(", let go ").Append(ends).Append(" | frame ").Append(Time.frameCount).ToString();
        }
#endif

        // (A loop faded out is paused and kept, and goes on from there when it is wanted again; one unheard for a minute is
        // let go. Since 2026-11-10: before, it was destroyed and a new one made and started the next time, and starting a
        // streamed recording holds the whole game up for 15 to 70 ms, measured on Natural Weather's wind loops, made the
        // same way.)
        static void Loops()
        {
            foreach (KeyValuePair<string, float> w in wanted)
            {
                if (w.Value < 0.002f) continue;
                if (loops.TryGetValue(w.Key, out AudioSource had))
                {
                    if (had != null && resting.Remove(w.Key)) { had.UnPause(); wakes++; }
                    continue;
                }
                AudioClip clip = Clip(w.Key);
                if (clip == null) continue;
                AudioSource s = Source("Natural Ocean " + w.Key, false);
                s.clip = clip; s.loop = true; s.spatialBlend = 0f;
                // (begun at a place of its own in the loop)
                s.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f));
                s.Play();
                starts++;
                loops[w.Key] = s;
            }
            ended.Clear();
            foreach (KeyValuePair<string, AudioSource> p in loops)
            {
                AudioSource s = p.Value;
                if (s == null) { ended.Add(p.Key); continue; }
                if (resting.TryGetValue(p.Key, out float since))
                {
                    if (Time.unscaledTime - since > 60f) { UnityEngine.Object.Destroy(s.gameObject); ended.Add(p.Key); ends++; }
                    continue;
                }
                float to = wanted.TryGetValue(p.Key, out float v) ? v : 0f;
                s.volume = Mathf.MoveTowards(s.volume, to, Time.unscaledDeltaTime * 0.35f);
                if (to < 0.002f && s.volume <= 0f) { s.Pause(); resting[p.Key] = Time.unscaledTime; }
            }
            foreach (string e in ended) { loops.Remove(e); resting.Remove(e); }
            wanted.Clear();
        }

        /// <summary>Something heavy has struck the water there (the blow's energy, joules): a splash from there, as loud as the blow.</summary>
        public static void Splash(Vector3 at, float joules)
        {
            Ask();
            if (gone || names.Count == 0) return;
            // (a kilojoule hardly heard, ten megajoules (a tonne at 140 m/s) as loud as it goes)
            float loud = Mathf.Clamp01((Mathf.Log10(Mathf.Max(joules, 1f)) - 3f) / 4f);
            if (loud < 0.05f) return;
            string one = Pick("splash-", ref lastSplash);
            // (the bigger the blow, the deeper, and the further it is heard at its full. Twice the ship's volume at its loudest: the
            // splashes are recorded some 9 dB quieter than the sea's loops, and twenty megajoules 150 m off stood only 5 dB over a
            // rough sea, 2026-10-24)
            if (one != null) Shot(one, at, Mathf.Clamp01(2f * loud * GameSettings.SHIP_VOLUME), Mathf.Lerp(1.1f, 0.8f, loud), true, Mathf.Lerp(15f, 150f, loud));
        }

        /// <summary>One of the loaded recordings whose names begin so, by chance, not the one picked last time.</summary>
        static string Pick(string start, ref int last)
        {
            var choices = new List<string>();
            foreach (string name in names) if (name.StartsWith(start, StringComparison.Ordinal) && Clip(name) != null) choices.Add(name);
            if (choices.Count == 0) return null;
            int pick = UnityEngine.Random.Range(0, choices.Count);
            if (pick == last && choices.Count > 1) pick = (pick + 1) % choices.Count;
            last = pick;
            return choices[pick];
        }

        /// <summary>A recording played once: from a place (heard as late as sound takes to come from there, at its full within
        /// `full` metres, then as one over the distance), or flat, as what is all round.</summary>
        static void Shot(string name, Vector3 at, float volume, float pitch, bool fromThere, float full = 12f)
        {
            AudioClip clip = Clip(name);
            if (clip == null || Hush.Paused) return;
            AudioSource s = null;
            foreach (AudioSource c in shots) if (c != null && !c.isPlaying) { s = c; break; }
            if (s == null)
            {
                if (shots.Count >= 4) return;
                s = Source("Natural Ocean splash", true);
                shots.Add(s);
            }
            s.transform.position = at;
            s.spatialBlend = fromThere ? 1f : 0f;
            s.minDistance = Mathf.Max(full, 1f);
            s.clip = clip; s.volume = volume; s.pitch = pitch;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            float far = cam != null && fromThere ? (cam.transform.position - at).magnitude : 0f;
            if (far > 4000f) return;
            s.PlayDelayed(far / Speed(at));
        }

        /// <summary>The speed of sound in the air there (docs/sound-rules.md, P2): 343 m/s where it cannot be had.</summary>
        static float Speed(Vector3 at)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null || !body.atmosphere) return 343f;
            double p = FlightGlobals.getStaticPressure((Vector3d)at, body);
            double rho = p > 0 ? FlightGlobals.getAtmDensity(p, FlightGlobals.getExternalTemperature((Vector3d)at, body), body) : 0;
            return p > 0 && rho > 0 ? Mathf.Clamp((float)body.GetSpeedOfSound(p, rho), 80f, 900f) : 343f;
        }

        static AudioSource Source(string name, bool placed)
        {
            if (holder == null) holder = new GameObject("Natural Ocean sounds");
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.dopplerLevel = 0f; s.volume = 0f;
            if (placed) { s.rolloffMode = AudioRolloffMode.Logarithmic; s.maxDistance = 4000f; s.spread = 60f; }
            Hush.Add(s);
            return s;
        }

        /// <summary>Everything let go at once (the scene is going).</summary>
        public static void Drop()
        {
            if (holder != null) UnityEngine.Object.Destroy(holder);
            holder = null;
            loops.Clear(); wanted.Clear(); resting.Clear(); shots.Clear();
            wasUnder = false;
        }
    }
}
