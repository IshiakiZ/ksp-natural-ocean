using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

#if !DEV
[assembly: KSPAssembly("NaturalOcean", 0, 1)]
[assembly: KSPAssemblyDependency("Keystone", 0, 7)]
#endif

namespace NaturalOcean
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class OceanAtStart : MonoBehaviour
    {
        void Awake() { Ocean.Setup(); Destroy(gameObject); }
    }

    /// <summary>
    /// Natural Ocean: a sea of its own for every world that has one. Asked for on 2026-10-11: "I want the oceans to look as
    /// beautiful as possible ... as realistic as possible ... based on the weather, have the oceans change to be really rocky
    /// seas", with "white water bubbles; mist from waves crashing into each other; oceans or waves that curve over themselves;
    /// basically everything that makes it realistic; seaweed". What real seas do, and how they are drawn, is in
    /// wiki/ksp-natural-ocean/Research.md; what is made so far and what is not, in its Plan.md.
    /// </summary>
    static class Ocean
    {
        public const string Version = "0.1.0";
        public static Mod Page;
        public static Toggle On, Others, Float, Weeds;
        public static Slider Rough, Clear;

        public static void Setup()
        {
            if (Page != null && Kit.Find("Natural Ocean") == Page) return;
            Page = Kit.Register("Natural Ocean", Version, "A sea of its own: waves raised by the wind the weather has, swell from elsewhere, foam where they break.");
            On = Page.Toggle("on", "Natural Ocean", true, "Off: the sea as the game (or another mod) draws it.");
            Rough = Page.Slider("rough", "How rough", 1f, 0f, 2f, "x", 2, "The wind the sea is given, against the wind the weather has: 1 as it is; 0 a flat calm; 2 twice the wind (waves four times as high).");
            Clear = Page.Slider("clear", "How clear the water is", 1f, 0.3f, 3f, "x", 2, "How far down the bottom can be seen: 1 clear coastal water, about twelve metres.");
            Float = Page.Toggle("float", "Craft ride the waves", true, "What floats is lifted and dropped by the waves that are drawn, and kerbals swim on them. Off: everything floats on a flat sea at the level the game knows, and the waves pass through it.");
            Weeds = Page.Toggle("weed", "Seaweed", true, "Grass and weed on the bottom where the water is one to eleven metres deep: plants near the camera, dark beds from afar.");
            Others = Page.Toggle("others", "Put other seas out of sight", true, "Where this sea is drawn, the game's own sea is not, and Scatterer's is drawn with nothing of it showing (Scatterer's settings are not touched; it goes on working its sea out, which costs a little: switching its sea off in Scatterer's own settings saves that).");
        }
    }

    /// <summary>The sea in flight: the waves, the mesh they move, and what the two are told every frame.</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Sea : MonoBehaviour
    {
        public static string Folder => KSPUtil.ApplicationRootPath + "GameData/NaturalOcean/PluginData/";
        public static string Report = "";
        static Shader shader, foamShader, weedShader, underShader;
        static bool tried;
        static string why = "";
        static Sea instance;

        Waves waves;
        Surface surface;
        /// <summary>
        /// How far the wind is taken to have blown over open water, m: what makes a sea young (short and steep) or grown (long
        /// and high). 400 km grows any wind's sea to its full height.
        /// </summary>
        public static float Reach = 400000f;
        Foam foam;
        Seabed seabed;
        Spray spray;
        Weed weed;
        double foamClock = double.NaN;
        // The sea's own two level axes, kept in the planet's own frame and carried along under the camera without being turned
        // against the ground (so the waves under a boat do not swing round as it sails), and where the point under the camera is on them.
        Vector3d firstFixed, upWas;
        Vector2d at;
        string placedOn = "";
        double clock = 1000.0, utWas = double.NaN;
        SeaState sea;
        bool seaSet;
        readonly Dictionary<Camera, CommandBuffer> grabs = new Dictionary<Camera, CommandBuffer>();
        // The picture the game finished last frame, kept half size with smaller copies, for the sea to mirror: the sky as it
        // really is, with its clouds, the land, a flame. (This frame's cannot be had: the clouds are drawn after the sea.)
        RenderTexture mirror;
        CommandBuffer mirrorGrab;
        Camera mirrorOn;
        Matrix4x4 seenWith, seenBefore;
        bool seenOnce;
        static readonly int idScene = Shader.PropertyToID("_OceanScene");
        internal static readonly int[] IdAt = MakeIds();
        static int[] MakeIds() { var ids = new int[Waves.Patches]; for (int n = 0; n < ids.Length; n++) ids[n] = Shader.PropertyToID("_OceanAt" + n); return ids; }
        // (development build: a sea held for a test)
        static float heldWind = -1f, heldSwell = -1f, heldTo = float.NaN, heldCross = float.NaN;
        static string lightSaid = "";
        /// <summary>(Development build, for finding a fault: 1 leaves out the copy of the picture before the sea; 2 leaves other seas alone; 3 hides ours; 4: nothing mirrored from the last picture.)</summary>
        public static float Test;
        public static string SetTest(string v) { Test = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); if (instance != null) instance.Off(""); return "test " + Test; }

        void Awake() { instance = this; Ocean.Setup(); Camera.onPreCull += SeenWith; Camera.onPreRender += UnderByWaves; }

        static Shader LoadOne(string file, string name, string system)
        {
            string path = Folder + file + system + ".bundle";
            if (!File.Exists(path)) { why = system == "" ? "a shader of its own (" + path + ") is missing" : "there is no shader for " + SystemInfo.graphicsDeviceType + " yet (only for OpenGL, on Macs and Linux)"; return null; }
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) { why = "a shader of its own did not load (" + file + ")"; return null; }
            Shader found = null;
            foreach (Shader s in bundle.LoadAllAssets<Shader>()) if (s != null && s.name == name && s.isSupported) found = s;
            bundle.Unload(false);
            if (found == null) why = "this graphics card cannot run one of its shaders (" + name + ")";
            return found;
        }

        static void Load()
        {
            tried = true;
            try
            {
                string system = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ? "" : Application.platform == RuntimePlatform.WindowsPlayer ? "-windows" : Application.platform == RuntimePlatform.LinuxPlayer ? "-linux" : "-mac";
                shader = LoadOne("ocean", "NaturalOcean/Sea", system);
                if (shader != null) foamShader = LoadOne("oceanfoam", "NaturalOcean/Foam", system);
                if (foamShader == null) shader = null;
                // (the seaweed's: the sea is drawn without it if it is not there)
                if (shader != null) { string was = why; weedShader = LoadOne("oceanweed", "NaturalOcean/Weed", system); why = was; }
                // (and the lens in the water: without it the game's own fog under water is left as it is)
                if (shader != null) { string was = why; underShader = LoadOne("oceanunder", "NaturalOcean/Under", system); why = was; }
            }
            catch (Exception ex) { shader = null; why = "its shaders did not load (" + ex.Message + ")"; }
            finally { Kit.Log("Natural Ocean", shader != null ? "the sea's shaders loaded" : "no sea: " + why); }
        }

        void MirrorOff()
        {
            if (mirrorOn != null && mirrorGrab != null) mirrorOn.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, mirrorGrab);
            mirrorOn = null; seenOnce = false; seenWith = Matrix4x4.zero;
        }

        /// <summary>The last camera to draw the scene is made to keep its finished picture (before the lens has it), every frame.</summary>
        void Mirror(Camera cam, Material m)
        {
            int w = Mathf.Max(64, cam.pixelWidth / 2), h = Mathf.Max(64, cam.pixelHeight / 2);
            if (mirror == null || mirror.width != w || mirror.height != h)
            {
                MirrorOff();
                if (mirror != null) { mirror.Release(); Destroy(mirror); }
                mirror = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
                {
                    name = "Natural Ocean: the last picture", useMipMap = true, autoGenerateMips = false, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                };
                mirror.Create();
            }
            if (mirrorOn != cam)
            {
                MirrorOff();
                if (mirrorGrab == null) mirrorGrab = new CommandBuffer { name = "Natural Ocean: the picture kept for mirroring" };
                mirrorGrab.Clear();
                mirrorGrab.Blit(BuiltinRenderTextureType.CameraTarget, mirror);
                mirrorGrab.GenerateMips(mirror);
                mirrorGrab.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                cam.AddCommandBuffer(CameraEvent.BeforeImageEffects, mirrorGrab);
                mirrorOn = cam;
            }
            // (what the camera saw with when it drew the picture the sea will mirror this frame: last frame's)
            Matrix4x4 with = seenBefore;
            m.SetVector("_OceanMirrorX", new Vector4(with.m00, with.m01, with.m02, seenOnce ? 1f : 0f));
            m.SetVector("_OceanMirrorY", new Vector4(with.m10, with.m11, with.m12, h / (cam.fieldOfView * Mathf.Deg2Rad)));
            m.SetVector("_OceanMirrorW", new Vector4(with.m30, with.m31, with.m32, Mathf.Max(0, mirror.mipmapCount - 2)));
            m.SetTexture("_OceanMirror", mirror);
        }

        static readonly int idLocalPos = Shader.PropertyToID("_LocalCameraPos"), idLocalDir = Shader.PropertyToID("_LocalCameraDir");

        /// <summary>
        /// The game fogs things under water when the camera is below the sea's own level, by how far below each thing is: the
        /// whole picture or none of it. With the lens's own pass (Lens, below) that fog is kept off altogether: just before
        /// each of the flight's cameras draws, the two values the game's shaders go by (where the camera is with how high it
        /// is, and which way is up with whether it is under) are set to "above". Without that pass (its shader missing) they
        /// are set by the water's height at the camera, so that at least it is the waves the camera is under or not. The
        /// game sets them again every frame.
        /// </summary>
        static void UnderByWaves(Camera cam)
        {
            Sea s = instance;
            if (s == null || s.waves == null || s.surface == null || !s.surface.Showing || cam == null || (cam.name != "Camera 00" && cam.name != "Camera 01")) return;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null) return;
            Vector3d eye = (Vector3d)cam.transform.position;
            float under = (float)body.GetAltitude(eye) - HeightAt(eye);
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, eye);
            if (under < 0f && s.lensMaterial == null)
            {
                Shader.SetGlobalVector(idLocalPos, new Vector4((float)eye.x, (float)eye.y, (float)eye.z, under));
                Shader.SetGlobalVector(idLocalDir, new Vector4(up.x, up.y, up.z, 1f));
            }
            else
            {
                Shader.SetGlobalVector(idLocalPos, new Vector4(1f, 1f, 1f, 0f));
                Shader.SetGlobalVector(idLocalDir, new Vector4(1f, 1f, 1f, 0f));
            }
            if (s.lensMaterial != null && cam == s.lensOn) s.Lens(cam, body, eye, under, up);
        }

        /// <summary>
        /// The game's own view of being under water, on or off. Below the sea's level (the level, not the waves) the game
        /// dims the sun for everything it draws, the hills above the water too, and tells its shaders to fog by distance:
        /// the whole picture or none of it. Each of the flight's cameras has a part for that, with a switch (fxOn); off, the
        /// scene is drawn as from the air, and the lens's own pass lays the water over what the wet glass sees. Put back on
        /// when the sea is off or has no such pass.
        /// </summary>
        void StockUnder(Camera near, Camera far, bool on)
        {
            if (near != stockNear || far != stockFar)
            {
                stockNear = near; stockFar = far;
                stockUnder[0] = near != null ? near.GetComponent<UnderwaterFog>() : null;
                stockUnder[1] = far != null && far != near ? far.GetComponent<UnderwaterFog>() : null;
            }
            for (int n = 0; n < stockUnder.Length; n++)
                if (stockUnder[n] != null && stockUnder[n].fxOn != on) stockUnder[n].fxOn = on;
        }
        Camera stockNear, stockFar;
        readonly UnderwaterFog[] stockUnder = new UnderwaterFog[2];

        // ---- the lens in the water (tools/shaderpack/oceanunder.glsl)
        Material lensMaterial;
        CommandBuffer lensPass;
        bool fineAtGlass;
        MaterialPropertyBlock lensBlock;
        Camera lensOn;
        /// <summary>The water's own light where the camera is (as light), the sky's low down, and how far one sees through the water (m): set where the sea's light is worked out.</summary>
        Color lensLight, lensSky;
        float lensSees = 30f;
        /// <summary>The sea's two level axes in the scene, as its mesh was laid this frame; the point of the sea under the camera then; where that point is in the four patches of smaller waves; which way the sun is, and how much of it there is.</summary>
        Vector3 lensFirst = Vector3.right, lensSecond = Vector3.forward, lensMid, lensToSun = Vector3.up;
        readonly Vector4[] lensAts = new Vector4[4];
        float lensSunny;
        /// <summary>How far a breaking crest goes over (1: as worked out; 0: not at all).</summary>
        public static float Curl = 1f;
        /// <summary>How large the lip a breaking crest throws is, against how high that size of wave runs (the mean of its highest third).</summary>
        public static float Lip = 0.33f;
        /// <summary>How much of the sun's light gathered by the waves shows under water (1: as worked out; 0: none).</summary>
        public static float Rays = 1f;
        /// <summary>Whether the lens's pass is laid over the picture this frame (the glass at the water or under it).</summary>
        public static bool LensOn;
        /// <summary>How far up the glass the water climbs at its edge, m (the dark thread along the line: on a wet wall, two or three millimetres).</summary>
        public static float Climbs = 0.0022f;
        /// <summary>(development build) 0: the line taken from the waves' numbers at the camera, as it first was, not from the map of the sea as drawn.</summary>
        public static float Mapped = 1f;
        /// <summary>(development build) one thing of the lens's pass shown alone (oceanunder.glsl); and what the pass last went by.</summary>
        public static float LensShow;
        public static string LensSaid = "";
        const int MapSide = 128;
        static readonly int idLensScene = Shader.PropertyToID("_UnderScene"), idLensMap = Shader.PropertyToID("_UnderMap"), idMapAt = Shader.PropertyToID("_OceanMapAt");

        /// <summary>
        /// Whether the camera's glass is at the water or under it, by where the camera is this frame; and the pass over the
        /// finished picture put on the camera or taken off it accordingly. (Not put on for good: it copies the whole picture.)
        /// What the pass does is written into it just before the camera draws (Lens), when where the camera stands is final.
        /// </summary>
        void LensWanted(Camera cam, CelestialBody body)
        {
            if (underShader == null) { LensOff(); return; }
            if (lensMaterial == null) lensMaterial = new Material(underShader) { name = "Natural Ocean: the lens in the water" };
            Vector3d eye = (Vector3d)cam.transform.position;
            float over = (float)body.GetAltitude(eye) - HeightAt(eye);
            // (the glass reaches some way below the camera's middle: as far as the near plane is wide, and a margin for the
            // water's slope and for the frame the camera has yet to move in)
            float reach = cam.nearClipPlane * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(1f, cam.aspect) * 1.6f + 0.25f;
            bool wanted = over < reach && Test != 5f;
            if (wanted && lensOn != cam)
            {
                LensOff();
                lensPass = new CommandBuffer { name = "Natural Ocean: the lens in the water" };
                // (After everything see-through is drawn and before anything is done to the finished picture. Put with the
                // picture's own effects (BeforeImageEffects), as it first was, it came after the ones already there: the
                // lens's effects had been done, light and colour were numbers for the screen, and the picture's fourth
                // number, by which the sea says how far off its underside is, had been written over.)
                cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, lensPass);
                lensOn = cam;
                // (the picture kept for mirroring is to be taken after this, as it was after the game's fog: taken off here, it is put on again behind)
                MirrorOff();
            }
            else if (!wanted) LensOff();
            LensOn = lensOn != null;
        }

        void LensOff()
        {
            if (lensOn != null && lensPass != null) lensOn.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, lensPass);
            if (lensPass != null) { lensPass.Release(); lensPass = null; }
            lensOn = null; LensOn = false;
        }

        /// <summary>
        /// Just before the camera draws: where the water's surface cuts its glass, and the pass written out for this frame.
        /// The cut is the sea's as it is DRAWN: the innermost ring of its mesh is drawn once more, seen from straight above
        /// and no wider than the glass, into a small picture of the water's height there (the sea's own shader does it when
        /// told where the glass is: ocean.glsl), and the pass reads the water's edge on the glass from that. The waves'
        /// numbers at the camera, which is what the line went by at first, are a finger's breadth off the mesh, and gave a
        /// band along the line with the wrong picture in it; they are still handed over, for wherever the map has nothing.
        /// </summary>
        void Lens(Camera cam, CelestialBody body, Vector3d eye, float over, Vector3 up)
        {
            Transform t = cam.transform;
            float near = cam.nearClipPlane, tanUp = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanAcross = tanUp * cam.aspect;
            // ---- the pass for this frame
            float half = 1.1f * near * Mathf.Sqrt(tanAcross * tanAcross + tanUp * tanUp) + 0.01f;
            // (the map only while the surface can be across the glass: deeper down the glass is all wet, and the water taken as flat says so)
            bool mapped = surface != null && surface.Showing && Mapped > 0.5f && over > -(2f * half + 0.5f);
            lensPass.Clear();
            if (mapped)
            {
                Vector3 glass = t.position + t.forward * near;
                if (lensBlock == null) lensBlock = new MaterialPropertyBlock();
                lensBlock.SetVector(idMapAt, new Vector4(glass.x, glass.y, glass.z, 1f / half));
                lensPass.GetTemporaryRT(idLensMap, MapSide, MapSide, 16, FilterMode.Bilinear, SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf);
                lensPass.SetRenderTarget(idLensMap);
                lensPass.ClearRenderTarget(true, true, Color.clear);
                lensPass.DrawMesh(surface.Inner, surface.InnerAt, surface.Material, 0, 0, lensBlock);
#if DEV
                // (for MapTest: the same map once more into a picture that is kept, with where and how large it was)
                if (mapKept != null)
                {
                    lensPass.SetRenderTarget(mapKept);
                    lensPass.ClearRenderTarget(true, true, Color.clear);
                    lensPass.DrawMesh(surface.Inner, surface.InnerAt, surface.Material, 0, 0, lensBlock);
                    mapGlass = glass; mapHalf = half; mapFrame = Time.frameCount;
                }
#endif
            }
            lensPass.GetTemporaryRT(idLensScene, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            lensPass.Blit(BuiltinRenderTextureType.CameraTarget, idLensScene);
            lensPass.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            lensPass.DrawMesh(Foam.Sheet, Matrix4x4.identity, lensMaterial, 0, 0);
            lensPass.ReleaseTemporaryRT(idLensScene);
            if (mapped) lensPass.ReleaseTemporaryRT(idLensMap);

            // ---- the water at the camera taken as flat (for where the map has nothing): its height there and its slope, a hand to either side along two level ways
            Vector3 a = Vector3.Cross(up, t.forward);
            if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(up, t.up);
            a.Normalize();
            Vector3 b = Vector3.Cross(up, a);
            const float Step = 0.2f;
            float da = (HeightAt(eye + (Vector3d)a * Step) - HeightAt(eye - (Vector3d)a * Step)) / (2f * Step);
            float db = (HeightAt(eye + (Vector3d)b * Step) - HeightAt(eye - (Vector3d)b * Step)) / (2f * Step);
            Vector3 normal = (up - a * Mathf.Clamp(da, -1.5f, 1.5f) - b * Mathf.Clamp(db, -1.5f, 1.5f)).normalized;
            lensMaterial.SetVector("_UnderLens", new Vector4(tanAcross, tanUp, near, (float)(clock % 1024.0)));
            lensMaterial.SetVector("_UnderPlane", new Vector4(Vector3.Dot(t.right, normal), Vector3.Dot(t.up, normal), Vector3.Dot(t.forward, normal), over * Vector3.Dot(normal, up)));
            // (the map's heights are along the sea's own up, as its mesh was laid: the two level axes crossed)
            Vector3 upSea = mapped ? Vector3.Cross(lensSecond, lensFirst).normalized : up;
            lensMaterial.SetVector("_UnderUp", new Vector4(Vector3.Dot(t.right, upSea), Vector3.Dot(t.up, upSea), Vector3.Dot(t.forward, upSea), mapped ? 1f : 0f));
            lensMaterial.SetVector("_UnderMapA", new Vector4(Vector3.Dot(t.right, lensFirst) / half, Vector3.Dot(t.up, lensFirst) / half, Vector3.Dot(t.forward, lensFirst) / half, half));
            lensMaterial.SetVector("_UnderMapB", new Vector4(Vector3.Dot(t.right, lensSecond), Vector3.Dot(t.up, lensSecond), Vector3.Dot(t.forward, lensSecond), 0f) / half);
            // ---- the sun's light gathered by the waves (the lines of light on things under water, and the shafts): the waves
            // themselves, where the camera is among them, and which way the sun's light goes in water
            Vector3 fromMid = t.position - lensMid;
            lensMaterial.SetVector("_UnderEye", new Vector4(Vector3.Dot(fromMid, lensFirst), Vector3.Dot(fromMid, lensSecond), 0f, 0f));
            Vector3 level = lensToSun - upSea * Vector3.Dot(lensToSun, upSea);
            Vector3 inWater = level / 1.333f;
            float rise = Mathf.Sqrt(Mathf.Max(1f - inWater.sqrMagnitude, 0.01f));
            lensMaterial.SetVector("_UnderSun", new Vector4(Vector3.Dot(inWater, lensFirst) / rise, Vector3.Dot(inWater, lensSecond) / rise, Vector3.Dot(lensToSun, upSea) > 0.02f ? lensSunny * Rays : 0f, 0f));
            lensMaterial.SetVector("_OceanLay", new Vector4(1f / Waves.Longest, Waves.Ratio, Waves.Size, Waves.Large));
            lensMaterial.SetVector("_OceanTwin", new Vector4(Waves.TwinSize, Waves.TwinCos, Waves.TwinSin, Waves.TwinWeight));
            Vector3 sunWay = (inWater + upSea * rise).normalized;
            lensMaterial.SetVector("_UnderSunWay", new Vector4(Vector3.Dot(t.right, sunWay), Vector3.Dot(t.up, sunWay), Vector3.Dot(t.forward, sunWay), 0f));
            lensMaterial.SetVector("_OceanAt3", lensAts[0]); lensMaterial.SetVector("_OceanAt4", lensAts[1]); lensMaterial.SetVector("_OceanAt5", lensAts[2]); lensMaterial.SetVector("_OceanAt6", lensAts[3]);
            if (waves != null) lensMaterial.SetTexture("_OceanSlope", waves.Slope);
            // (the water's light dims with how deep the camera is: by e in some twenty-five metres of clear water)
            float deep = Mathf.Max(-over, 0f);
            Color light = lensLight * Mathf.Exp(-deep / (0.85f * Mathf.Max(lensSees, 3f)));
            lensMaterial.SetVector("_UnderLight", new Vector4(light.r, light.g, light.b, QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1f : 0f));
            lensMaterial.SetVector("_UnderSky", new Vector4(lensSky.r, lensSky.g, lensSky.b, LensShow));
#if DEV
            LensSaid = "The lens: the camera's picture " + (cam.allowHDR ? "keeps light as it is (HDR)" : "is kept as numbers for the screen") + (cam.activeTexture != null ? ", drawn into " + cam.activeTexture.format + " " + cam.activeTexture.width + "x" + cam.activeTexture.height + " x" + cam.activeTexture.antiAliasing : ", drawn straight to the screen") + (cam.allowMSAA ? ", smoothed edges allowed (" + QualitySettings.antiAliasing + ")" : "") + "; the map " + (mapped ? "drawn, half a side " + half.ToString("F3") + " m" : "not drawn") + "; the camera " + over.ToString("F3") + " m over the water; the ground under it " + (seabed != null ? seabed.At(at.x, at.y).ToString("F1") : "unknown") + " m against the sea's level; the water's light " + lensLight.r.ToString("F3") + " " + lensLight.g.ToString("F3") + " " + lensLight.b.ToString("F3") + ".";
#endif
            // (What a metre of water takes of each colour: red by far the most, gone in a few metres. Set so that at as far as
            // one sees the blue is down to a sixth.)
            float sees = Mathf.Max(lensSees, 3f);
            lensMaterial.SetVector("_UnderTakes", new Vector4(9f / sees, 2.4f / sees, 1.8f / sees, Climbs));
        }

        static void SeenWith(Camera cam)
        {
            Sea s = instance;
            if (s == null || cam != s.mirrorOn) return;
            // (kept as the camera draws: at the next frame it is the last picture's)
            s.seenBefore = s.seenWith; s.seenOnce = s.seenWith != Matrix4x4.zero;
            s.seenWith = cam.projectionMatrix * cam.worldToCameraMatrix;
        }

        void Off(string said)
        {
            Report = said;
            if (surface != null) surface.Hide();
            foreach (var pair in grabs) if (pair.Key != null) pair.Key.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, pair.Value);
            foreach (var pair in grabs) pair.Value.Release();
            grabs.Clear();
            MirrorOff();
            LensOff();
            for (int n = 0; n < stockUnder.Length; n++) if (stockUnder[n] != null) stockUnder[n].fxOn = true;
            OtherSeas.Back();
        }

        void OnDestroy()
        {
            Off("");
            Camera.onPreCull -= SeenWith;
            Camera.onPreRender -= UnderByWaves;
            if (mirror != null) { mirror.Release(); Destroy(mirror); }
            if (mirrorGrab != null) mirrorGrab.Release();
            if (waves != null) waves.Dispose();
            if (surface != null) surface.Dispose();
            if (foam != null) foam.Dispose();
            if (seabed != null) seabed.Dispose();
            if (spray != null) spray.Dispose();
            if (weed != null) weed.Dispose();
            if (instance == this) instance = null;
        }

        static Camera Find(string name)
        {
            foreach (Camera c in Camera.allCameras) if (c != null && c.name == name) return c;
            return null;
        }

        void LateUpdate()
        {
            if (Ocean.On == null || !Ocean.On) { Off(""); return; }
            if (!tried) Load();
            if (shader == null) { Off("Natural Ocean: " + why); return; }
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera near = Find("Camera 00"), far = Find("Camera 01") ?? near;
            if (body == null || !body.ocean || near == null || MapView.MapIsEnabled) { Off(body != null && !body.ocean ? "No sea on " + body.bodyName + "." : ""); return; }
            double R = body.Radius;
            Vector3d camFrom = (Vector3d)near.transform.position - body.position;
            double high = camFrom.magnitude - R;
            // (from far above, the game's picture of the whole planet has the sea)
            if (high > 60000.0) { Off("Too high for the sea to be drawn in waves."); return; }
            if (waves == null) waves = new Waves();
            if (surface == null) surface = new Surface(shader);
            if (foam == null) foam = new Foam(foamShader);
            if (seabed == null) seabed = new Seabed();

            // ---- the planet's own axes in the scene, and the sea's two axes carried along
            Vector3d fx = body.GetSurfaceNVector(0.0, 0.0), fy = body.GetSurfaceNVector(0.0, 90.0), fz = body.GetSurfaceNVector(90.0, 0.0);
            Vector3d upScene = camFrom.normalized;
            Vector3d up = new Vector3d(Vector3d.Dot(upScene, fx), Vector3d.Dot(upScene, fy), Vector3d.Dot(upScene, fz));
            if (placedOn != body.bodyName || Vector3d.Dot(up, upWas) < 0.999)
            {
                // (begun afresh: the first axis east)
                firstFixed = Vector3d.Cross(new Vector3d(0, 0, 1), up);
                if (firstFixed.sqrMagnitude < 1e-6) firstFixed = Vector3d.Cross(new Vector3d(0, 1, 0), up);
                firstFixed = firstFixed.normalized;
                placedOn = body.bodyName; upWas = up;
                at = new Vector2d(0.0, 0.0);
            }
            Vector3d first = (fx * firstFixed.x + fy * firstFixed.y + fz * firstFixed.z).normalized;
            Vector3d second = Vector3d.Cross(first, upScene).normalized;
            Vector3d secondFixed = new Vector3d(Vector3d.Dot(second, fx), Vector3d.Dot(second, fy), Vector3d.Dot(second, fz));
            Vector3d gone = (up - upWas) * R;
            at += new Vector2d(Vector3d.Dot(gone, firstFixed), Vector3d.Dot(gone, secondFixed));
            upWas = up;
            firstFixed = (firstFixed - up * Vector3d.Dot(firstFixed, up)).normalized;
            first = (fx * firstFixed.x + fy * firstFixed.y + fz * firstFixed.z).normalized;
            second = Vector3d.Cross(first, upScene).normalized;

            // ---- the sea's clock: the game's, but never faster than four times real time (a sea at a thousand times is a flicker)
            double ut = Planetarium.GetUniversalTime();
            if (!double.IsNaN(utWas)) clock += Math.Max(0.0, Math.Min(ut - utWas, 4.0 * Time.unscaledDeltaTime + 0.02));
            utWas = ut;

            // ---- what the sea is doing: the wind the weather has here, eased (a sea is always behind its wind)
            Vector3d eastScene = Vector3d.Cross(upScene, fz).normalized, northScene = Vector3d.Cross(eastScene, upScene).normalized;
            body.GetLatLonAlt((Vector3d)near.transform.position, out double lat, out double lon, out double _);
            Vector3d windEN = Keystone.Weather.There ? Keystone.Weather.At(body, lat, lon, ut).Wind : new Vector3d(-4.5, -1.0, 0.0);
            Vector3d windScene = eastScene * windEN.x + northScene * windEN.y;
            float rough = Ocean.Rough != null ? (float)Ocean.Rough : 1f;
            float speed = (float)Math.Sqrt(windEN.x * windEN.x + windEN.y * windEN.y) * rough;
            float to = (float)Math.Atan2(Vector3d.Dot(windScene, second), Vector3d.Dot(windScene, first));
#if DEV
            if (heldWind >= 0f) speed = heldWind;
            if (!float.IsNaN(heldTo)) to = heldTo;
#endif
            // (a swell from elsewhere, by where on the planet and which day: a metre or so, eleven to fifteen seconds, from its own way)
            uint day = unchecked((uint)(long)Math.Floor(ut / 21600.0)), place = unchecked((uint)(long)(Math.Floor(lat / 20.0) * 31 + Math.Floor(lon / 20.0)));
            float chance = Hash(day * 7919u + place * 104729u), chance2 = Hash(day * 31337u + place * 7u + 5u);
            float swell = Mathf.Lerp(0.3f, 1.8f, chance * chance) * Mathf.Clamp01(rough), swellPeriod = Mathf.Lerp(10f, 15f, chance2), swellTo = chance2 * 6.2832f + 1.3f;
#if DEV
            if (heldSwell >= 0f) swell = heldSwell;
            // (the swell made to run so many degrees across the wind's waves: a cross sea)
            if (!float.IsNaN(heldCross)) swellTo = to + heldCross * Mathf.Deg2Rad;
#endif
            var wanted = new SeaState
            {
                // (every patch is laid twice, the second time turned: the waves of the two run half that turn to either side of the way asked for)
                Wind = speed, WindTo = to + 0.5f * Waves.TwinTurn, Fetch = Reach, Depth = 0f, Swell = swell, SwellPeriod = swellPeriod, SwellTo = swellTo + 0.5f * Waves.TwinTurn,
                Gravity = (float)(body.GeeASL * 9.80665)
            };
            if (!seaSet) { sea = wanted; seaSet = true; }
            else
            {
                // (the wind sea rises over ten minutes and dies over half an hour; the swell changes with the day)
                float step = (float)Math.Min(1.0, Math.Max(0.0, Time.deltaTime * (wanted.Wind > sea.Wind ? 1.0 / 600.0 : 1.0 / 1800.0) * 3.0));
#if DEV
                if (heldWind >= 0f) step = Mathf.Min(1f, Time.unscaledDeltaTime * 0.7f);
#endif
                sea.Wind = Mathf.Lerp(sea.Wind, wanted.Wind, step);
                sea.WindTo = Mathf.LerpAngle(sea.WindTo * Mathf.Rad2Deg, wanted.WindTo * Mathf.Rad2Deg, Mathf.Max(step, 0.01f)) * Mathf.Deg2Rad;
                sea.Swell = Mathf.Lerp(sea.Swell, wanted.Swell, step); sea.SwellPeriod = Mathf.Lerp(sea.SwellPeriod, wanted.SwellPeriod, step);
                sea.SwellTo = wanted.SwellTo; sea.Fetch = wanted.Fetch; sea.Depth = wanted.Depth; sea.Gravity = wanted.Gravity;
            }
            waves.Sea = sea;
            waves.Step(clock, Mathf.Min(Time.unscaledDeltaTime, 0.1f));

            // ---- the mesh: finer the nearer the camera is to the water, out to the horizon
            float above = (float)Math.Max(high, 1.0);
            // (With the camera at the water itself, four times finer still: the water's surface then crosses the glass, and
            // where it cuts it is drawn as a line across the whole picture. On cells of a hand's breadth that line was three
            // or four straight strokes with corners between; on these it is a curve, and carries the ripples.)
            float overWater = (float)high - HeightAt((Vector3d)near.transform.position);
            fineAtGlass = overWater < (fineAtGlass ? 2.5f : 1.5f) && overWater > (fineAtGlass ? -3f : -2f);
            float cell = Mathf.Clamp(Mathf.Pow(2f, Mathf.Round(Mathf.Log(above * 0.004f, 2f))), fineAtGlass ? 0.03125f : 0.125f, 64f);
            double horizon = Math.Sqrt(2.0 * R * above + (double)above * above) + 2000.0;
            int rings = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log((float)horizon / (cell * Surface.Cells * 0.5f), 2f)) + 1, 4, Surface.Rings);
            Vector3 mid = (Vector3)(body.position + upScene * R);
            float reach = surface.Lay(mid, (Vector3)first, (Vector3)upScene, (Vector3)second, at, cell, rings);
            Material m = surface.Material;
            m.SetVector("_OceanMid", new Vector4(mid.x, mid.y, mid.z, (float)R));
            m.SetVector("_OceanAxisA", new Vector4((float)first.x, (float)first.y, (float)first.z, Surface.Cells * 0.5f));
            lensFirst = (Vector3)first; lensSecond = (Vector3)second; lensMid = mid;
            // (nought but for the lens's one drawing of the innermost ring from above: Lens)
            m.SetVector(idMapAt, Vector4.zero);
            m.SetVector("_OceanAxisB", new Vector4((float)second.x, (float)second.y, (float)second.z, Mathf.Max(1f, (float)(horizon * 1.6) / reach)));
            m.SetVector("_OceanLay", new Vector4(1f / Waves.Longest, Waves.Ratio, Waves.Size, Waves.Large));
            m.SetVector("_OceanTwin", new Vector4(Waves.TwinSize, Waves.TwinCos, Waves.TwinSin, Waves.TwinWeight));
            for (int n = 0; n < Waves.Patches; n++)
            {
                // (where the point under the camera is in each patch as first laid, and as laid the second time: smaller and turned)
                double side = Waves.Sides[n], twin = side * Waves.TwinSize;
                Vector2 shift = Waves.TwinShift(n);
                var where = new Vector4(Part(at.x, side), Part(at.y, side),
                    Part((Waves.TwinCos * at.x - Waves.TwinSin * at.y) / twin + shift.x, 1.0), Part((Waves.TwinSin * at.x + Waves.TwinCos * at.y) / twin + shift.y, 1.0));
                m.SetVector(IdAt[n], where);
                if (n >= 3 && n < 7) lensAts[n - 3] = where;
            }
            // (z: the lens's own pass lays the water between the camera and things under it: the sea's own shader then leaves that out)
            m.SetVector("_OceanShore", new Vector4(1f, Test >= 10f ? Test - 10f : 0f, underShader != null && Test != 5f ? 1f : 0f, LensOn ? 1f : 0f));
            m.SetVector("_OceanAtSpot", new Vector4((float)(at.x - Math.Floor(at.x / 1024.0) * 1024.0), (float)(at.y - Math.Floor(at.y / 1024.0) * 1024.0), 0f, 0f));
            m.SetVector("_OceanRoughA", new Vector4(waves.Patch[0].Roughness, waves.Patch[1].Roughness, waves.Patch[2].Roughness, waves.Patch[3].Roughness));
            m.SetVector("_OceanRoughB", new Vector4(waves.Patch[4].Roughness, waves.Patch[5].Roughness, waves.Patch[6].Roughness, waves.Patch[7].Roughness));
            m.SetTexture("_OceanMoved", waves.Moved); m.SetTexture("_OceanSlope", waves.Slope);

            // ---- the ground under the sea, for the shallows; and how high each patch's waves are, for what the depth lets through
            seabed.Step(body, at, body.position + upScene * R, first, second, upScene, m, foam.Work);
            var highA = new Vector4(waves.Patch[0].Height, waves.Patch[1].Height, waves.Patch[2].Height, waves.Patch[3].Height);
            var highB = new Vector4(waves.Patch[4].Height, waves.Patch[5].Height, waves.Patch[6].Height, waves.Patch[7].Height);
            m.SetVector("_OceanHighA", highA); m.SetVector("_OceanHighB", highB);
            foam.Work.SetVector("_OceanHighA", highA); foam.Work.SetVector("_OceanHighB", highB);
            // (The surf's two trains: the swell's and the wind sea's dominant waves, as high as they are out at sea. Each train's
            // count of periods is kept in two parts, the whole periods gone and the part of one: together they would be too
            // long a number for the graphics card to keep the part exactly.)
            double swellEvery = Math.Max(sea.SwellPeriod, 2f), windEvery = Math.Max(2.0 * Math.PI / Math.Max(Spectrum.Peak(sea), 0.05), 1.5);
            double swellCount = waves.ShownTime / swellEvery, windCount = waves.ShownTime / windEvery;
            var surfA = new Vector4((float)swellEvery, sea.Swell * 0.9f, (float)windEvery, (float)Spectrum.WindHeight(sea) * 0.6f);
            var surfB = new Vector4((float)(swellCount - Math.Floor(swellCount)), (float)(Math.Floor(swellCount) % 4096.0), (float)(windCount - Math.Floor(windCount)), (float)(Math.Floor(windCount) % 4096.0));
            var pinned = new Vector4((float)(at.x - Math.Floor(at.x / 1024.0) * 1024.0), (float)(at.y - Math.Floor(at.y / 1024.0) * 1024.0), 0f, 0f);
            m.SetVector("_OceanSurfA", surfA); m.SetVector("_OceanSurfB", surfB);
            foam.Work.SetVector("_OceanSurfA", surfA); foam.Work.SetVector("_OceanSurfB", surfB); foam.Work.SetVector("_OceanAtSpot", pinned);

            // ---- the white water, remembered from moment to moment (whitecaps: none below three metres a second, as the wind has it from six)
            float ready = Mathf.Clamp01((sea.Wind - 3f) / 3f);
            if (!float.IsNaN(waves.Breaks))
            {
#if DEV
                // (for SeenTest: white water laid at a known place, as a hull would churn it)
                if (Time.unscaledTime < seenMarkUntil) Foam.Mark(new Vector2d(at.x + seenMark.x, at.y + seenMark.y), 5f, 1f);
#endif
                foam.Step(waves, at, above, double.IsNaN(foamClock) ? 0.0 : Math.Max(0.0, Math.Min(waves.ShownTime - foamClock, 1.0)), ready, sea.WindTo - 0.5f * Waves.TwinTurn, sea.Wind, sea.Gravity, m);
                foamClock = waves.ShownTime;
            }

            // ---- the light: the sun as the game has it, the sky by how high the sun stands, both as the weather leaves them
            Vector3d toSun = Planetarium.fetch != null && Planetarium.fetch.Sun != null ? ((Vector3d)Planetarium.fetch.Sun.position - (Vector3d)near.transform.position).normalized : upScene;
            float sunUp = (float)Vector3d.Dot(toSun, upScene);
            Light sunLight = Sun.Instance != null ? Sun.Instance.sunLight : null;
            Color sunTint = sunLight != null ? sunLight.color * Mathf.Min(sunLight.intensity, 1.5f) : Color.white;
            float daylight = Mathf.Clamp01(sunUp * 5f + 0.15f), dusk = Mathf.Clamp01(1f - sunUp * 4f);
            WeatherShade shade = Keystone.Weather.There ? Keystone.Weather.Shade(body, (Vector3d)near.transform.position, toSun, ut) : new WeatherShade { Sun = 1f, Sky = 1f, Grey = 0f };
            Color top = new Color(0.035f, 0.11f, 0.32f) * daylight, low = Color.Lerp(new Color(0.36f, 0.50f, 0.68f), new Color(0.62f, 0.42f, 0.30f), dusk * 0.7f) * daylight;
            float greyTop = top.grayscale * 2.2f, greyLow = low.grayscale * 1.05f;
            top = Color.Lerp(top, new Color(greyTop, greyTop, greyTop * 1.04f), shade.Grey) * shade.Sky;
            low = Color.Lerp(low, new Color(greyLow, greyLow, greyLow * 1.03f), shade.Grey) * shade.Sky;
            m.SetVector("_OceanSun", new Vector4((float)toSun.x, (float)toSun.y, (float)toSun.z, shade.SunDimmed ? Mathf.Clamp01(sunUp * 12f + 0.2f) : shade.Sun * Mathf.Clamp01(sunUp * 12f + 0.2f)));
            m.SetVector("_OceanSunTint", new Vector4(sunTint.r, sunTint.g, sunTint.b, Mathf.Max(waves.HeightNow, 0.05f)));
            m.SetVector("_OceanSkyTop", new Vector4(top.r, top.g, top.b, shade.Grey));
            // (how far the sea can be seen along: in a gale the air over it is full of spray)
            // (from a gale on the air over the sea is full of spray: in a storm one sees two or three kilometres along it, in a hurricane less)
            float thick = Mathf.Clamp01((sea.Wind - 12f) / 16f);
            m.SetVector("_OceanSkyLow", new Vector4(low.r, low.g, low.b, Mathf.Lerp(90000f, 2200f, Mathf.Pow(thick, 0.7f))));
            // (the wind for the mesh: which way it blows on the sea's two axes, how hard, and how much of a storm it is: from a
            // near gale the breaking crests go over)
            float windWay = sea.WindTo - 0.5f * Waves.TwinTurn;
            m.SetVector("_OceanWind", new Vector4(Mathf.Cos(windWay), Mathf.Sin(windWay), sea.Wind, Mathf.Lerp(0.6f, 1f, Mathf.Clamp01((sea.Wind - 8f) / 14f)) * Curl));
            // (crests going over: where the white water has a size of wave breaking, the mesh throws a lip: the same levels)
            m.SetVector("_OceanBreaks", new Vector4(waves.Levels[1], waves.Levels[2], waves.Levels[3], waves.Levels[4]));
            m.SetVector("_OceanBreakHow", new Vector4(Foam.Eased, Waves.Longer, Foam.Soft, Lip));
            // ---- spray off the breaking crests, lit as the foam is
            if (spray == null) spray = new Spray();
            Color whiteLight = sunTint * ((shade.SunDimmed ? 1f : shade.Sun) * Mathf.Clamp01(sunUp * 12f + 0.2f) * (0.3f + 0.6f * Mathf.Max(sunUp, 0f))) + (top + low) * 0.6f;
            whiteLight = new Color(Mathf.Pow(Mathf.Clamp01(whiteLight.r * 0.62f), 1f / 2.2f), Mathf.Pow(Mathf.Clamp01(whiteLight.g * 0.62f), 1f / 2.2f), Mathf.Pow(Mathf.Clamp01(whiteLight.b * 0.62f), 1f / 2.2f), 1f);
            spray.Step(foam, waves, at, body.position + upScene * R, first, second, upScene, sea.Wind, sea.WindTo - 0.5f * Waves.TwinTurn, above, whiteLight, Mathf.Min(Time.deltaTime, 0.1f));
            // ---- seaweed: the beds as the sea's shader sees them from afar (by where on the planet), and the plants near the camera
            m.SetVector("_WeedUp", new Vector4((float)up.x, (float)up.y, (float)up.z, (float)R));
            m.SetVector("_WeedFirst", new Vector4((float)firstFixed.x, (float)firstFixed.y, (float)firstFixed.z, 0f));
            m.SetVector("_WeedSecond", new Vector4((float)secondFixed.x, (float)secondFixed.y, (float)secondFixed.z, 0f));
            if (weedShader != null && (Ocean.Weeds == null || Ocean.Weeds))
            {
                if (weed == null) weed = new Weed(weedShader);
                Vector3d eye = (Vector3d)near.transform.position;
                bool under = high < HeightAt(eye);
                // (the water goes to and fro along the way the swell runs, further the higher the sea)
                Vector3 sway = (Vector3)(first * Math.Cos(sea.SwellTo) + second * Math.Sin(sea.SwellTo));
                float much = Mathf.Clamp(0.12f + 0.1f * waves.HeightNow, 0.1f, 0.5f);
                Color sunLight2 = sunTint * ((shade.SunDimmed ? 1f : shade.Sun) * Mathf.Clamp01(sunUp * 12f + 0.2f) * (0.35f + 0.65f * Mathf.Max(sunUp, 0f)));
                Color fogged = Shader.GetGlobalColor("_UnderwaterFogColor");
                fogged = new Color(Mathf.Pow(fogged.r, 2.2f), Mathf.Pow(fogged.g, 2.2f), Mathf.Pow(fogged.b, 2.2f)) * (0.35f + 0.9f * Mathf.Max(sunUp, 0f));
                if (mirror != null) Shader.SetGlobalTexture("_OceanMirror", mirror);
                // (with the lens's own pass the weed is not fogged twice)
                weed.Step(body, eye, (float)high, under && (underShader == null || Test == 5f), LensOn, sway, much, clock, sunLight2, top + low, fogged, m);
            }
            else if (weed != null) { weed.Dispose(); weed = null; }
            float clear = Ocean.Clear != null ? (float)Ocean.Clear : 1f;
            m.SetVector("_OceanWater", new Vector4(0.004f, 0.030f, 0.062f, 12f * clear));
            // ---- the lens in the water: the water's own light (the game's colour for this sea's water, lit by the sun as
            // high as it stands and by the sky) and how far one sees through it (thirty metres in clear coastal water)
            {
                Color water = Shader.GetGlobalColor("_UnderwaterFogColor");
                Color sunNow = sunTint * ((shade.SunDimmed ? 1f : shade.Sun) * Mathf.Clamp01(sunUp * 12f + 0.2f));
                // (Over a shallow bottom the water's light is not the deep's blue: the bottom sends the sun back up through it,
                // and over sand a few metres down it is the green-blue of a lagoon. By how deep the water is under the camera.)
                float deepHere = seabed != null ? Mathf.Max(-seabed.At(at.x, at.y), 0f) : 1000f;
                float shallow = 1f - Mathf.SmoothStep(3f, 22f, deepHere);
                var tone = new Color(Mathf.Lerp(Mathf.Pow(water.r, 2.2f), 0.022f, shallow), Mathf.Lerp(Mathf.Pow(water.g, 2.2f), 0.21f, shallow), Mathf.Lerp(Mathf.Pow(water.b, 2.2f), 0.19f, shallow));
                lensLight = new Color(tone.r * (sunNow.r * (0.35f + 0.9f * Mathf.Max(sunUp, 0f)) + 0.5f * (top.r + low.r)),
                                      tone.g * (sunNow.g * (0.35f + 0.9f * Mathf.Max(sunUp, 0f)) + 0.5f * (top.g + low.g)),
                                      tone.b * (sunNow.b * (0.35f + 0.9f * Mathf.Max(sunUp, 0f)) + 0.5f * (top.b + low.b)));
                lensSky = (low * 0.8f + top * 0.2f) + sunNow * 0.08f;
                lensToSun = (Vector3)toSun; lensSunny = (shade.SunDimmed ? 1f : shade.Sun) * Mathf.Clamp01(sunUp * 6f);
                // (the sea's own shader mirrors the same light in its underside: dimmed as the lens's is, by how deep the camera is)
                Color mirrored = lensLight * Mathf.Exp(-Mathf.Max(HeightAt((Vector3d)near.transform.position) - (float)high, 0f) / (0.85f * Mathf.Max(30f * clear, 3f)));
                m.SetVector("_UnderLight", new Vector4(mirrored.r, mirrored.g, mirrored.b, 0f));
                lensSees = 30f * clear;
                LensWanted(near, body);
            }
            m.SetVector("_OceanFoam", new Vector4(ready, QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1f : 0f, waves.Breaks, (float)(waves.ShownTime % 4096.0)));

            // ---- what the camera has drawn before the sea, for seeing the bottom through shallow water
            if (Test != 1f) { Grab(near); if (far != near) Grab(far); }
            if (Test != 4f) Mirror(near, m); else { MirrorOff(); m.SetVector("_OceanMirrorX", Vector4.zero); }
            if ((Ocean.Others == null || Ocean.Others) && Test != 2f) OtherSeas.Away(body); else OtherSeas.Back();
            // (with the lens's own pass, no other whole-picture view of being under water: the game's (StockUnder) and Scatterer's (OtherSeas.Dry))
            StockUnder(near, far, underShader == null || Test == 5f);
            if (underShader != null && Test != 5f) OtherSeas.Dry();
            if (Test == 3f) surface.Hide();
#if DEV
            lightSaid = " Light: the sun " + sunTint.r.ToString("F2") + " " + sunTint.g.ToString("F2") + " " + sunTint.b.ToString("F2") + " (the game's light " + (sunLight != null ? sunLight.intensity.ToString("F2") : "none") + "), of which " + shade.Sun.ToString("F2")
                + " reaches the camera (sky " + shade.Sky.ToString("F2") + ", grey " + shade.Grey.ToString("F2") + (shade.SunDimmed ? ", the sun's light already dimmed by the weather" : "") + "); sky overhead " + top.r.ToString("F2") + " " + top.g.ToString("F2") + " " + top.b.ToString("F2")
                + ", at the horizon " + low.r.ToString("F2") + " " + low.g.ToString("F2") + " " + low.b.ToString("F2") + ".";
#endif
            Report = "The sea: wind " + sea.Wind.ToString("F1") + " m/s, waves " + waves.HeightNow.ToString("F1") + " m (the mean of the highest third), swell " + sea.Swell.ToString("F1") + " m every " + sea.SwellPeriod.ToString("F0") + " s.";
        }

        void Grab(Camera cam)
        {
            if (cam == null || grabs.ContainsKey(cam)) return;
            var c = new CommandBuffer { name = "Natural Ocean: the picture before the sea" };
            c.GetTemporaryRT(idScene, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            c.Blit(BuiltinRenderTextureType.CameraTarget, idScene);
            // (and back to the camera's own picture: a copy leaves what it copied into as the place things are drawn, and the
            // rest of the camera's frame went into the copy)
            c.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            c.SetGlobalTexture(idScene, idScene);
            cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, c);
            grabs[cam] = c;
        }

        static float Part(double where, double side) => (float)((where / side) - Math.Floor(where / side));

        static float Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777215f;
        }

        /// <summary>The water at a place in the scene: how high it stands against the sea's own level (m), and how fast the water itself is moving there (m/s, in the scene).</summary>
        public static float WaterAt(Vector3d world, out Vector3 flow) => WaterAt(world, out flow, out Vector2d _);

        /// <summary>The same, and where on the still sea that water belongs (on the sea's two axes, m: as the foam is kept).</summary>
        public static float WaterAt(Vector3d world, out Vector3 flow, out Vector2d place)
        {
            flow = Vector3.zero; place = new Vector2d(0.0, 0.0);
            Sea s = instance;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (s == null || s.waves == null || body == null || s.placedOn != body.bodyName) return 0f;
            Vector3d fx = body.GetSurfaceNVector(0.0, 0.0), fy = body.GetSurfaceNVector(0.0, 90.0), fz = body.GetSurfaceNVector(90.0, 0.0);
            Vector3d first = fx * s.firstFixed.x + fy * s.firstFixed.y + fz * s.firstFixed.z;
            Vector3d upScene = fx * s.upWas.x + fy * s.upWas.y + fz * s.upWas.z;
            Vector3d second = Vector3d.Cross(first, upScene);
            Vector3d from = world - (body.position + upScene * body.Radius);
            double x = s.at.x + Vector3d.Dot(from, first), z = s.at.y + Vector3d.Dot(from, second);
            float height = s.waves.WaterAt(x, z, s.seabed != null ? Mathf.Max(-s.seabed.At(x, z), 0f) : 1000f, out Vector3 along, out double fromX, out double fromZ);
            flow = (Vector3)(first * along.x + upScene * along.y + second * along.z);
            place = new Vector2d(fromX, fromZ);
            return height;
        }

        /// <summary>Whether a sea is being drawn in waves just now (and so can lift what floats).</summary>
        public static bool Drawn => instance != null && instance.waves != null && instance.surface != null && instance.surface.Showing;

        /// <summary>How high the sea stands over a place in the scene, against its own level (m); nothing where there is no sea drawn.</summary>
        public static float HeightAt(Vector3d world)
        {
            Sea s = instance;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (s == null || s.waves == null || body == null || s.placedOn != body.bodyName) return 0f;
            Vector3d fx = body.GetSurfaceNVector(0.0, 0.0), fy = body.GetSurfaceNVector(0.0, 90.0), fz = body.GetSurfaceNVector(90.0, 0.0);
            Vector3d first = fx * s.firstFixed.x + fy * s.firstFixed.y + fz * s.firstFixed.z;
            Vector3d upScene = fx * s.upWas.x + fy * s.upWas.y + fz * s.upWas.z;
            Vector3d second = Vector3d.Cross(first, upScene);
            Vector3d from = world - (body.position + upScene * body.Radius);
            double x = s.at.x + Vector3d.Dot(from, first), z = s.at.y + Vector3d.Dot(from, second);
            return s.waves.HeightAt(x, z, s.seabed != null ? Mathf.Max(-s.seabed.At(x, z), 0f) : 1000f);
        }

#if DEV
        /// <summary>(Development build: a sea held for a test: "wind 12", "swell 2", "to 90" (degrees round from the sea's first axis), "go" lets all go.)</summary>
        public static string Try(string words)
        {
            string[] w = (words ?? "").Split(' ');
            if (w.Length == 1 && w[0] == "go") { heldWind = -1f; heldSwell = -1f; heldTo = float.NaN; heldCross = float.NaN; return "the sea as the weather has it"; }
            for (int n = 0; n + 1 < w.Length; n += 2)
            {
                float v = float.Parse(w[n + 1], System.Globalization.CultureInfo.InvariantCulture);
                if (w[n] == "wind") heldWind = v; else if (w[n] == "swell") heldSwell = v; else if (w[n] == "to") heldTo = v * Mathf.Deg2Rad; else if (w[n] == "cross") heldCross = v;
            }
            return "held: wind " + heldWind + " m/s, swell " + heldSwell + " m";
        }

        /// <summary>(Development build: every camera, where it is from the craft and what it draws.)</summary>
        public static string Cams()
        {
            var b = new System.Text.StringBuilder();
            Vessel v = FlightGlobals.ActiveVessel;
            foreach (Camera c in Camera.allCameras)
            {
                Vector3 from = v != null ? c.transform.position - (Vector3)v.CoMD : c.transform.position;
                b.Append(c.name).Append(": ").Append(from.magnitude.ToString("F0")).Append(" m from the craft, depth ").Append(c.depth).Append(", near ").Append(c.nearClipPlane.ToString("F2")).Append(" far ").Append(c.farClipPlane.ToString("F0"))
                 .Append(", clears ").Append(c.clearFlags).Append(", mask ").Append(Convert.ToString(c.cullingMask, 16)).Append(c.targetTexture != null ? ", into " + c.targetTexture.name : "").Append(c.enabled ? "" : " (off)").Append("\n");
            }
            return b.ToString();
        }

        /// <summary>(Development build: the game's mirror balls, the pictures of the surroundings that shiny parts mirror.)</summary>
        public static string Probes()
        {
            var b = new System.Text.StringBuilder();
            foreach (ReflectionProbe p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>())
            {
                b.Append(p.name).Append(" on ").Append(p.transform.parent != null ? p.transform.parent.name : "nothing").Append(": ").Append(p.mode).Append(", ").Append(p.refreshMode).Append(", ").Append(p.resolution).Append(" px, hdr ").Append(p.hdr)
                 .Append(", mask ").Append(Convert.ToString(p.cullingMask, 16)).Append(", clears ").Append(p.clearFlags).Append(", near ").Append(p.nearClipPlane).Append(" far ").Append(p.farClipPlane).Append(", size ").Append(p.size)
                 .Append(", importance ").Append(p.importance).Append(", texture ").Append(p.texture != null ? p.texture.name + " " + p.texture.width + " mips " + p.texture.mipmapCount : "none").Append(p.enabled ? "" : " (off)").Append("\n");
            }
            CelestialBody home = FlightGlobals.currentMainBody;
            b.Append("under water, the game's own: fog colour ").Append(Shader.GetGlobalColor("_UnderwaterFogColor")).Append(", least distance ").Append(Shader.GetGlobalFloat("_UnderwaterMinAlphaFogDistance")).Append(", most albedo ").Append(Shader.GetGlobalFloat("_UnderwaterMaxAlbedoFog"))
             .Append(", most alpha ").Append(Shader.GetGlobalFloat("_UnderwaterMaxAlphaFog")).Append(", albedo scale ").Append(Shader.GetGlobalFloat("_UnderwaterAlbedoDistanceScalar")).Append(", alpha scale ").Append(Shader.GetGlobalFloat("_UnderwaterAlphaDistanceScalar"))
             .Append("; camera ").Append(Shader.GetGlobalVector("_LocalCameraPos")).Append(" dir ").Append(Shader.GetGlobalVector("_LocalCameraDir")).Append("\n");
            if (home != null) b.Append(home.bodyName).Append(": oceanUseFog ").Append(home.oceanUseFog).Append(", fog start ").Append(home.oceanFogColorStart).Append(" end ").Append(home.oceanFogColorEnd).Append(", density start ").Append(home.oceanFogDensityStart)
                .Append(" end ").Append(home.oceanFogDensityEnd).Append(", sun base ").Append(home.oceanSunBase).Append(" per metre ").Append(home.oceanSunAltMult).Append(" least ").Append(home.oceanSunMin).Append(", density ").Append(home.oceanDensity).Append("\n");
            b.Append("fog now: ").Append(RenderSettings.fog).Append(' ').Append(RenderSettings.fogMode).Append(' ').Append(RenderSettings.fogColor).Append(" density ").Append(RenderSettings.fogDensity).Append("\n");
            b.Append("default: ").Append(RenderSettings.defaultReflectionMode).Append(", custom ").Append(RenderSettings.customReflection != null ? RenderSettings.customReflection.name : "none").Append(", intensity ").Append(RenderSettings.reflectionIntensity)
             .Append("; skybox ").Append(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none").Append("; ambient ").Append(RenderSettings.ambientMode).Append(" ").Append(RenderSettings.ambientLight);
            return b.ToString();
        }

        /// <summary>
        /// (Development build: the ground along lines running east, to find a shore of a given kind: "step count north-offset
        /// east-start" in metres; or "find east-start east-end north-from north-to north-step": for each line, where the
        /// ground first goes under the sea and how far on it is 2 m and 5 m deep, which is how gently that shore shelves.)
        /// </summary>
        public static string Ground(string words)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (v == null || body == null || body.pqsController == null) return "no ground to ask";
            string[] w = (words ?? "").Split(' ');
            double perDegree = body.Radius * Math.PI / 180.0;
            double lat = v.latitude, lon = v.longitude;
            double High(double east, double north) => body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(lat + north / perDegree, lon + east / (perDegree * Math.Cos(lat * Math.PI / 180.0)))) - body.Radius;
            var b = new System.Text.StringBuilder();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (w.Length > 0 && w[0] == "find")
            {
                double from = double.Parse(w[1], inv), to = double.Parse(w[2], inv), northFrom = double.Parse(w[3], inv), northTo = double.Parse(w[4], inv), northStep = double.Parse(w[5], inv);
                for (double north = northFrom; north <= northTo; north += northStep)
                {
                    double edge = double.NaN, two = double.NaN, five = double.NaN, was = High(from, north);
                    for (double east = from; east <= to; east += 5.0)
                    {
                        double h = High(east, north);
                        if (double.IsNaN(edge) && was >= 0.0 && h < 0.0) edge = east;
                        if (!double.IsNaN(edge) && double.IsNaN(two) && h < -2.0) two = east;
                        if (!double.IsNaN(edge) && double.IsNaN(five) && h < -5.0) { five = east; break; }
                        was = h;
                    }
                    b.Append("north ").Append(north.ToString("F0")).Append(": the water's edge at ").Append(edge.ToString("F0")).Append(" m east, 2 m deep ").Append((two - edge).ToString("F0")).Append(" m on, 5 m deep ").Append((five - edge).ToString("F0")).Append(" m on\n");
                }
                return b.ToString();
            }
            double step = w.Length > 0 && w[0] != "" ? double.Parse(w[0], inv) : 100.0;
            int count = w.Length > 1 ? int.Parse(w[1]) : 40;
            double off = w.Length > 2 ? double.Parse(w[2], inv) : 0.0, start = w.Length > 3 ? double.Parse(w[3], inv) : 0.0;
            b.Append("north ").Append(off.ToString("F0")).Append(", from ").Append(start.ToString("F0")).Append(" m east every ").Append(step.ToString("F0")).Append(" m:");
            for (int n = 0; n < count; n++) b.Append(' ').Append(High(start + n * step, off).ToString("F1"));
            return b.ToString();
        }

        /// <summary>(Development build: the ground and the time to the shore along a line from the camera's point of the sea: "east-step north-step count", metres.)</summary>
        public static string Shore(string words)
        {
            Sea s = instance;
            if (s == null || s.seabed == null) return "no seabed yet";
            string[] w = (words ?? "").Split(' ');
            double stepX = w.Length > 0 && w[0] != "" ? double.Parse(w[0], System.Globalization.CultureInfo.InvariantCulture) : 4.0;
            double stepZ = w.Length > 1 ? double.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
            int count = w.Length > 2 ? int.Parse(w[2]) : 30;
            return "from the camera's point, ground/seconds to the shore every " + stepX + ", " + stepZ + " m: " + s.seabed.Line(s.at.x, s.at.y, stepX, stepZ, count) + "; " + s.seabed.Waiting + " points still to ask";
        }

        /// <summary>(Development build: what one of the game's own types has: its fields, properties and methods whose names hold a word: "PartBuoyancy" or "PhysicsGlobals Buoyancy".)</summary>
        public static string Api(string words)
        {
            string[] w = (words ?? "").Split(' ');
            Type type = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { foreach (Type t in a.GetTypes()) if (t.Name == w[0]) { type = t; break; } } catch { }
                if (type != null) break;
            }
            if (type == null) return "no type " + w[0];
            string word = w.Length > 1 ? w[1].ToLowerInvariant() : "";
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            object one = null;
            PropertyInfo instanceOf = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceOf != null) try { one = instanceOf.GetValue(null, null); } catch { }
            var b = new System.Text.StringBuilder(type.FullName + (type.BaseType != null ? " : " + type.BaseType.Name : "") + "\n");
            foreach (FieldInfo f in type.GetFields(All))
            {
                if (word != "" && !f.Name.ToLowerInvariant().Contains(word)) continue;
                string value = "";
                try { if (f.IsStatic) value = " = " + f.GetValue(null); else if (one != null) value = " = " + f.GetValue(one); } catch { }
                b.Append(f.IsPublic ? "  " : "  (private) ").Append(f.IsStatic ? "static " : "").Append(f.FieldType.Name).Append(' ').Append(f.Name).Append(value).Append("\n");
            }
            foreach (PropertyInfo q in type.GetProperties(All))
            {
                if (word != "" && !q.Name.ToLowerInvariant().Contains(word)) continue;
                string value = "";
                try { MethodInfo g = q.GetGetMethod(true); if (g != null && g.GetParameters().Length == 0) { if (g.IsStatic) value = " = " + q.GetValue(null, null); else if (one != null) value = " = " + q.GetValue(one, null); } } catch { }
                b.Append("  property ").Append(q.PropertyType.Name).Append(' ').Append(q.Name).Append(q.CanWrite ? " (can be set)" : "").Append(value).Append("\n");
            }
            foreach (MethodInfo m2 in type.GetMethods(All))
            {
                if (m2.IsSpecialName || (word != "" && !m2.Name.ToLowerInvariant().Contains(word))) continue;
                b.Append(m2.IsPublic ? "  " : "  (private) ").Append(m2.IsStatic ? "static " : "").Append(m2.ReturnType.Name).Append(' ').Append(m2.Name).Append('(');
                ParameterInfo[] ps = m2.GetParameters();
                for (int k = 0; k < ps.Length; k++) b.Append(k > 0 ? ", " : "").Append(ps[k].ParameterType.Name).Append(' ').Append(ps[k].Name);
                b.Append(")\n");
            }
            return b.ToString();
        }

        /// <summary>(Development build: the craft being flown against the water: how high its middle is, how high the water stands there, how fast it moves, whether the game would let it be saved.)</summary>
        public static string Craft()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double alt = v.mainBody.GetAltitude(v.CoMD);
            float water = WaterAt(v.CoMD, out Vector3 flow);
            Vector3d up = (v.CoMD - v.mainBody.position).normalized;
            float drag = v.rootPart != null && v.rootPart.rb != null ? v.rootPart.rb.drag : -1f;
            return alt.ToString("F2", inv) + " " + water.ToString("F2", inv) + " " + v.srfSpeed.ToString("F2", inv) + " " + v.verticalSpeed.ToString("F2", inv) + " " + v.situation + " " + v.IsClearToSave() + " " + (v.IsRecoverable ? "recoverable" : "not-recoverable")
                + " " + Vector3.Dot(flow, (Vector3)up).ToString("F2", inv) + " " + drag.ToString("F2", inv);
        }

        /// <summary>
        /// (Development build: what one of the game's own methods does, as the steps it is compiled to, with the names of what
        /// it reads, writes and calls: "PartBuoyancy FixedUpdate". For working with the game's own reckoning, not round it.)
        /// </summary>
        public static string Il(string words)
        {
            string[] w = (words ?? "").Split(' ');
            Type type = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { foreach (Type t in a.GetTypes()) if (t.Name == w[0]) { type = t; break; } } catch { }
                if (type != null) break;
            }
            if (type == null || w.Length < 2) return "no type " + w[0];
            MethodInfo method = type.GetMethod(w[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (method == null) return "no method " + w[1];
            MethodBody body = method.GetMethodBody();
            byte[] il = body.GetILAsByteArray();
            var one = new Dictionary<short, System.Reflection.Emit.OpCode>();
            foreach (FieldInfo f in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var op = (System.Reflection.Emit.OpCode)f.GetValue(null);
                one[op.Value] = op;
            }
            var b = new System.Text.StringBuilder();
            Module module = method.Module;
            Type[] typeArgs = type.IsGenericType ? type.GetGenericArguments() : null, methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            int at = 0;
            int from = w.Length > 2 ? int.Parse(w[2]) : 0, to = w.Length > 3 ? int.Parse(w[3]) : int.MaxValue;
            while (at < il.Length)
            {
                int began = at;
                short code = il[at++];
                if (code == 0xFE) code = (short)(0xFE00 | il[at++]);
                if (!one.TryGetValue(code, out System.Reflection.Emit.OpCode opcode)) { b.Append(began.ToString("X4")).Append(" ?\n"); continue; }
                string operand = "";
                switch (opcode.OperandType)
                {
                    case System.Reflection.Emit.OperandType.InlineNone: break;
                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget: operand = "-> " + (at + 1 + (sbyte)il[at]).ToString("X4"); at += 1; break;
                    case System.Reflection.Emit.OperandType.InlineBrTarget: operand = "-> " + (at + 4 + BitConverter.ToInt32(il, at)).ToString("X4"); at += 4; break;
                    case System.Reflection.Emit.OperandType.ShortInlineI: case System.Reflection.Emit.OperandType.ShortInlineVar: operand = il[at].ToString(); at += 1; break;
                    case System.Reflection.Emit.OperandType.InlineVar: operand = BitConverter.ToInt16(il, at).ToString(); at += 2; break;
                    case System.Reflection.Emit.OperandType.InlineI: operand = BitConverter.ToInt32(il, at).ToString(); at += 4; break;
                    case System.Reflection.Emit.OperandType.InlineI8: operand = BitConverter.ToInt64(il, at).ToString(); at += 8; break;
                    case System.Reflection.Emit.OperandType.ShortInlineR: operand = BitConverter.ToSingle(il, at).ToString("R", System.Globalization.CultureInfo.InvariantCulture); at += 4; break;
                    case System.Reflection.Emit.OperandType.InlineR: operand = BitConverter.ToDouble(il, at).ToString("R", System.Globalization.CultureInfo.InvariantCulture); at += 8; break;
                    case System.Reflection.Emit.OperandType.InlineString: try { operand = "\"" + module.ResolveString(BitConverter.ToInt32(il, at)) + "\""; } catch { operand = "(string)"; } at += 4; break;
                    case System.Reflection.Emit.OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, at); at += 4 + 4 * n; operand = "(switch of " + n + ")"; break; }
                    default:
                        try
                        {
                            MemberInfo m = module.ResolveMember(BitConverter.ToInt32(il, at), typeArgs, methodArgs);
                            operand = (m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name;
                        }
                        catch { operand = "(member)"; }
                        at += 4; break;
                }
                if (began >= from && began <= to) b.Append(began.ToString("X4")).Append(' ').Append(opcode.Name).Append(' ').Append(operand).Append("\n");
            }
            return b.ToString();
        }

        /// <summary>(Development build: the numbers the white water goes by, set while the game runs; and "fetch 150": how many
        /// kilometres of open water the wind is taken to have blown over.)</summary>
        static void TuneFetch(float km) { Reach = km * 1000f; }

        /// <summary>(Development build: the numbers the white water goes by, set while the game runs: "eased 0.2 runs 0.5 ragged 0.14
        /// soft 0.05 longer 0.5 outright 0.1 show 0"; with nothing, what they are.)</summary>
        public static string Tune(string words)
        {
            string[] w = (words ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int n = 0; n + 1 < w.Length; n += 2)
            {
                float v = float.Parse(w[n + 1], System.Globalization.CultureInfo.InvariantCulture);
                switch (w[n])
                {
                    case "eased": Foam.Eased = v; break;
                    case "ragged": Foam.Ragged = v; break;
                    case "soft": Foam.Soft = v; break;
                    case "runs": Foam.Runs = v; break;
                    case "band": Foam.Band = v; break;
                    case "longer": Waves.Longer = v; break;
                    case "outright": Waves.Outright = v; break;
                    case "show": Foam.Show = v; break;
                    case "fetch": TuneFetch(v); break;
                    case "climbs": Climbs = v; break;
                    case "mapped": Mapped = v; break;
                    case "lens": LensShow = v; break;
                    case "rays": Rays = v; break;
                    case "curl": Curl = v; break;
                    case "lip": Lip = v; break;
                    case "white": if (instance != null && instance.waves != null) instance.waves.White = v; break;
                    case "plain": Waves.PlainTimes = v; break;
                    case "streaks": Foam.Streaks = v; break;
                    case "rows": Foam.Rows = v; break;
                    case "lasts": Foam.Lasts = v; break;
                    case "spray": Spray.Much = v; break;
                    default: return "no such number: " + w[n];
                }
            }
            return "eased " + Foam.Eased + " runs " + Foam.Runs + " band " + Foam.Band + " ragged " + Foam.Ragged + " soft " + Foam.Soft + " longer " + Waves.Longer + " outright " + Waves.Outright + " plain " + Waves.PlainTimes + " streaks " + Foam.Streaks + " fetch " + Reach / 1000f + " rows " + Foam.Rows + " lasts " + Foam.Lasts + " spray " + Spray.Much + " show " + Foam.Show;
        }

        /// <summary>
        /// (Development build: whether the small copy of the white water, which is read back from the graphics card for the
        /// spray, lies the way the maps themselves do. "lay 40 10": white water laid for four seconds 40 m along the sea's
        /// first axis and 10 m along its second from the camera's point of the sea, as a hull would churn it. "read": where
        /// the copy has water breaking this moment, as the spray takes it, in the same measure. The two must agree: a
        /// picture read back can come the other way up on one system than on another (asked of Direct3D, 2026-10-11).)
        /// </summary>
        public static string SeenTest(string words)
        {
            string[] w = (words ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (w.Length >= 3 && w[0] == "lay")
            {
                seenMark = new Vector2(float.Parse(w[1], inv), float.Parse(w[2], inv));
                seenMarkUntil = Time.unscaledTime + 4f;
                return "white water laid at " + seenMark.x.ToString("F1", inv) + ", " + seenMark.y.ToString("F1", inv) + " for four seconds";
            }
            Sea s = instance;
            if (s == null || s.foam == null) return "no white water yet";
            Unity.Collections.NativeArray<Color32> seen = s.foam.Seen(out bool there);
            if (!there) return "nothing read back yet";
            var b = new System.Text.StringBuilder("laid at " + seenMark.x.ToString("F1", inv) + ", " + seenMark.y.ToString("F1", inv) + (Time.unscaledTime < seenMarkUntil ? " (still being laid)" : " (no longer laid)") + "; breaking, as read back:");
            for (int l = 0; l < 3; l++)
            {
                double sumX = 0.0, sumZ = 0.0; int count = 0;
                for (int j = 0; j < Foam.SeenAcross; j++)
                    for (int i = 0; i < Foam.SeenAcross; i++)
                    {
                        Color32 c = seen[j * Foam.SeenAcross + i];
                        if ((l == 0 ? c.r : l == 1 ? c.g : c.b) < 236) continue;
                        s.foam.SeenWhere(l, i, j, out double x, out double z);
                        sumX += x - s.at.x; sumZ += z - s.at.y; count++;
                    }
                b.Append(" map ").Append(l).Append(" (").Append(s.foam.Texel[l].ToString("F3", inv)).Append(" m): ").Append(count).Append(" points");
                if (count > 0) b.Append(", their middle at ").Append((sumX / count).ToString("F1", inv)).Append(", ").Append((sumZ / count).ToString("F1", inv));
                b.Append(";");
            }
            return b.ToString();
        }
        static Vector2 seenMark;
        static float seenMarkUntil = -1f;

        /// <summary>
        /// (Development build: whether the lens's map of the water's height round the glass lies the way the lens's pass
        /// reads it. "keep": from now on the map is drawn once more each frame into a picture that is kept. "read": that
        /// picture read back and held against the waves' own heights at the same places, four ways: as the pass reads it,
        /// with the sea's second axis turned over, with its first, with both. The first must be the nearest by far. Stop the
        /// clock first (PhotoStudio.Exhibit.Still "on"), or the sea has moved between the drawing and the asking. "drop":
        /// no more kept. Asked of Direct3D, 2026-10-11: there a picture's first row is its top, and depth runs the other way.)
        /// </summary>
        public static string MapTest(string words)
        {
            words = (words ?? "").Trim();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (words == "keep")
            {
                if (mapKept == null)
                {
                    mapKept = new RenderTexture(MapSide, MapSide, 16, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { name = "Natural Ocean: the lens's map, kept for a test", filterMode = FilterMode.Point };
                    mapKept.Create();
                }
                return "the lens's map is kept from now on";
            }
            if (words == "drop")
            {
                if (mapKept != null) { mapKept.Release(); Destroy(mapKept); mapKept = null; }
                return "no map kept";
            }
            Sea s = instance;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (s == null || mapKept == null || mapFrame < 0 || body == null) return "no map kept yet (\"keep\" first, with the lens at the water)";
            var read = new Texture2D(MapSide, MapSide, TextureFormat.RGBAFloat, false, true);
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = mapKept;
            read.ReadPixels(new Rect(0, 0, MapSide, MapSide), 0, 0, false);
            RenderTexture.active = before;
            Color[] px = read.GetPixels();
            Destroy(read);
            double glassHigh = body.GetAltitude((Vector3d)mapGlass);
            var sums = new double[4]; var means = new double[4];
            int there = 0, asked = 0;
            double least = 1e9, most = -1e9;
            const int Step = 8;
            for (int j = Step / 2; j < MapSide; j += Step)
                for (int i = Step / 2; i < MapSide; i += Step)
                {
                    asked++;
                    Color c = px[j * MapSide + i];
                    if (c.a < 0.999f) continue;
                    there++;
                    least = Math.Min(least, c.r); most = Math.Max(most, c.r);
                    float x = ((i + 0.5f) / MapSide * 2f - 1f) * mapHalf, y = ((j + 0.5f) / MapSide * 2f - 1f) * mapHalf;
                    for (int way = 0; way < 4; way++)
                    {
                        Vector3 place = mapGlass + s.lensFirst * ((way & 2) != 0 ? -x : x) + s.lensSecond * ((way & 1) != 0 ? -y : y);
                        double waves = HeightAt((Vector3d)place) - glassHigh;
                        sums[way] += (c.r - waves) * (c.r - waves); means[way] += c.r - waves;
                    }
                }
            if (there == 0) return "the map kept has nothing of the sea in it (" + asked + " places asked; drawn at frame " + mapFrame + ", now " + Time.frameCount + ")";
            return "the map kept (half a side " + mapHalf.ToString("F3", inv) + " m, drawn " + (Time.frameCount - mapFrame) + " frames ago): the sea at " + there + " of " + asked + " places, standing " + least.ToString("F3", inv) + " to " + most.ToString("F3", inv)
                + " m over the middle of the glass, " + (means[0] / there).ToString("F3", inv) + " m higher in the mean than the waves' own numbers have it; its shape off theirs by (root mean square with that mean taken out, m): as read "
                + Shape(0) + "; second axis turned over " + Shape(1) + "; first axis turned over " + Shape(2) + "; both " + Shape(3);
            string Shape(int way) => Math.Sqrt(Math.Max(sums[way] / there - (means[way] / there) * (means[way] / there), 0.0)).ToString("F4", inv);
        }
        static RenderTexture mapKept;
        static Vector3 mapGlass;
        static float mapHalf;
        static int mapFrame = -1;

        /// <summary>(Development build: the white water's four maps written to a picture.)</summary>
        public static string FoamDump(string path) => instance != null && instance.foam != null ? instance.foam.Dump(path) : "no foam yet";

        /// <summary>(Development build: what the sea is doing and what it costs.)</summary>
        public static string Say()
        {
            Sea s = instance;
            if (s == null || s.waves == null) return Report + " (no waves yet) " + why;
            var b = new System.Text.StringBuilder(Report);
            b.Append(lightSaid);
            if (LensOn) b.Append(" ").Append(LensSaid);
            b.Append(" The game keeps its picture as ").Append(QualitySettings.activeColorSpace).Append(".");
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (body != null && cam != null && Planetarium.fetch != null)
            {
                Vector3d upS = ((Vector3d)cam.transform.position - body.position).normalized, sun = ((Vector3d)Planetarium.fetch.Sun.position - (Vector3d)cam.transform.position).normalized;
                Vector3d east = Vector3d.Cross(upS, body.GetSurfaceNVector(90.0, 0.0)).normalized, north = Vector3d.Cross(east, upS);
                b.Append(" The sun stands ").Append((Math.Asin(Vector3d.Dot(sun, upS)) * 57.2958).ToString("F0")).Append(" degrees up, heading ").Append(((Math.Atan2(Vector3d.Dot(sun, east), Vector3d.Dot(sun, north)) * 57.2958 + 360.0) % 360.0).ToString("F0")).Append(".");
            }
            b.Append(" Breaking: ").Append((100f * s.waves.Breaking).ToString("F2")).Append("% of the sea at a time; each size of wave where squeezed below");
            for (int n = 0; n < s.waves.Levels.Length; n++) b.Append(n == 0 ? " " : ", ").Append(s.waves.Levels[n] < -8f ? "never" : s.waves.Levels[n].ToString("F2") + " to begin, " + s.waves.Plain[n].ToString("F2")).Append(" (").Append((100f * s.waves.Shares[n]).ToString("F0")).Append("%)");
            b.Append(". All slopes ").Append(s.waves.RoughNow.ToString("F4"))
             .Append(" in the mean square (Cox and Munk: ").Append((0.003 + 0.00512 * s.sea.Wind).ToString("F4")).Append("); the fourth power to ").Append(s.waves.Stretch.ToString("F1")).Append(" peaks, ripples times ").Append(s.waves.Ripples.ToString("F2")).Append(".");
            b.Append(" The waves took ").Append(s.waves.Took.ToString("F1")).Append(" ms on their threads. Patches (as shaped, then as measured):");
            foreach (Cascade c in s.waves.Patch)
            {
                double sum = 0.0, sum2 = 0.0; float least = 1e9f, most = -1e9f, fold = 1e9f; int all = c.N * c.N;
                for (int k = 0; k < all; k++) { float h = c.Moved[4 * k + 1]; sum += h; sum2 += (double)h * h; least = Mathf.Min(least, h); most = Mathf.Max(most, h); fold = Mathf.Min(fold, c.Moved[4 * k + 3]); }
                double mean = sum / all;
                b.Append("\n  ").Append(c.Side.ToString("F2")).Append(" m: ").Append(c.Height.ToString("F3")).Append(" m high, slope ").Append(Mathf.Sqrt(c.Roughness).ToString("F3"))
                 .Append("; ").Append((4.0 * Math.Sqrt(Math.Max(sum2 / all - mean * mean, 0.0))).ToString("F3")).Append(" m (").Append(least.ToString("F2")).Append(" to ").Append(most.ToString("F2")).Append("), most squeezed ").Append(fold.ToString("F2"));
            }
            b.Append("\n");
            if (s.foam != null) b.Append("\n").Append(s.foam.Measure()).Append("\n");
            if (s.weed != null) b.Append(" Seaweed: ").Append(s.weed.Plants).Append(" plants in ").Append(s.weed.Plots).Append(" squares.");
            if (s.spray != null) b.Append(" Spray: ").Append(s.spray.Bursts).Append(" bursts a second, ").Append(s.spray.Alive).Append(" puffs in the air, lit ").Append(Spray.LastLight.r.ToString("F2")).Append(" ").Append(Spray.LastLight.g.ToString("F2")).Append(" ").Append(Spray.LastLight.b.ToString("F2")).Append(".");
            b.Append(" Floating: ").Append(Floating.Lifted).Append(" parts told a water level, from ").Append(Floating.Lowest.ToString("F2")).Append(" to ").Append(Floating.Highest.ToString("F2")).Append(" m.");
            if (s.seabed != null) b.Append(" The ground under the camera's point of the sea stands ").Append(s.seabed.At(s.at.x, s.at.y).ToString("F1")).Append(" m against the sea's level; ").Append(s.seabed.Waiting).Append(" points of its maps still to ask.");
            b.Append(" at ").Append(s.at.x.ToString("F0")).Append(", ").Append(s.at.y.ToString("F0")).Append("; under the camera the water stands ").Append(HeightAt((Vector3d)FlightCamera.fetch.mainCamera.transform.position).ToString("F2")).Append(" m; other seas: ").Append(OtherSeas.Report);
            return b.ToString();
        }
#endif
    }

    /// <summary>
    /// The other seas, put out of sight where this one is drawn. The game's own (a second planet-sized mesh): its pieces
    /// kept from being drawn (see Put). Scatterer's: drawn with nothing of it showing, by the two numbers its sea's material has for
    /// fading the sea out as the planet's own picture takes over from orbit (how much of it shows, and whether it writes
    /// how far off it is), set to nothing just before each camera draws, after Scatterer has set them for that camera.
    /// Scatterer goes on working its sea out, which costs what it costs; switching its sea off in Scatterer's own settings
    /// saves that. (Its settings are never touched from here: it writes them back to the user's file. Its sea's meshes are
    /// not switched off, as was tried first: something it does once a frame hangs on their being drawn, and without it the
    /// screen kept an old picture of whatever had been near the camera. Nor can its sea be sunk under ours by the radius
    /// its material is given, tried second: where its sea stands is worked out before that.)
    /// </summary>
    static class OtherSeas
    {
        public static string Report = "none looked for";
        // (the game's own sea: its spheres on this world, and the pieces of them that are kept from being drawn just now)
        static readonly List<PQS> stockSeas = new List<PQS>();
        static HashSet<MeshRenderer> kept = new HashSet<MeshRenderer>(), keptNow = new HashSet<MeshRenderer>();
        static bool stockHooked;
        static readonly List<Material> faded = new List<Material>();
        static float looked = -10f;
        static Type scatterers;
        static FieldInfo scatterersMaterial, scatterersUnder, scatterersIs;
        static MethodInfo scatterersToggle;
        static readonly List<UnityEngine.Object> nodes = new List<UnityEngine.Object>();
        static bool scattererSought, hooked;

        public static void Away(CelestialBody body)
        {
            // ---- the game's own sea: which spheres it is on this world (their pieces are seen to as each camera sets out: Put)
            stockSeas.Clear();
            if (body != null && body.pqsController != null)
            {
                PQS[] seas = body.pqsController.ChildSpheres;
                if (seas != null) for (int n = 0; n < seas.Length; n++) if (seas[n] != null) stockSeas.Add(seas[n]);
            }
            if (!stockHooked) { Camera.onPreCull += Put; stockHooked = true; }
            if (Time.unscaledTime - looked < 1f) return;
            looked = Time.unscaledTime;
            if (!scattererSought)
            {
                scattererSought = true;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                {
                    Type t = a.assembly != null ? a.assembly.GetType("Scatterer.OceanNode", false) : null;
                    if (t != null)
                    {
                        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                        scatterers = t; scatterersMaterial = t.GetField("m_oceanMaterial", Any);
                        scatterersUnder = t.GetField("underwaterMode", Any); scatterersIs = t.GetField("isUnderwater", Any);
                        scatterersToggle = t.GetMethod("toggleUnderwaterMode", Any, null, Type.EmptyTypes, null);
                    }
                }
            }
            faded.Clear(); nodes.Clear();
            if (scatterers != null && scatterersMaterial != null)
                foreach (UnityEngine.Object node in UnityEngine.Object.FindObjectsOfType(scatterers))
                {
                    try { if (scatterersMaterial.GetValue(node) is Material m && m != null && m.HasProperty("_PlanetOpacity")) { faded.Add(m); nodes.Add(node); } }
                    catch (Exception) { }
                }
            if (faded.Count > 0 && !hooked) { Camera.onPreRender += Fade; hooked = true; }
            Report = (kept.Count > 0 ? kept.Count + " pieces of the game's sea kept from being drawn; " : "") + (scatterers == null ? "no Scatterer" : faded.Count + " of Scatterer's seas drawn with nothing showing");
        }

        /// <summary>
        /// The game's own sea kept from being drawn: every piece of it, as each camera that could see it sets out, when the
        /// game has made, cut up and joined its pieces for the frame.
        ///
        /// The pieces are found as the game itself keeps them: a tree of squares, each cut into four as the camera nears.
        /// They were first looked for among the things under the sea's own object, and their renderers switched off once a
        /// second. On Windows without Scatterer (2026-10-11) that found the pieces the scene began with and no others: the
        /// pieces the game makes as the camera moves are kept under another object altogether ("PQStorage"), and the game's
        /// sea was drawn through ours wherever ours stood lower, in sheets along the swell, edged white (our sea took it for
        /// a bottom a hand's breadth down). And a piece switched off is switched on again by the game when it next comes
        /// into view. So each piece's renderer is told not to draw (which the game never touches), every frame.
        ///
        /// The game keeps its pieces in one store for every sphere of every world, and a piece that leaves the sea may be
        /// the land's next: so what is kept from being drawn is remembered, and whatever is no longer a piece of the sea is
        /// let go at once.
        /// </summary>
        static void Put(Camera cam)
        {
            // (the game's sea is on the layer for water, the fifth: a camera that leaves that out has nothing of it to draw)
            if (cam == null || (cam.cullingMask & (1 << 4)) == 0 || stockSeas.Count == 0) return;
            keptNow.Clear();
            for (int n = 0; n < stockSeas.Count; n++)
            {
                PQS sea = stockSeas[n];
                if (sea == null || sea.quads == null) continue;
                for (int k = 0; k < sea.quads.Length; k++) PutAway(sea, sea.quads[k], 0);
            }
            foreach (MeshRenderer r in kept) if (r != null && !keptNow.Contains(r)) r.forceRenderingOff = false;
            HashSet<MeshRenderer> was = kept; kept = keptNow; keptNow = was;
        }

        static void PutAway(PQS sea, PQ piece, int deep)
        {
            // (a piece's four parts are only its own while it is cut up: afterwards they go back to the store)
            if (piece == null || piece.sphereRoot != sea || deep > 32) return;
            MeshRenderer r = piece.meshRenderer;
            if (r != null) { if (!r.forceRenderingOff) r.forceRenderingOff = true; keptNow.Add(r); }
            PQ[] parts = piece.isSubdivided ? piece.subNodes : null;
            if (parts != null) for (int k = 0; k < parts.Length; k++) PutAway(sea, parts[k], deep + 1);
        }

        /// <summary>
        /// Scatterer's own picture of being under water, kept off. Below its sea's level Scatterer turns the whole picture
        /// over: its haze of the air goes, a green-blue fog by distance comes over everything, the sun is dimmed. Whole
        /// picture or none, by where the middle of the camera is: with the water's surface across the glass the sky above
        /// the line came out green. The lens's own pass (Sea.Lens) lays the water where the glass is wet, and only there; so
        /// each frame, after Scatterer has decided, its switch is thrown back by its own means (the same method it throws it
        /// with), and it throws it again the next frame, and so on while the camera is under. None of its settings is
        /// touched, and with this sea off it goes on as it would.
        /// </summary>
        public static void Dry()
        {
            if (scatterersToggle == null || scatterersUnder == null) return;
            for (int n = 0; n < nodes.Count; n++)
            {
                UnityEngine.Object node = nodes[n];
                if (node == null) continue;
                try
                {
                    if ((bool)scatterersUnder.GetValue(node)) scatterersToggle.Invoke(node, null);
                    if (scatterersIs != null && (bool)scatterersIs.GetValue(node)) scatterersIs.SetValue(node, false);
                }
                catch (Exception) { }
            }
        }

        static void Fade(Camera cam)
        {
            for (int n = 0; n < faded.Count; n++)
            {
                Material m = faded[n];
                if (m == null) continue;
                m.SetFloat("_PlanetOpacity", 0f);
                m.SetInt("_ZwriteVariable", 0);
            }
        }

#if DEV
        /// <summary>(Development build: the game's own sea as it stands this moment: its spheres, their pieces, and how many of the pieces' renderers are on.)</summary>
        public static string Look()
        {
            var b = new System.Text.StringBuilder();
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null || body.pqsController == null) return "no planet";
            foreach (PQS child in body.pqsController.ChildSpheres)
            {
                if (child == null) continue;
                MeshRenderer[] all = child.GetComponentsInChildren<MeshRenderer>(true);
                b.Append(child.name).Append(": ").Append(child.isActiveAndEnabled ? "on" : "off").Append(", ").Append(all.Length).Append(" renderers under its own object")
                 .Append("; material ").Append(child.surfaceMaterial != null ? child.surfaceMaterial.name + " (" + (child.surfaceMaterial.shader != null ? child.surfaceMaterial.shader.name : "no shader") + ")" : "none").Append("\n");
            }
            b.Append("pieces kept from being drawn: ").Append(kept.Count).Append("\n");
            // (and every renderer in the scene that is on, is named like a sea and is not kept from being drawn, whoever it belongs to)
            int others = 0, drawn = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || r.name.IndexOf("cean", StringComparison.OrdinalIgnoreCase) < 0) continue;
                others++;
                if (r.forceRenderingOff) continue;
                if (drawn++ >= 4) continue;
                b.Append("drawn: ").Append(r.name).Append(" (").Append(r.GetType().Name).Append(", layer ").Append(r.gameObject.layer).Append(", material ").Append(r.sharedMaterial != null ? r.sharedMaterial.name + " / " + (r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "?") : "none").Append("), under");
                for (Transform t = r.transform.parent; t != null; t = t.parent) b.Append(" < ").Append(t.name);
                b.Append("\n");
            }
            b.Append(others).Append(" renderers named like a sea are switched on, ").Append(drawn).Append(" of them drawn");
            return b.ToString();
        }
#endif

        public static void Back()
        {
            if (stockHooked) { Camera.onPreCull -= Put; stockHooked = false; }
            foreach (MeshRenderer r in kept) if (r != null) r.forceRenderingOff = false;
            kept.Clear(); keptNow.Clear(); stockSeas.Clear();
            // (Scatterer sets both numbers again for every camera: nothing to put back but the hook)
            if (hooked) { Camera.onPreRender -= Fade; hooked = false; }
            faded.Clear(); nodes.Clear();
            looked = -10f;
        }
    }
}
