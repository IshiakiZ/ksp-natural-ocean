// NaturalOcean/Sea: made from tools/shaderpack/ocean.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalOcean/Sea"
{
    Properties
    {
        _OceanMoved ("OceanMoved", 2DArray) = "" {}
        _OceanSlope ("OceanSlope", 2DArray) = "" {}
        _OceanFoamMap ("OceanFoamMap", 2D) = "" {}
        _OceanLace ("OceanLace", 2D) = "" {}
        _OceanMirror ("OceanMirror", 2D) = "" {}
        _OceanSeabed ("OceanSeabed", 2D) = "" {}
        _OceanSurfTime ("OceanSurfTime", 2D) = "" {}
        _OceanSurfDeep ("OceanSurfDeep", 2D) = "" {}
        _OceanFarDepth ("OceanFarDepth", 2D) = "" {}
        _OceanFarScene ("OceanFarScene", 2D) = "" {}
        _OceanDent ("OceanDent", 2D) = "" {}
        _OceanDentMove ("OceanDentMove", 2D) = "" {}
        _OceanWake ("OceanWake", 2D) = "" {}
        _OceanWakeMove ("OceanWakeMove", 2D) = "" {}
        _WeatherShadowMap ("WeatherShadowMap", 2D) = "" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma only_renderers d3d11 glcore metal vulkan
            #include "UnityCG.cginc"

            // (the GLSL has each of the game's matrices as its four columns; HLSL has them as rows)
            #define hlslcc_mtx4x4unity_ObjectToWorld transpose(unity_ObjectToWorld)
            #define hlslcc_mtx4x4unity_WorldToObject transpose(unity_WorldToObject)
            #define hlslcc_mtx4x4unity_MatrixVP transpose(UNITY_MATRIX_VP)
            #define hlslcc_mtx4x4unity_MatrixV transpose(UNITY_MATRIX_V)
            #define SampleLod(S, uv, lod) S.SampleLevel(sampler##S, uv, lod)
            #define SampleFlat(S, uv) S.SampleLevel(sampler##S, uv, 0.0)
            #define SampleGradient(S, uv, dx, dy) S.SampleGrad(sampler##S, uv, dx, dy)
            #define Mod(x, y) ((x) - (y) * floor((x) / (y)))
            #define GreaterThan(a, b) ((a) > (b))
            #define GreaterThanEqual(a, b) ((a) >= (b))
            #define LessThan(a, b) ((a) < (b))
            #define LessThanEqual(a, b) ((a) <= (b))
            float4 TexelFetch(Texture2D t, int2 p, int lod) { return t.Load(int3(p, lod)); }
            float4 TexelFetch(Texture3D t, int3 p, int lod) { return t.Load(int4(p, lod)); }
            int2 TextureSize(Texture2D t, int lod) { uint w, h; t.GetDimensions(w, h); return int2(w, h); }
            bool AllZero(float4 v) { return !any(v); }
            // (one of a vector's numbers, chosen by a variable, read and given a new value: see chosen in tools/unityshaders/port.py)
            float Pick(float2 v, int n) { return n <= 0 ? v.x : v.y; }
            float Pick(float3 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : v.z; }
            float Pick(float4 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : n == 2 ? v.z : v.w; }
            float2 Put(float2 v, int n, float x) { return float2(n == 0 ? x : v.x, n == 1 ? x : v.y); }
            float3 Put(float3 v, int n, float x) { return float3(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z); }
            float4 Put(float4 v, int n, float x) { return float4(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z, n == 3 ? x : v.w); }
            // gl_FragCoord: which pixel is being drawn, its rows counted from the bottom, as every picture's are read here. Direct3D counts
            // the rows of what it draws into from the top, and the game draws into its pictures upside down to make up for it (saying so with
            // _ProjectionParams.x at -1): so where it is drawing straight to the screen, the row is turned over, or each picture read by pixel
            // (the camera's depth, the smoke drawn small) would be read upside down.
            float4 FragCoord(float4 pos)
            {
            #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x > 0.0) pos.y = _ScreenParams.y - pos.y;
            #endif
                return pos;
            }

            float4 _OceanMid;     // xyz: the point of the sea under the camera, in the scene (m); w: the sea's radius (m)
            float4 _OceanAxisA;     // xyz: the sea's first level axis, in the scene; w: half the number of cells across a ring
            float4 _OceanAxisB;     // xyz: its second; w: how many times further out the outermost ring's edge is pushed (to the horizon)
            float4 _OceanLay;     // the patches of waves: x one over the largest one's side (1/m); y how many times smaller each next one is; z points across one; w how many of them are large enough to break white
            float4 _OceanTwin;     // the second laying of every patch (Waves.cs): x its size against the first; y, z the cosine and sine of its turn; w what the two added are multiplied by
            float4 _OceanAt0;     // where that point is in each patch, in the patch's own sides: xy as first laid, zw as laid the second time
            float4 _OceanAt1;
            float4 _OceanAt2;
            float4 _OceanAt3;
            float4 _OceanAt4;
            float4 _OceanAt5;
            float4 _OceanAt6;
            float4 _OceanAt7;
            float4 _OceanShore;     // x: how much of the waves there is here (1: open sea); y: (development build) one thing shown alone; z: 1 if the lens's own pass lays the water between the camera and things under water (oceanunder.glsl); w: 1 while that pass is on (the glass at the water or under it)
            float4 _OceanWind;     // xy: the way the wind blows, on the sea's two axes; z: how hard, m/s; w: how far a breaking crest goes over (0 not at all; 1 as worked out: a little less on a light sea)
            float4 _OceanMapAt;     // nought but for one drawing of the innermost ring a frame while the lens is at the water: then xyz the middle of the camera's glass, in the scene, and w one over half the side (1/m) of a map of the water's height round it (see the end of this program)
            float4 _SeabedAtA;     // the maps of the ground under the sea (Seabed.cs): where the point of the sea under the camera is in maps 0 (xy) and 1 (zw), in pictures
            float4 _SeabedAtB;     // and in maps 2 and 3
            float4 _SeabedCell;     // how far apart each map's points are, m
            float4 _OceanHighA;     // how high each patch's waves are (the mean of the highest third, m): the four largest
            float4 _OceanHighB;     // and the four smallest
            float4 _OceanSurfA;     // the surf's two trains (the swell's and the wind sea's): x, z how long between crests (s); y, w how high out at sea (m)
            float4 _OceanSurfB;     // x, z: how far each train's count has got (the part of a period); y, w: how many whole periods have gone (wrapped)
            float4 _OceanAtSpot;     // xy: where the point of the sea under the camera is, m, wrapped every 1024 m (for patterns pinned to the water)
            float4 _FoamAtA;     // the white water's maps (Foam.cs): where the point of the sea under the camera is in maps 0 (xy) and 1 (zw), in pictures
            float4 _FoamTexel;     // how far apart each map's points are, m
            float4 _FoamReach;     // y: points across a map
            float4 _FoamAtB;     // and in maps 2 and 3
            float4 _OceanBreaks;     // the squeeze below which a breaker of each size of wave begins: the second patch to the fifth (-9: that size does not break), as the white water has it
            float4 _OceanBreakHow;     // x: how much less squeezed than that a crest still carries a running front; y: how much of the longer patches' squeeze counts with a patch's own; z: over how much squeeze a wave goes from not breaking to breaking outright; w: how large the lip a breaking crest throws is, against how high that size of wave runs
            float4 _OceanPlunge;     // how far past the level at which a crest of its size begins to break it must be squeezed to come over: x it begins to, y all of it (Sea.Plunge)
            float4 _OceanDentAt;     // a blow's dent (Dents.cs): xy its middle, m from the point under the camera on the sea's two axes; z one over its side, 1/m; w 1 if there is one
            float4 _OceanDentMid;     // the crater's middle, where it was deepest (xy, m from the point under the camera), and the way the craft came (zw): the dent's rings run out from there
            float4 _OceanDentCurl;     // a breaking ring's lip: x how large against how high the ring stands (0: none); y the dent's points apart, m; z how far the leading ring's lip has come down (0 to 1); w one over how much longer the crater was along the way the craft came
            float4 _OceanWakeAt;     // a hull's wake (Wakes.cs): xy its middle, m from the point under the camera on the sea's two axes; z one over its side, 1/m; w how much of it is shown (0: none)
            float4 _UnderLight;     // rgb: the water's own light where the camera is, as light: the same that the lens's pass fogs things under water with (oceanunder.glsl)
            float4 _OceanRoughA;     // the mean square slope of each patch's waves: the four largest
            float4 _OceanRoughB;     // and the four smallest
            float4 _OceanSun;     // xyz: towards the sun, in the scene; w: how much of it reaches the sea (0 night or deep shade, 1 clear)
            float4 _OceanSunTint;     // rgb: the sun's light on the sea (as light); w: the height of the waves (the mean of the highest third, m)
            float4 _OceanSkyTop;     // rgb: the sky overhead (as light); w: how grey the sky is (0 clear, 1 overcast)
            float4 _OceanSkyLow;     // rgb: the sky at the horizon; w: how far one can see along the sea (m)
            float4 _OceanWater;     // rgb: what a deep sea sends back of white light; w: how far down the bottom can still be seen (m)
            float4 _UnderwaterFogColor;     // the game's own: the colour it fogs things with under water (as numbers for the screen)
            float4 _WeedUp;     // which way is up at the point of the sea under the camera, in the planet's own turning frame; w: the sea's radius, m
            float4 _WeedFirst;     // the sea's first level axis in that frame
            float4 _WeedSecond;     // and its second
            float4 _OceanFoam;     // x: how readily the crests whiten (0 none; 1 as the wind has it); y: 1 if the game keeps its picture as numbers for the screen (gamma), 0 as light; z: the squeeze below which the water is breaking; w: the sea's clock, seconds (wrapped)
            float4 _OceanLaceAt;     // where the point of the sea under the camera is in the lace, at its two sizes
            float4 _OceanLaceAtB;     // the same for the lace as it is being turned to (the wind has veered)
            float4 _OceanLaceCount;     // how many whole times the lace has come round between where the sea's axes begin and the point under the camera, counted to 64 and begun again: xy at its large size, zw at its small
            float4 _OceanLaceCountB;     // the same for the lace as it is being turned to
            float4 _OceanLaceTurn;     // xy: the way the lace is laid (the wind's, in the sea's two axes: the lace is stretched along it); zw: the way it is being turned to
            float4 _OceanLaceMix;     // x: how far it has been turned to the second way (0 to 1); y: how many times longer along the wind than across; zw: spare
            float4 _OceanGust;     // gusts on the water and slicks: xy how far the gusts' pattern has drifted down the wind (m, wrapped every 1024); z how patchy the ripples are (0 even, 1 most); w how much calm slick there is
            float4 _OceanRain;     // x: how hard it rains on the sea here (0 to 1, 1 a cloudburst of 50 mm an hour); y: the clock for the drops' rings (s, wrapped)
            float4 _OceanStreak;     // the streaks of foam down the wind: x how much of the sea is streaked (0 none); y one over the width their pattern repeats at (1/m); z how many times shorter the pattern is taken along the wind
            float4 _OceanStreakAt;     // where the point of the sea under the camera is in the streaks' pattern: xy as the lace is laid, zw as it is being turned to
            float4 _WeatherShadowAt;     // xyz: the middle of the shadow's square (the world's axes and place, m); w: its side (m)
            float4 _WeatherShadowX;     // xyz: the square's one side; w: the height of the ground it lies on (m above the radius)
            float4 _WeatherShadowY;     // xyz: its other side
            float4 _WeatherShadowCentre;     // xyz: the planet's middle (the world's place); w: its radius (m)
            float4 _WeatherShadowFade;     // x: what the sun is dimmed by where the craft is; y: how far off the pattern fades to half (m); z: how far the camera is above all the clouds (0 to 1: the pattern taken by one less as much)
            float4 _WeatherShadowSun;     // xyz: towards the sun; w: 1 when the shadow is handed over (else the sea's own sun, _OceanSun.w, is dimmed for the whole sea)
            float4 _OceanMirrorX;     // the picture the game finished last frame, for mirroring: xyz, with the next two, turn a way in the scene into where that way was in the picture; w: 1 if there is such a picture
            float4 _OceanMirrorY;     // w: how many of the picture's points there are to a radian
            float4 _OceanMirrorW;     // w: how many smaller copies the picture has
            float4 _WeatherVeilAir;     // the weather in the air (Natural Light's veil, handed to every shader): x how thick (the light lost in each metre; 0: none); y its top, above the camera (m); z how soon it thins out above its top (m)
            float4 _WeatherVeilColour;     // rgb: its own light, lit from all round (as it shows)
            float4 _WeatherVeilSun;     // xyz: towards the sun, in the scene; w: how strongly what is in the air throws light forward
            float4 _WeatherVeilSunColour;     // rgb: what the sun adds to it looking straight towards the sun (as it shows)
            float4 _OceanFarZ;     // xy: that camera's own _ZBufferParams.zw; z: how far it sees, m; w: how far the near camera sees, m (0: one camera draws everything, and there is no such picture)
            Texture2DArray _OceanMoved; SamplerState sampler_OceanMoved;     // each patch: rgb how far the water at a point has moved (m: along the first axis, up, along the second); a: how squeezed
            Texture2D _OceanSeabed; SamplerState sampler_OceanSeabed;     // the ground's height against the sea's level, m: four maps, one in each number
            Texture2D _OceanSurfTime; SamplerState sampler_OceanSurfTime;     // how long a wave takes from each point to the shore, s: four maps, laid out as the seabed's
            Texture2D _OceanSurfDeep; SamplerState sampler_OceanSurfDeep;     // how deep the water is at each point as a wave feels it (taken over some metres round), m: laid out the same
            Texture2D _OceanLace; SamplerState sampler_OceanLace;     // the foam's lace (Foam.cs): its fourth number, slow blotches, puts the surf a little forward or back along the shore
            Texture2D _OceanFoamMap; SamplerState sampler_OceanFoamMap;     // the white water: four maps, one in each number (1: breaking this moment)
            Texture2D _OceanDent; SamplerState sampler_OceanDent;     // the dent: r how high the water stands, m; gb its slope along the sea's two axes (as it lies, moved sideways too); a how steep it is
            Texture2D _OceanDentMove; SamplerState sampler_OceanDentMove;     // the dent's water moved sideways (rg, m, along the sea's two axes); b where its rings break; a how steep it is
            Texture2D _OceanWake; SamplerState sampler_OceanWake;     // the wake: r how high the water stands, m; gb its slope along the sea's two axes (as it lies, moved sideways too); a how steep it is
            Texture2D _OceanWakeMove; SamplerState sampler_OceanWakeMove;     // the wake's water moved sideways (rg, m, along the sea's two axes); a how steep it is
            Texture2DArray _OceanSlope; SamplerState sampler_OceanSlope;     // each patch: rg the surface's slope along the two axes; b: how squeezed the water is (0 not at all, -1 to nothing); a: the two slopes' squares added
            Texture2D _WeatherShadowMap; SamplerState sampler_WeatherShadowMap;     // the clouds' shadow, as Natural Weather hands it over (r: how much cloud towards the sun; g, b: its lowest and highest, km above the radius)
            Texture2D _OceanMirror; SamplerState sampler_OceanMirror;     // that picture, half size, with smaller copies
            Texture2D _OceanScene; SamplerState sampler_OceanScene;     // what the camera had drawn before the sea (the ground and what stands on it)
            Texture2D _CameraDepthTexture; SamplerState sampler_CameraDepthTexture;
            Texture2D _OceanFarDepth; SamplerState sampler_OceanFarDepth;     // where the game draws with two cameras (OpenGL): the far camera's depth picture, kept for the near camera's sea (Sea.cs, FarDepth)
            Texture2D _OceanFarScene; SamplerState sampler_OceanFarScene;     // and that camera's picture as it was before it drew its own sea

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 vs_TEXCOORD0 : TEXCOORD0;
                float4 vs_TEXCOORD1 : TEXCOORD1;
                float4 vs_TEXCOORD2 : TEXCOORD2;
                float4 vs_TEXCOORD3 : TEXCOORD3;
                float4 vs_TEXCOORD4 : TEXCOORD4;
            };

            // Natural Ocean: the sea's surface. A mesh of square rings round the point of the sea under the camera (see Surface.cs),
            // each ring's cells twice the size of the last, is moved here by the waves (four patches of Fourier waves: Waves.cs) and
            // bent down over the planet's curve. The rings slide with the camera in whole cells, so a point of the mesh always
            // stands at the same place in the waves: nothing swims.

            // <surf> (this stretch is word for word the same in ocean.glsl and oceanfoam.glsl: glcheck_all.py sees that it stays so)
            // The ground's height against the sea's level at a place (m; below nought: under so much water), from the finest of the four
            // maps of it that reaches so far (Seabed.cs); deep water where none does.
            float groundAt(float2 spot)
            {
                float out_ = max(abs(spot.x), abs(spot.y));
                float ground = -1000.0;
                { [loop] for (int l = 3; l >= 0; l--)
                {
                    float span = 128.0 * Pick(_SeabedCell, l);
                    float inside = 1.0 - smoothstep(0.38, 0.46, out_ / span);
                    if (inside > 0.0)
                    {
                        float2 at = l == 0 ? _SeabedAtA.xy : l == 1 ? _SeabedAtA.zw : l == 2 ? _SeabedAtB.xy : _SeabedAtB.zw;
                        ground = lerp(ground, SampleLod(_OceanSeabed, at + spot / span, 0.0)[l], inside);
                    }
                } }
                return ground;
            }

            // How long a wave takes from a place to the shore (s), from the same maps' second picture; 5000 where no map knows of a shore.
            float shoreTimeAt(float2 spot)
            {
                float out_ = max(abs(spot.x), abs(spot.y));
                float time = 5000.0;
                { [loop] for (int l = 3; l >= 0; l--)
                {
                    float span = 128.0 * Pick(_SeabedCell, l);
                    float inside = 1.0 - smoothstep(0.38, 0.46, out_ / span);
                    if (inside > 0.0)
                    {
                        float2 at = l == 0 ? _SeabedAtA.xy : l == 1 ? _SeabedAtA.zw : l == 2 ? _SeabedAtB.xy : _SeabedAtB.zw;
                        float here = SampleLod(_OceanSurfTime, at + spot / span, 0.0)[l];
                        // (a map that knows of no shore says nothing against one that does)
                        time = time > 4000.0 ? here : here > 4000.0 ? time : lerp(time, here, inside);
                    }
                } }
                return time;
            }

            // How deep the water is at a place as a wave feels it (m): taken over some metres round, land counting as no depth. The
            // game's seabed is all bumps a few metres across, which a wave ten times as long passes over without noticing.
            float feltDepthAt(float2 spot)
            {
                float out_ = max(abs(spot.x), abs(spot.y));
                float deep = 1000.0;
                { [loop] for (int l = 3; l >= 0; l--)
                {
                    float span = 128.0 * Pick(_SeabedCell, l);
                    float inside = 1.0 - smoothstep(0.38, 0.46, out_ / span);
                    if (inside > 0.0)
                    {
                        float2 at = l == 0 ? _SeabedAtA.xy : l == 1 ? _SeabedAtA.zw : l == 2 ? _SeabedAtB.xy : _SeabedAtB.zw;
                        deep = lerp(deep, SampleLod(_OceanSurfDeep, at + spot / span, 0.0)[l], inside);
                    }
                } }
                return deep;
            }

            // A number from 0 to 1 that three whole numbers give by chance, the same every time for the same three.
            float surfChance(uint3 v)
            {
                v = v * 1664525u + 1013904223u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                v ^= v >> 16u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                return float(v.x >> 8u) / 16777216.0;
            }

            // The surf at a place whose ground stands so high against the sea's level, where the water is so deep as a wave feels it,
            // and from which a wave takes so long to the shore.
            // Out at sea waves run every way; over a shelving bottom they slow, and their crests swing round to lie along the shore
            // whichever way they came: along the lines of equal time to the shore. So the surf is two trains of waves (the swell's and
            // the wind sea's) whose crests ARE those lines: a place's part in a wave is the sea's clock and its own time to the shore
            // together, counted in the train's periods. Each wave of a train has its own height (they come in sets); it grows as the
            // water shoals (Green's law), its front steepens, and where it is 0.78 of the depth high it breaks and runs on as a bore,
            // lower and white. At the water's edge each arriving wave lifts the sea's level itself, which is the sheet of water that
            // runs up the sand and drains back. The shore is not struck all along at once: the count is put a little forward or back
            // by a slow pattern pinned to the water.
            // Gives x: how much higher the water stands for it (m); y: how much of a breaking front the place is (for the foam); z: how
            // far the water here is thrown forward as the crest pitches over (m), and w how much of that throw it has (0 to 1). A wave
            // that breaks by plunging does it so: the water just behind the top of the crest outruns the wave, goes over the front and
            // falls. The mesh is made to do the same (see the sea's vertex program), and so the wave curls over itself.
            float4 surfAt(float2 spot, float ground, float deep, float time)
            {
                float2 pinned = spot + _OceanAtSpot.xy;
                float late = SampleLod(_OceanLace, pinned / 256.0, 0.0).a + 0.2 * SampleLod(_OceanLace, pinned / 64.0 + 0.31, 0.0).a;
                float known = 1.0 - smoothstep(150.0, 400.0, time);
                float lift = 0.0, front = 0.0, thrown = 0.0, share = 0.0;
                { [loop] for (int j = 0; j < 2; j++)
                {
                    float period = j == 0 ? _OceanSurfA.x : _OceanSurfA.z, offshore = j == 0 ? _OceanSurfA.y : _OceanSurfA.w;
                    float begun = j == 0 ? _OceanSurfB.x : _OceanSurfB.z, counted = j == 0 ? _OceanSurfB.y : _OceanSurfB.w;
                    if (offshore > 0.02 && known > 0.0)
                    {
                        float count = begun + min(time, 400.0) / period + late * (j == 0 ? 0.3 : 0.5) + float(j) * 0.37;
                        float which = floor(count), part = count - which;          // (part: 0 at the crest, growing seaward: just under 1 is the wave's front)
                        float size = offshore * (0.55 + 0.9 * surfChance(uint3(uint(int(counted + which) + 70000), uint(j), 7u)));
                        float shoaled = size * clamp(pow(max(4.0 * size, 0.2) / max(deep, 0.05), 0.25), 1.0, 1.45);
                        float past = shoaled / max(0.78 * deep, 0.02);              // (1 where it breaks)
                        float broken = smoothstep(0.95, 1.5, past);
                        float tall = min(shoaled, 0.78 * deep) * lerp(1.0, 0.7, broken);
                        // (where the open sea's long waves are still themselves there is no surf: it takes over as the bottom takes them)
                        float taken = (1.0 - smoothstep(2.0, 6.0, deep / max(size, 0.05))) * known;
                        float steep = clamp(past, 0.0, 1.0);
                        float sharp = lerp(1.0, 3.0, steep);
                        float skewed = pow(part, lerp(1.0, 2.4, steep));
                        float shape = pow(0.5 + 0.5 * cos(6.2832 * skewed), sharp);
                        lift += taken * tall * (shape - 0.5 / sqrt(sharp));
                        // (the breaking front: the crest and the face before it; once broken, the bore's white reaches further back)
                        float crest = smoothstep(0.84, 0.97, part) + 1.0 - smoothstep(0.0, lerp(0.07, 0.3, broken), part);
                        front = max(front, taken * smoothstep(0.85, 1.05, past) * clamp(crest, 0.0, 1.0) * step(0.03, -ground));
                        // (the pitching over: strongest as the wave breaks, less once it is a bore; the water a little behind the top goes furthest)
                        float pitch = smoothstep(0.82, 1.02, past) * (1.0 - 0.75 * broken) * taken;
                        float lip = part < 0.5 ? exp(-pow((part - 0.018) / 0.03, 2.0)) : 0.0;
                        if (pitch * tall * lip > thrown) { thrown = pitch * tall * lip; share = lip; }
                        // (the run up the sand: the level rises fast as the wave arrives and drains slowly; only at the water's edge)
                        float run = pow(1.0 - part, 2.5) * smoothstep(0.0, 0.06, part);
                        lift += 0.34 * size * run * (1.0 - smoothstep(0.0, 1.2 * size, deep)) * (1.0 - smoothstep(0.6, 2.5, time)) * known;
                    }
                } }
                return float4(lift, front, thrown, share);
            }
            // </surf>

            // A crest going over.
            //
            // What a breaking crest is: the top of the wave goes up, forward, over and round, and its tip comes back in under it, with
            // a hollow tube of air inside the curl and the wave's own face for the tube's back wall. (The user drew it, 2026-10-11:
            // on the left what the sea did then, "a stretched version where it's trying to curve over", a cliff with a long slope
            // behind it; on the right what a breaker is, a round curl with its tip turned in over a flat trough.)
            //
            // The waves here are sums of sines with their crests sharpened, and at the size of such a curl the top of a long wave is
            // nearly level ground: there is nothing to curl over. So the breaker's shape is built, at the wave's own crest line, out
            // of the strip of surface that lies there. (Tried first that day, and each seen by the user for what it was: the white's
            // front carried forward ("I don't see the capsizing"); the white raised into a roller ("those white spots just create a
            // taller mound"); the crest's top swung forward about a line below it; then a flat tongue thrown out over a slot cut in
            // the face, which from any distance was the cliff of the drawing.)
            //
            // `u` says where a point of the still sea lies on that strip: how far down the wind of the crest's line, in the breaker's
            // own sizes (a size: four tenths of how high that size of wave runs). What comes back is where that point goes, in sizes
            // from the crest's line: x down the wind, y up. With no breaker a point stays where the waves have it: `left` of a size
            // down the wind for each size of still sea (the waves' own sideways squeeze), and level.
            //
            //   behind the line (u below nought), from eight sizes back: the wave's back climbs to a size above the crest and goes
            //     on round the outside of a circle (the curl: four fifths of a size in radius, its middle half a size ahead of the
            //     line): over the top, down the front, and in underneath, to the tip at u nought;
            //   ahead of it: back from the tip round the INSIDE of the curl, which is the tube's roof (the lip between the two is
            //     nothing at the tip and a third of a size where it springs from the wave); then down the tube's back wall, which
            //     is the wave's face, to a foot a size below where the crest stood; then the trough's floor, level under the curl
            //     and rising again to the wave's own face eleven sizes ahead.
            //
            // Every point keeps its neighbours: the surface is one sheet, rolled. Along the sheet it is nowhere pulled out by more
            // than a fifth, so the foam's pattern, which is pinned to the still sea, is not drawn into streaks on it.
            //
            // `much` (0 to 1) is how far the breaker has got. The curl winds on as it grows: at first the crest only stands up and
            // leans (the outside of the circle reaches a little past its top), then it pitches forward, then it comes over and its
            // tip turns in. `fallen` (0 to 1) is the same curl come down into the trough: a breaker's front that has just stopped
            // running goes through that on its way back to plain water; it is not unrolled.
            // (2026-10-11, after the user's verdict on the round curl, "very geometric and not natural and organic", and what was then
            // looked up of real plunging crests (wiki/ksp-natural-ocean/Research.md, 8): the hollow under an overturning crest is
            // not a circle but close to an ellipse 1.7 times as long as wide, lying tilted, its long way running forward and down
            // from under the crest (New, 1983). So the curl goes round such an ellipse; it stands less high over the wave and
            // the trough in front of it is dug less deep and less far than it first was.)
            float2 curlRound(float angle, float reach, float2 middle)
            {
                // (the ellipse: its long half `reach`, its short half 0.6 of that, tilted 32 degrees forward and down)
                float2 e = float2(cos(angle), 0.6 * sin(angle)) * reach;
                return middle + float2(e.x * 0.848 + e.y * 0.530, -e.x * 0.530 + e.y * 0.848);
            }

            float2 curlAt(float u, float left, float much, float fallen)
            {
                const float reach0 = 0.95, lead = 2.6, roof = 2.4, wall = 1.1, back = 7.0, floorEnd = 8.0;
                float reach = reach0 * (1.0 - 0.25 * fallen);
                float2 middle = float2(0.45, 0.05 - 0.45 * fallen);
                float foot = -0.8 * (1.0 - 0.6 * fallen);
                // (how far round the tip has got: from just past the top of the hollow to in underneath it)
                float tip = radians(lerp(120.0, -55.0, smoothstep(0.15, 1.0, much)) - 45.0 * fallen);
                float from = radians(130.0);
                float2 plain = float2(left * u, 0.0);
                float2 goes;
                if (u <= -back || u >= floorEnd) goes = plain;
                else if (u <= -lead)
                {
                    // (the back of the wave, from where it is as the waves have it to where the curl takes it up, climbing)
                    float t = (u + back) / (back - lead), wide = back - lead;
                    float2 p1 = curlRound(from, reach, middle);
                    float2 m0 = float2(left * wide, 0.0), m1 = wide * (curlRound(from - 0.02, reach, middle) - p1) / (0.02 * lead / max(from - tip, 0.05));
                    goes = (2.0 * t * t * t - 3.0 * t * t + 1.0) * float2(-left * back, 0.0) + (t * t * t - 2.0 * t * t + t) * m0 + (-2.0 * t * t * t + 3.0 * t * t) * p1 + (t * t * t - t * t) * m1;
                }
                else if (u <= 0.0)
                {
                    // (round the outside of the curl)
                    goes = curlRound(lerp(tip, from, -u / lead), reach, middle);
                }
                else if (u <= roof)
                {
                    // (back round the inside: the hollow's roof, and the lip growing thicker towards where it springs from the wave)
                    float angle = lerp(tip, radians(180.0), u / roof);
                    float thick = 0.4 * smoothstep(0.0, 1.0, (angle - tip) / radians(200.0));
                    goes = curlRound(angle, reach * (1.0 - thick), middle);
                }
                else if (u <= roof + wall)
                {
                    // (down the hollow's back wall, the wave's face, to its foot)
                    float t = (u - roof) / wall;
                    float thickEnd = 0.4 * smoothstep(0.0, 1.0, (radians(180.0) - tip) / radians(200.0));
                    float2 p0 = curlRound(radians(180.0), reach * (1.0 - thickEnd), middle), p1 = float2(0.25, foot);
                    float2 m0 = wall * 0.9 * normalize(curlRound(radians(183.0), reach * (1.0 - thickEnd), middle) - p0), m1 = float2(0.9 * wall, 0.0);
                    goes = (2.0 * t * t * t - 3.0 * t * t + 1.0) * p0 + (t * t * t - 2.0 * t * t + t) * m0 + (-2.0 * t * t * t + 3.0 * t * t) * p1 + (t * t * t - t * t) * m1;
                }
                else
                {
                    // (the trough's floor: level under the curl, then up to the wave's own face again)
                    float wide = floorEnd - roof - wall, t = (u - roof - wall) / wide;
                    float2 p0 = float2(0.25, foot), p1 = float2(left * floorEnd, 0.0);
                    float2 m0 = float2(0.9 * wide, 0.0), m1 = float2(left * wide, 0.0);
                    goes = (2.0 * t * t * t - 3.0 * t * t + 1.0) * p0 + (t * t * t - 2.0 * t * t + t) * m0 + (-2.0 * t * t * t + 3.0 * t * t) * p1 + (t * t * t - t * t) * m1;
                }
                // (and it grows out of the plain sea: nothing at first)
                return lerp(plain, goes, smoothstep(0.0, 0.3, much));
            }

            // How white the water is at a place (m from the point of the sea under the camera), read over `wide` metres: from the
            // finest of the three finer maps that reaches so far.
            float whiteAt(float2 place, float wide)
            {
                float out_ = max(abs(place.x), abs(place.y));
                float white = 0.0;
                { [loop] for (int l = 2; l >= 0; l--)
                {
                    float span = _FoamReach.y * Pick(_FoamTexel, l);
                    float inside = 1.0 - smoothstep(0.34, 0.42, out_ / span);
                    float2 at = l == 0 ? _FoamAtA.xy : l == 1 ? _FoamAtA.zw : _FoamAtB.xy;
                    white = lerp(white, SampleLod(_OceanFoamMap, at + place / span, max(log2(max(wide / Pick(_FoamTexel, l), 1e-3)), 0.0))[l], inside);
                } }
                return white;
            }

            // The waves at a place on the still sea (m from the point under the camera, on the sea's two axes): how far the water that
            // rests there is moved (m: along the first axis, up, along the second). `fine`: how far apart the mesh's points are there;
            // `deep`: how much water there is; `rimmed`: 1 on the outermost ring's rim. And of a crest going over there: `lipSaid`, for
            // the light (see OUT.vs_TEXCOORD4); `lipPull`, for gathering the mesh's points to it (see main): where on the breaker's shape
            // the place is, the breaker's size (m), and how much of a breaker.
            float3 wavesAt(float2 spot, float fine, float deep, float rimmed, out float4 lipSaid, out float3 lipPull, out float4 lipWhy)
            {
                float4 ats[8] = {_OceanAt0, _OceanAt1, _OceanAt2, _OceanAt3, _OceanAt4, _OceanAt5, _OceanAt6, _OceanAt7};
                float2 turned = float2(_OceanTwin.y * spot.x - _OceanTwin.z * spot.y, _OceanTwin.z * spot.x + _OceanTwin.y * spot.y) / _OceanTwin.x;
                float high[8] = {_OceanHighA.x, _OceanHighA.y, _OceanHighA.z, _OceanHighA.w, _OceanHighB.x, _OceanHighB.y, _OceanHighB.z, _OceanHighB.w};
                float3 moved = ((float3)(0.0));
                // (crests going over: how far they move this point, m down the wind and up; and what the light is told of it: the
                // cosine and sine of how far the surface is turned, how much of a breaker, where on its shape)
                float2 lipMoved = ((float2)(0.0));
                float2 lipTurn = float2(1.0, 0.0);
                lipSaid = float4(1.0, 0.0, 0.0, 0.0);
                lipPull = ((float3)(0.0));
                lipWhy = ((float4)(0.0));
                float longer = 0.0;
                float one = _OceanLay.x;
                { [loop] for (int n = 0; n < 8; n++)
                {
                    float level = max(log2(fine * one * _OceanLay.z) + 0.5, 0.0);
                    float4 a4 = SampleLod(_OceanMoved, float3(ats[n].xy + spot * one, float(n)), level);
                    float4 b4 = SampleLod(_OceanMoved, float3(ats[n].zw + turned * one, float(n)), level + 0.3);
                    float3 a = a4.rgb, b = b4.rgb;
                    // (the second laying's sideways moves turned back, and all of it as much smaller as it is laid)
                    float3 both = a + float3(_OceanTwin.y * b.x + _OceanTwin.z * b.z, b.y, _OceanTwin.y * b.z - _OceanTwin.z * b.x) * _OceanTwin.x;
                    // ---- A crest going over (see curlAt). For each size of wave that breaks white, where the white water has a
                    // front running on a crest of it: the breaker's shape, laid along the crest's own line.
                    //
                    // Where the line is: where this size of wave's own height (both its layings; nothing of the other sizes, which
                    // ride on it and are carried with it) stops rising down the wind. From the height one of the patch's points up
                    // the wind and one down: how fast it rises here and how sharply that changes; the one over the other is how far
                    // from here the top is. (By how far the water has been moved sideways it cannot be told: a short wave's crest on
                    // a long wave's back is carried bodily forward or back by the long one, a metre and more.)
                    //
                    // How much of a breaker: by the squeeze AT THE LINE, held against the level at which a crest of this size
                    // carries a front, as the white water holds it (oceanfoam.glsl), and by whether the water just down the wind of
                    // the line is breaking white this moment. Both are read at the line, not here, so that every point across the
                    // breaker is moved by the same breaker: read here, the lip's root and its tip disagreed and the shape tore.
                    float own = (a4.a + b4.a) * _OceanTwin.w;
                    float height0 = both.y;
                    float size = abs(_OceanBreakHow.w) * high[n];
                    float begins = n >= 1 && n <= 4 ? Pick(_OceanBreaks, clamp(n - 1, 0, 3)) : -9.0;
                    float carried = begins + _OceanBreakHow.x;
                    // (only where the mesh can hold the shape: with its points gathered to the curl (see main) some six of them round
                    // the outside of it at the least. Coarser than that the curl only begins; coarser still, nothing.)
                    float holds = 1.0 - smoothstep(0.55, 1.5, fine / max(size, 1e-3));
                    // (for finding what the sea costs, from the development build: test 43 leaves the curls out)
                    bool may = begins > -8.0 && own + _OceanBreakHow.y * longer < min(0.5 * carried, -0.02) && holds * _OceanWind.w > 0.001 && rimmed < 0.5 && abs(_OceanShore.y - 43.0) > 0.5;
                    { [loop] for (int once = may ? 0 : 1; once < 1; once++)
                    {
                        float apart = 1.0 / (one * _OceanLay.z);
                        float2 st = _OceanWind.xy * apart;
                        float2 stT = float2(_OceanTwin.y * st.x - _OceanTwin.z * st.y, _OceanTwin.z * st.x + _OceanTwin.y * st.y) / _OceanTwin.x;
                        float4 aF = SampleLod(_OceanMoved, float3(ats[n].xy + (spot + st) * one, float(n)), level);
                        float4 aB = SampleLod(_OceanMoved, float3(ats[n].xy + (spot - st) * one, float(n)), level);
                        float4 bF = SampleLod(_OceanMoved, float3(ats[n].zw + (turned + stT) * one, float(n)), level + 0.3);
                        float4 bB = SampleLod(_OceanMoved, float3(ats[n].zw + (turned - stT) * one, float(n)), level + 0.3);
                        float hF = aF.g + bF.g * _OceanTwin.x, hB = aB.g + bB.g * _OceanTwin.x;
                        float rises = (hF - hB) / (2.0 * apart);
                        float bends = (hF - 2.0 * height0 + hB) / (apart * apart);
                        // (a crest, and one that is sharp enough down the wind to have a line: against the usual crest of this size,
                        // which bends by its height over the square of its length over 2 pi)
                        float usual = 35.2 * one;
                        usual = usual * usual * 0.5 * high[n];
                        float sure = smoothstep(0.3, 0.7, -bends * _OceanTwin.w / max(usual, 1e-6));
                        // (how far down the wind of the crest's line this point lies, m: behind it, below nought)
                        float ahead = clamp(rises / min(bends, -1e-6), -12.0 * size, 12.0 * size);
                        float2 crest = spot - _OceanWind.xy * ahead;
                        float2 crestT = float2(_OceanTwin.y * crest.x - _OceanTwin.z * crest.y, _OceanTwin.z * crest.x + _OceanTwin.y * crest.y) / _OceanTwin.x;
                        float4 cA = SampleLod(_OceanMoved, float3(ats[n].xy + crest * one, float(n)), level);
                        float4 cB = SampleLod(_OceanMoved, float3(ats[n].zw + crestT * one, float(n)), level + 0.3);
                        float ownThere = (cA.a + cB.a) * _OceanTwin.w;
                        // (And how sure it is that there is a crest, read at the line as well: read where each point stands, as it was,
                        // the points across one breaker were given breakers of different strengths, each its own curl, and the sheet
                        // between them tore into the flaps and teeth the user saw. Read at the line, every point across it agrees.)
                        float hC = cA.g + cB.g * _OceanTwin.x;
                        float hCF = SampleLod(_OceanMoved, float3(ats[n].xy + (crest + st) * one, float(n)), level).g + SampleLod(_OceanMoved, float3(ats[n].zw + (crestT + stT) * one, float(n)), level + 0.3).g * _OceanTwin.x;
                        float hCB = SampleLod(_OceanMoved, float3(ats[n].xy + (crest - st) * one, float(n)), level).g + SampleLod(_OceanMoved, float3(ats[n].zw + (crestT - stT) * one, float(n)), level + 0.3).g * _OceanTwin.x;
                        sure = smoothstep(0.3, 0.7, -((hCF - 2.0 * hC + hCB) / (apart * apart)) * _OceanTwin.w / max(usual, 1e-6));
                        float steep = clamp((carried - (ownThere + _OceanBreakHow.y * longer)) / _OceanBreakHow.z, 0.0, 1.0);
                        // (for finding faults, the size given below nought: a lip on every steep crest, white or not)
                        // (white just down the wind of the line: a front is running here now. White a little way up the wind of it: one
                        // ran here a moment ago, and the lip it threw is still coming down)
                        float now_ = smoothstep(0.05, 0.25, whiteAt(crest + _OceanWind.xy * size, 2.0 * size));
                        float lately = smoothstep(0.05, 0.25, 0.8 * whiteAt(crest - _OceanWind.xy * (2.5 * size), 2.0 * size));
                        float plunges = _OceanBreakHow.w < 0.0 ? 1.0 : smoothstep(_OceanPlunge.x, _OceanPlunge.y, begins - (ownThere + _OceanBreakHow.y * longer));
                        // (a crest squeezed past the plunge band is breaking hard whatever the white water's map holds at its line this moment: the read
                        // there seldom finds the white on the very crest that is steep and sure, and with it no crest ever came over; _OceanPlunge.z 1)
                        float running = _OceanBreakHow.w < 0.0 ? 1.0 : max(max(now_, lately), _OceanPlunge.z * plunges);
                        float fallen = _OceanBreakHow.w < 0.0 ? 0.0 : clamp(lately - now_, 0.0, 1.0);
                        // (not evenly along the crest: further here, less there, by the lace's slow blotches, which are pinned to the water:
                        // the crest runs over them, so the lip's edge is drawn out in tongues that come and go, as a real one's is)
                        float uneven = 0.78 + 0.4 * SampleLod(_OceanLace, (crest + _OceanAtSpot.xy) / (60.0 * size), 0.0).a;
                        // (And only a crest that breaks violently plunges: one squeezed well past the level at which a breaker of its
                        // size begins. Nearly all the breaking on deep water is spilling, white water going down the face from the
                        // crest with no hollow under it, and those crests are left their own shape; of the rest most only pitch
                        // forward and few come right over. See wiki/ksp-natural-ocean/Research.md, 8.)
                        // (plunges: worked out above, with running)
                        float much = clamp(steep * running * sure * holds * _OceanWind.w * clamp(0.45 * deep / max(high[n], 1e-3), 0.0, 1.0) * uneven * plunges, 0.0, 1.0);
                        float u = ahead / max(size, 1e-3);
                        // (a metre of still sea is `left` of a metre after this size of wave's sideways squeeze, and rises by the wave's own slope)
                        float2 mF = aF.rb + float2(_OceanTwin.y * bF.r + _OceanTwin.z * bF.b, _OceanTwin.y * bF.b - _OceanTwin.z * bF.r) * _OceanTwin.x;
                        float2 mB = aB.rb + float2(_OceanTwin.y * bB.r + _OceanTwin.z * bB.b, _OceanTwin.y * bB.b - _OceanTwin.z * bB.r) * _OceanTwin.x;
                        float left = clamp(1.0 + dot(mF - mB, _OceanWind.xy) / (2.0 * apart) * _OceanTwin.w, 0.5, 1.0);
                        float2 shape = curlAt(u, left, much, fallen);
                        lipMoved += size * (shape - float2(left * u, 0.0));
                        // (How the surface is turned by it, for the light: the way it runs down the wind, a small step on along the
                        // shape, against the way it runs with no breaker. At the tip the sheet turns back on itself, and from there
                        // on round the tube's roof and down its wall the light's side of it is the inside. Handed on as a cosine and
                        // a sine, which a triangle of the mesh can carry from one corner to the next the short way round; an angle
                        // it would carry the long way, through a full turn, where the roof comes round to the floor.)
                        float2 runs = curlAt(u + 0.04, left, much, fallen) - shape;
                        float2 plainRuns = float2(left * 0.04, rises * _OceanTwin.w * 0.04);
                        float by = atan2(runs.y, runs.x) - atan2(plainRuns.y, plainRuns.x);
                        lipTurn = float2(lipTurn.x * cos(by) - lipTurn.y * sin(by), lipTurn.y * cos(by) + lipTurn.x * sin(by));
                        if (much > lipSaid.y) { lipSaid.yz = float2(much, u); lipPull = float3(u, size, much); }
                        if (_OceanShore.y < 9.5 || int(_OceanShore.y - 9.5) == n) lipWhy = max(lipWhy, float4(1.0, steep, running, sure * holds));
                    } }
                    // ---- And before it breaks, the wave leans. A crest near breaking is not the even hump a sum of sines makes: its
                    // front has steepened and its back drawn out, and it stands a little higher (the waves of a group running
                    // together). Without that a lip is thrown off a low round swell and the breaker is a lump that comes up out of
                    // flat water. So where this size of wave is squeezed to near the level at which it carries a breaking front, the
                    // higher its water stands the further it is carried down the wind: the front face closes up, the back opens out.
                    // It comes and goes as the squeeze does, over seconds, on every steep crest, white or not.
                    float nearly = begins > -8.0 ? smoothstep(0.0, 1.0, (carried + 0.06 - (own + _OceanBreakHow.y * longer)) / 0.2) * _OceanWind.w : 0.0;
                    both.xz += _OceanWind.xy * (1.6 * nearly * max(height0, 0.0));
                    both.y *= 1.0 + 0.2 * nearly;
                    longer += own;
                    moved += both * clamp(0.45 * deep / max(high[n], 1e-3), 0.0, 1.0);
                    one *= _OceanLay.y;
                } }
                moved *= _OceanTwin.w;
                moved += float3(_OceanWind.x * lipMoved.x, lipMoved.y, _OceanWind.y * lipMoved.x);
                lipSaid.xw = lipTurn;
                return moved;
            }

            // The way a blow's rings run at a place (Dents.cs): out from the crater's middle, across the ellipse a slanting blow's
            // rings make (drawn out along the way the craft came by how much longer its crater was).
            float2 dentOutward(float2 spot)
            {
                float2 from = spot - _OceanDentMid.xy;
                float2 athwart = float2(-_OceanDentMid.w, _OceanDentMid.z);
                float2 runs = _OceanDentMid.zw * (dot(from, _OceanDentMid.zw) * _OceanDentCurl.w * _OceanDentCurl.w) + athwart * dot(from, athwart);
                float long_ = length(runs);
                return long_ > 1e-4 ? runs / long_ : _OceanDentMid.zw;
            }

            // A blow's dent at a place (Dents.cs): how far its water is moved there (m: along the first axis, up, along the second);
            // and of a ring of it going over there, as wavesAt says of a crest: `lipSaid`, `lipPull`, and the way the ring runs
            // (`lipWay`). A ring's crest is found as a wave's is, along the way it runs; where it is steep past breaking at its crest
            // it throws the sea's own lip (curlAt), as large as it stands high, where the mesh can hold it; the leading ring's lip
            // comes down as it runs on (Dents.cs says when), and what breaks after it spills.
            float3 dentAt(float2 spot, float fine, out float4 lipSaid, out float3 lipPull, out float2 lipWay)
            {
                lipSaid = float4(1.0, 0.0, 0.0, 0.0);
                lipPull = ((float3)(0.0));
                lipWay = _OceanWind.xy;
                float3 moved = ((float3)(0.0));
                float2 inDent = (spot - _OceanDentAt.xy) * _OceanDentAt.z + 0.5;
                bool there = _OceanDentAt.w > 0.5 && inDent.x > 0.0 && inDent.y > 0.0 && inDent.x < 1.0 && inDent.y < 1.0;
                { [loop] for (int once = there ? 0 : 1; once < 1; once++)
                {
                    float4 d = SampleLod(_OceanDent, inDent, 0.0);
                    float4 m = SampleLod(_OceanDentMove, inDent, 0.0);
                    moved = float3(m.r, d.r, m.g);
                    float2 outward = dentOutward(spot);
                    float apart = _OceanDentCurl.y;
                    float2 st = outward * (apart * _OceanDentAt.z);
                    float hF = SampleLod(_OceanDent, inDent + st, 0.0).r, hB = SampleLod(_OceanDent, inDent - st, 0.0).r;
                    float rises = (hF - hB) / (2.0 * apart);
                    float bends = (hF - 2.0 * d.r + hB) / (apart * apart);
                    // (how far out of the crest's line this point lies, m: inside it, below nought)
                    float ahead = clamp(rises / min(bends, -1e-6), -6.0 * apart, 6.0 * apart);
                    float4 c = SampleLod(_OceanDent, inDent - outward * (ahead * _OceanDentAt.z), 0.0);
                    float size = _OceanDentCurl.x * max(c.r, 0.0);
                    // (a crest, and a sharp one: bending about as a wave as high and as steep does at its crest (its height times its
                    // wave number squared, and the steepness is the height times the wave number))
                    float usual = c.a * c.a / max(c.r, 0.05);
                    float sure = smoothstep(0.3, 0.7, -bends / max(usual, 1e-4));
                    float holds = 1.0 - smoothstep(0.55, 1.5, fine / max(size, 1e-3));
                    float much = clamp(smoothstep(0.42, 0.56, c.a) * sure * holds * step(0.2, size), 0.0, 1.0);
                    { [loop] for (int lip = much > 0.001 ? 0 : 1; lip < 1; lip++)
                    {
                        float4 mF = SampleLod(_OceanDentMove, inDent + st, 0.0), mB = SampleLod(_OceanDentMove, inDent - st, 0.0);
                        float left = clamp(1.0 + dot(mF.rg - mB.rg, outward) / (2.0 * apart), 0.5, 1.0);
                        float u = ahead / max(size, 1e-3);
                        float fallen = _OceanDentCurl.z;
                        float2 shape = curlAt(u, left, much, fallen);
                        float2 lipMoved = size * (shape - float2(left * u, 0.0));
                        moved += float3(outward.x * lipMoved.x, lipMoved.y, outward.y * lipMoved.x);
                        float2 runs = curlAt(u + 0.04, left, much, fallen) - shape;
                        float2 plainRuns = float2(left * 0.04, rises * 0.04);
                        float by = atan2(runs.y, runs.x) - atan2(plainRuns.y, plainRuns.x);
                        lipSaid = float4(cos(by), much, u, sin(by));
                        lipPull = float3(u, size, much);
                        lipWay = outward;
                    } }
                } }
                return moved;
            }

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                float3 cellAt = in_POSITION0;
                float half_ = _OceanAxisA.w;
                // Towards a ring's rim its points move onto the points of the next ring out (every other one of its own), so that the
                // two rings meet with no crack and no jump.
                float rim = max(abs(cellAt.x), abs(cellAt.z)) / half_;
                float merge = smoothstep(0.72, 0.95, rim);
                cellAt.xz -= frac(cellAt.xz * 0.5) * 2.0 * merge;
                float cell = length(hlslcc_mtx4x4unity_ObjectToWorld[0].xyz);
                float4 world = hlslcc_mtx4x4unity_ObjectToWorld[0] * cellAt.x + hlslcc_mtx4x4unity_ObjectToWorld[2] * cellAt.z + hlslcc_mtx4x4unity_ObjectToWorld[3];
                float3 from = world.xyz - _OceanMid.xyz;
                float2 spot = float2(dot(from, _OceanAxisA.xyz), dot(from, _OceanAxisB.xyz));
                // (the outermost ring's rim, out to the horizon and past it)
                spot *= 1.0 + in_POSITION0.y * (_OceanAxisB.w - 1.0);
                float3 up = cross(_OceanAxisB.xyz, _OceanAxisA.xyz);
                // The waves, each patch read no finer than the mesh is here (what is finer than the mesh is left to the light: see below).
                float fine = cell * (1.0 + merge) * (1.0 + in_POSITION0.y * 30.0);
                // A wave cannot be much higher than the water is deep: coming in over a shelving bottom it breaks and spends itself, the
                // highest waves furthest out, and at the water's edge there is nothing left of any. So each patch's waves are kept only as
                // far as the depth here allows them (0.45 of the depth for the mean of the highest third, as is measured in surf).
                float ground = groundAt(spot);
                float deep = max(-ground, 0.0);
                float4 lipSaid, lipWhy;
                float3 lipPull;
                float3 moved = wavesAt(spot, fine, deep, in_POSITION0.y, lipSaid, lipPull, lipWhy);
                // (a blow's dent: its water's moves, and a ring of it going over; of its lip and the waves' the larger is the one the
                // mesh is gathered to and the light told of)
                float4 dentSaid;
                float3 dentPull;
                float2 dentWay;
                float3 dented = dentAt(spot, fine, dentSaid, dentPull, dentWay);
                bool dentLip = dentSaid.y > lipSaid.y;
                float2 pullWay = dentLip ? dentWay : _OceanWind.xy;
                lipSaid = dentLip ? dentSaid : lipSaid;
                lipPull = dentLip ? dentPull : lipPull;
                // ---- The mesh's points gathered to a curl. A curl is a size or two across and wants a dozen points round it to be
                // round; the mesh has one every fiftieth of the way to the camera, which sixty metres off is a point a metre. With
                // two or three points to bend, a curl was a step: the cliff of the user's drawing. So where a point of the mesh
                // falls within reach of a breaker it does not stand for its own place on the still sea but for one nearer the
                // crest's line: the points from eight sizes either side are drawn into the three sizes round the curl (two and a
                // half times as many there), and those from eight to fourteen sizes are spread over what is left. The sea is the
                // same sea, read at other places: nothing is moved by this, only what the mesh has points for. The waves are then
                // worked out afresh for the place the point now stands for.
                { [loop] for (int once = lipPull.z > 0.02 ? 0 : 1; once < 1; once++)
                {
                    float v = lipPull.x - 0.2, far = abs(v);
                    float nearer = far <= 8.0 ? far / 2.5 : far <= 14.0 ? 3.2 + (far - 8.0) * 1.8 : far;
                    float goesTo = sign(v) * nearer + 0.2;
                    // (And every point that is gathered lands on one of a set of lines along the crest, the same lines the whole length
                    // of it, the lip's edge one of them. Each row of the mesh crosses the crest at its own slant, so left where they
                    // fell its points stood at other places across the curl than the next row's: the curl was drawn through other
                    // points in every row, its outline and the light on it changed from one row to the next, and near the tip a
                    // triangle could reach from the outside of the curl across to its roof. That was the jagged edge the user saw
                    // ("they curl well, but they have a lot of jagged edges from the meshing", 2026-10-17). On the lines every row
                    // draws the curl through the same points, and the triangles between two lines lie on the one strip of the curl
                    // between them, whichever way the mesh runs. The lines are as far apart as the mesh's points are along its
                    // diagonals after gathering, so that no triangle spans more than one strip, and by the ring's own spacing, a
                    // power of two, so that the next ring out has every other line: no seam where two rings meet.)
                    float lines = 1.5 * exp2(ceil(log2(max(fine, 1e-4)))) / (2.5 * max(lipPull.y, 1e-3));
                    // (for comparing, from the development build: test 54 gathers the points as they were before, the one nearest the
                    // lip's edge in each row put on it and the rest where they fell)
                    bool asBefore = abs(_OceanShore.y - 44.0) < 0.5;
                    if (asBefore) { if (abs(goesTo) < 0.5 * fine / (2.5 * lipPull.y)) goesTo = 0.0; }
                    else if (goesTo > -7.5 && goesTo < 8.5) goesTo = lines * floor(goesTo / lines + 0.5);
                    // (Only where the mesh is too coarse for the curl by itself. Close to the camera its points are a hand apart and
                    // the curl is round without this; and there, gathered all the same, the long thin triangles it makes between a
                    // point that was moved and a neighbour that was not showed from inside the hollow as shards of glass.)
                    float needed = smoothstep(0.04, 0.14, fine / max(lipPull.y, 1e-3));
                    float pulls = smoothstep(0.0, 0.3, lipPull.z) * needed;
                    float sizeWas = lipPull.y;
                    spot += pullWay * ((goesTo - lipPull.x) * lipPull.y * pulls);
                    ground = groundAt(spot);
                    deep = max(-ground, 0.0);
                    moved = wavesAt(spot, fine, deep, in_POSITION0.y, lipSaid, lipPull, lipWhy);
                    dented = dentAt(spot, fine, dentSaid, dentPull, dentWay);
                    dentLip = dentSaid.y > lipSaid.y;
                    lipSaid = dentLip ? dentSaid : lipSaid;
                    lipPull = dentLip ? dentPull : lipPull;
                    // (And a second step, from where the crest is seen to be from the new place: the first was taken by how far it was
                    // seen to be from the old one, which is seen less well the further off it was, and a point a little off its line
                    // is a tooth again. Only while it is the same breaker that is nearest, and only for a point that missed its line by
                    // more than a sixth of the way to the next: most land close enough the first time, and each step costs the waves
                    // worked out once more.)
                    if (!asBefore && lipPull.z > 0.02 && abs(lipPull.y - sizeWas) < 1e-3 * sizeWas && abs(goesTo - lipPull.x) > lines / 6.0)
                    {
                        spot += pullWay * ((goesTo - lipPull.x) * lipPull.y * pulls);
                        ground = groundAt(spot);
                        deep = max(-ground, 0.0);
                        moved = wavesAt(spot, fine, deep, in_POSITION0.y, lipSaid, lipPull, lipWhy);
                        dented = dentAt(spot, fine, dentSaid, dentPull, dentWay);
                        dentLip = dentSaid.y > lipSaid.y;
                        lipSaid = dentLip ? dentSaid : lipSaid;
                        lipPull = dentLip ? dentPull : lipPull;
                    }
                } }
                // ---- the surf (only near a shore: where the water is shallow or the ground stands a little above it)
                float4 surf = ((float4)(0.0));
                float tallest = max(_OceanSurfA.y, _OceanSurfA.w);
                if (ground > -9.0 * tallest - 1.0 && ground < 2.0 * tallest + 0.5 && in_POSITION0.y < 0.5)
                {
                    float reach = max(fine, 0.3);
                    float timeHere = shoreTimeAt(spot);
                    float4 here = surfAt(spot, ground, feltDepthAt(spot), timeHere);
                    float2 east = spot + float2(reach, 0.0), north = spot + float2(0.0, reach);
                    float timeA = shoreTimeAt(east), timeB = shoreTimeAt(north);
                    float liftA = surfAt(east, groundAt(east), feltDepthAt(east), timeA).x, liftB = surfAt(north, groundAt(north), feltDepthAt(north), timeB).x;
                    surf = float4(here.x, (liftA - here.x) / reach, (liftB - here.x) / reach, here.y);
                    moved.y += surf.x;
                    // (The crest pitching over: the water just behind the top is carried forward, towards the shore, past the wave's own
                    // front, and down: a lip that hangs over the face. Which way the shore lies is the way the time to it falls fastest,
                    // taken over three metres to either side: taken over a hand's breadth, as it first was, it turned about from one
                    // point of the seabed's map to the next and the thrown water stood in blocks with walls between. Where the time
                    // does not fall as fast as a wave runs (waves from two shores meeting, a map still being worked out) nothing is
                    // thrown at all. And only where the mesh is fine enough to bend so: further off the wave breaks white without it.)
                    float fineEnough = 1.0 - smoothstep(0.3, 1.0, fine);
                    if (here.z * fineEnough > 0.001 && timeHere < 400.0)
                    {
                        float2 fall = float2(shoreTimeAt(spot + float2(3.0, 0.0)) - shoreTimeAt(spot - float2(3.0, 0.0)), shoreTimeAt(spot + float2(0.0, 3.0)) - shoreTimeAt(spot - float2(0.0, 3.0))) / 6.0;
                        float runs = sqrt(9.81 * clamp(feltDepthAt(spot), 0.05, 25.0));
                        float sure = smoothstep(0.5, 0.85, length(fall) * runs) * (1.0 - smoothstep(1.3, 2.0, length(fall) * runs));
                        // (And only where the mesh can hold the lip. The water that goes over is a narrow strip along the crest, some
                        // six hundredths of the wave's length from front to back: a metre and a half for a swell in two metres of
                        // water, less for the wind's waves. Where fewer than four or five of the mesh's points span it, one point is
                        // thrown and its neighbour is not, and the lip was a row of teeth with white streaks hanging from them
                        // (seen from the beach, 2026-10-11). There the wave breaks white without curling.)
                        float strip = 0.06 * min(_OceanSurfA.x, _OceanSurfA.z) * runs;
                        float held = 1.0 - smoothstep(0.12, 0.3, fine / max(strip, 0.05));
                        float throw_ = here.z * fineEnough * sure * held;
                        float2 shoreward = -fall / max(length(fall), 1e-6);
                        moved.x += shoreward.x * throw_ * 1.25;
                        moved.z += shoreward.y * throw_ * 1.25;
                        moved.y -= throw_ * here.w * 0.55;
                    }
                }
                moved *= _OceanShore.x;
                // ---- a blow's dent: the crater a craft digs going in, its walls running back, the rings it sends out (Dents.cs, dentAt)
                moved += dented;
                // ---- a hull's wake: the waves a craft makes going through the water (Wakes.cs), its height and its moves sideways
                if (_OceanWakeAt.w > 0.0)
                {
                    float2 inWake = (spot - _OceanWakeAt.xy) * _OceanWakeAt.z + 0.5;
                    if (inWake.x > 0.0 && inWake.y > 0.0 && inWake.x < 1.0 && inWake.y < 1.0)
                    {
                        float hw = SampleLod(_OceanWake, inWake, 0.0).r;
                        float2 mw = SampleLod(_OceanWakeMove, inWake, 0.0).rg;
                        moved += float3(mw.x, hw, mw.y) * _OceanWakeAt.w;
                    }
                }
                // (There was a roller here: the mesh raised where the white water's maps had a front, a hand high and then, to make
                // the crests look as if they broke, a man's height. "Those white spots just create a taller mound. They rise up and
                // then lower. They don't actually curve over themselves." It is gone: a breaking crest is the lip above.)
                // (the planet's curve: how far below the level plane the sea is, so far out)
                float R = _OceanMid.w;
                float out2 = dot(spot, spot);
                float drop = out2 / (R + sqrt(max(R * R - out2, 0.0)));
                float3 p = _OceanMid.xyz + _OceanAxisA.xyz * (spot.x + moved.x) + _OceanAxisB.xyz * (spot.y + moved.z) + up * (moved.y - drop);
                float4 clip_ = hlslcc_mtx4x4unity_MatrixVP[0] * p.x + hlslcc_mtx4x4unity_MatrixVP[1] * p.y + hlslcc_mtx4x4unity_MatrixVP[2] * p.z + hlslcc_mtx4x4unity_MatrixVP[3];
                // (The lens's map. A camera at the water's surface has the surface across its glass, and where it cuts the glass must
                // be known to the pixel: as this mesh has it, not as the waves' numbers have it, which is a finger's breadth off. So
                // while the lens is at the water this same mesh is drawn once more a frame, seen from straight above and no wider
                // than the glass, into a small picture of how high the water stands: the very same points, moved the very same way.
                // The fragment program writes the height; the higher of two sheets of water, where one hangs over another, wins.)
                if (_OceanMapAt.w > 0.0)
                {
                    float3 off = p - _OceanMapAt.xyz;
                    float mapWay = 1.0;
#if UNITY_UV_STARTS_AT_TOP
                    mapWay = -1.0;
#endif
#if defined(UNITY_REVERSED_Z)
                    float mapDeep = 0.5 + 0.5 * clamp(dot(off, up) * 0.5, -0.99, 0.99);
#else
                    float mapDeep = clamp(-dot(off, up) * 0.5, -0.99, 0.99);
#endif
                    clip_ = float4(dot(off, _OceanAxisA.xyz) * _OceanMapAt.w, dot(off, _OceanAxisB.xyz) * _OceanMapAt.w * mapWay, mapDeep, 1.0);
                }
                OUT.pos = clip_;
                OUT.vs_TEXCOORD0 = p;
                OUT.vs_TEXCOORD1 = float4(spot, clip_.w, fine);
                OUT.vs_TEXCOORD2 = clip_;
                OUT.vs_TEXCOORD3 = float4(ground, surf.yzw);
                // (a lip of a blow's ring is told by its much below nought: the light turns it the way the ring runs, not down the wind)
                OUT.vs_TEXCOORD4 = float4(lipSaid.x, dentLip ? -lipSaid.y : lipSaid.y * _OceanShore.x, lipSaid.z, lipSaid.w);
                if (_OceanShore.y > 8.5 && _OceanShore.y < 29.5) OUT.vs_TEXCOORD4 = lipWhy;
                return OUT;
            }

            // The light on the sea. Looked at steeply water is nearly clear and shows what is in it; looked along, it is a mirror of
            // the sky (Fresnel); the sun is mirrored by every facet turned the right way, in a path as wide as the sea is rough (Cox and
            // Munk); its own colour is sunlight scattered back out of it, blue because water takes the red first; a thin crest against
            // the light glows green; where the surface folds there is foam. See wiki/ksp-natural-ocean/Research.md.

#define PI 3.14159265

            // A number from 0 to 1 that three whole numbers give by chance, the same every time for the same three.
            float chance(uint3 v)
            {
                v = v * 1664525u + 1013904223u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                v ^= v >> 16u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                return float(v.x >> 8u) / 16777216.0;
            }

            // A slow pattern in space, 0 to 1: chance numbers at the corners of cubes one unit across, smoothed between.
            float weedSlow(float3 q)
            {
                float3 f = floor(q), w = q - f;
                int3 c = ((int3)(f)) + 1048576;
                w = w * w * (3.0 - 2.0 * w);
                float near = lerp(lerp(chance(((uint3)(c))), chance(((uint3)(c + int3(1, 0, 0)))), w.x), lerp(chance(((uint3)(c + int3(0, 1, 0)))), chance(((uint3)(c + int3(1, 1, 0)))), w.x), w.y);
                float far = lerp(lerp(chance(((uint3)(c + int3(0, 0, 1)))), chance(((uint3)(c + int3(1, 0, 1)))), w.x), lerp(chance(((uint3)(c + int3(0, 1, 1)))), chance(((uint3)(c + int3(1, 1, 1)))), w.x), w.y);
                return lerp(near, far, w.z);
            }

            // A slant from three whole numbers by chance, each of its parts from -1 to 1 (mixed as chance mixes them).
            float3 chanceSlant(uint3 v)
            {
                v = v * 1664525u + 1013904223u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                v ^= v >> 16u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                return ((float3)(v >> 8u)) / 8388608.0 - 1.0;
            }

            // A smooth pattern in space, about -1 to 1 and nought on the whole: a slant by chance at each corner of cubes one unit across,
            // each corner's rise or fall along its own slant, blended by a quintic (gradient noise, as Perlin's). weedSlow's values at the
            // corners are the corners themselves when seen from far enough: a grid of squares, each lighter or darker. Here the rises and
            // falls lie between the corners, and no grid shows.
            float gustNoise(float3 q)
            {
                float3 i = floor(q), f = q - i;
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                uint3 c = ((uint3)(((int3)(i)) + 1048576));
                float a = dot(chanceSlant(c), f), b = dot(chanceSlant(c + uint3(1u, 0u, 0u)), f - float3(1.0, 0.0, 0.0));
                float d = dot(chanceSlant(c + uint3(0u, 1u, 0u)), f - float3(0.0, 1.0, 0.0)), e = dot(chanceSlant(c + uint3(1u, 1u, 0u)), f - float3(1.0, 1.0, 0.0));
                float g = dot(chanceSlant(c + uint3(0u, 0u, 1u)), f - float3(0.0, 0.0, 1.0)), h = dot(chanceSlant(c + uint3(1u, 0u, 1u)), f - float3(1.0, 0.0, 1.0));
                float k = dot(chanceSlant(c + uint3(0u, 1u, 1u)), f - float3(0.0, 1.0, 1.0)), l = dot(chanceSlant(c + uint3(1u, 1u, 1u)), f - float3(1.0, 1.0, 1.0));
                return lerp(lerp(lerp(a, b, u.x), lerp(d, e, u.x), u.y), lerp(lerp(g, h, u.x), lerp(k, l, u.x), u.y), u.z);
            }

            // How much of a bed of weed there is at a place given from the planet's middle in its own turning frame (m), 0 to 1: the same
            // sum as Weed.Bed, which stands the plants by it.
            float weedBed(float3 where)
            {
                return smoothstep(0.5, 0.64, 0.65 * weedSlow(where / 95.0) + 0.35 * weedSlow(where / 27.0));
            }

            float3 skyAt(float3 way, float3 up)
            {
                float high = clamp(dot(way, up), 0.0, 1.0);
                return lerp(_OceanSkyLow.rgb, _OceanSkyTop.rgb, pow(high, 0.42));
            }

            // What lay a given way in the picture the game finished last frame (as light), read as blurred as `blur` says (which of its
            // smaller copies); `there`: how far that way was inside the picture (0: outside it, or no picture).
            float3 lastSeen(float3 way, float blur, out float there)
            {
                float w = dot(_OceanMirrorW.xyz, way);
                float2 uv = float2(dot(_OceanMirrorX.xyz, way), dot(_OceanMirrorY.xyz, way)) / max(w, 1e-4) * 0.5 + 0.5;
                // (Off the picture's side, what lies at the same height just inside it: the sky changes slowly from side to side. The
                // plain sky put in its place there was another brightness (at dusk twice as bright), and the sea brightened towards both
                // sides of the picture in two upright bands. Off its top or bottom nothing of it is known, and the plain sky is used.)
                uv.x = clamp(uv.x, 0.02, 0.98);
                float edge = min(uv.y, 1.0 - uv.y);
                there = w > 1e-3 ? smoothstep(0.0, 0.14, edge) * _OceanMirrorX.w : 0.0;
                float3 seen = SampleLod(_OceanMirror, clamp(uv, ((float2)(0.0)), ((float2)(1.0))), blur).rgb;
                if (_OceanFoam.y > 0.5) seen = pow(max(seen, ((float3)(0.0))), ((float3)(2.2)));
                return seen;
            }

            // The sky a given way, to be mirrored: the real one where the last picture shows it (with its clouds, the land, whatever
            // burns), a plain one where it does not, made to meet the real one at the horizon where the horizon was in the picture.
            float3 skyMirrored(float3 way, float3 up, float sigma)
            {
                float high = clamp(dot(way, up), 0.0, 1.0);
                // (A rough sea mirrors a wide piece of sky at once: as wide as its facets tilt, both ways. But never from below the
                // horizon: there is only more sea there, and it is dark. The picture's smaller copies are made of squares that lie where
                // they lie, not round the place asked for, and reading between two of them takes in a square to either side: so the
                // blur is kept to half of how far above the horizon the place is. At 1.4 times, a bright evening sky of cumulus came
                // back from the far sea a third as bright as it was.)
                float points = _OceanMirrorY.w;
                float blur = clamp(log2(max(min(2.6 * sigma, 0.5 * asin(high)) * points, 1.0)), 0.0, _OceanMirrorW.w);
                float there, low;
                float3 seen = lastSeen(way, blur, there);
                float3 level = way - up * dot(way, up);
                float3 atHorizon = lastSeen(normalize(normalize(level + up * 1e-5) + up * 0.09), max(blur, 3.5), low);
                float3 plainLow = lerp(_OceanSkyLow.rgb, atHorizon, low);
                float lift = lerp(1.0, clamp(dot(atHorizon, float3(0.3, 0.5, 0.2)) / max(dot(_OceanSkyLow.rgb, float3(0.3, 0.5, 0.2)), 1e-3), 0.3, 3.0), low * 0.7);
                float3 plain = lerp(plainLow, _OceanSkyTop.rgb * lift, pow(high, 0.42));
                return lerp(plain, seen, there);
            }

            // The foam's net, read so that it does not come round the same. The net is one picture, and laid side by side it is the
            // same holes again every few metres: in a sheet of foam a hundred metres across the eye finds the pattern at once ("the
            // same texture over and over", of a storm's sea). So the sea is laid out in squares as large as the picture, and each
            // square reads the picture its own way: from a place of its own in it, a little turned, and at a size of its own (seven
            // tenths to half as large again, so that the holes are of many sizes too). Along the squares' edges two readings are
            // mixed over a fifth of a square: mixed so as to keep what they have of light and dark (each taken about the middle
            // value, and their sum made as strong again as one of them: Heitz and Neyret, 2018). Mixed plainly, two unlike nets
            // come to an even grey between them, and the foam, which is drawn where the net stands above a level, was then all
            // white or all clear along every edge: straight bands, and patches of foam with square corners. Which way a square reads is settled by which square it is, counted from where the
            // sea's axes begin, so it stays with the water; it comes round after 64 pictures each way, which nothing shows.
            // `u`: the place, in pictures, counted from that beginning; `dx`, `dy`: how it changes from pixel to pixel.
            float4 laceVaried(float2 u, float2 dx, float2 dy)
            {
                float2 square = floor(u), within = u - square;
                float2 mixed = smoothstep(0.4, 0.6, within);
                float4 sum = ((float4)(0.0));
                float strength = 0.0;
                { [loop] for (int i = 0; i < 4; i++)
                {
                    float2 corner = float2(float(i & 1), float(i >> 1));
                    float share = (corner.x > 0.5 ? mixed.x : 1.0 - mixed.x) * (corner.y > 0.5 ? mixed.y : 1.0 - mixed.y);
                    { [loop] for (int once = share > 0.001 ? 0 : 1; once < 1; once++)
                    {
                        float2 which = Mod(square + corner, 64.0);
                        float4 chance = frac(sin(float4(dot(which, float2(127.1, 311.7)), dot(which, float2(269.5, 183.3)), dot(which, float2(419.2, 371.9)), dot(which, float2(98.3, 541.7)))) * 43758.5453);
                        float turn = (chance.z - 0.5) * 0.6, size = lerp(0.7, 1.5, chance.w);
                        float c = cos(turn) * size, s_ = sin(turn) * size;
                        float2 from = within - corner;
                        sum += share * (SampleGradient(_OceanLace, float2(c * from.x - s_ * from.y, s_ * from.x + c * from.y) + chance.xy,
                                                    float2(c * dx.x - s_ * dx.y, s_ * dx.x + c * dx.y), float2(c * dy.x - s_ * dy.y, s_ * dy.x + c * dy.y)) - 0.5);
                        strength += share * share;
                    } }
                } }
                return clamp(((float4)(0.5)) + sum / sqrt(max(strength, 1e-4)), 0.0, 1.0);
            }

            // The rings a raindrop leaves on water. Of all the drops of a rain only the large ones (two millimetres and more, a few in a
            // thousand) leave rings one can pick out; each sends a ring of ripples out at the speed of the slowest ripples on water (a
            // quarter of a metre a second, at about two centimetres from crest to crest), dying in a fraction of a second. The water
            // pinned in cells of `cell` metres, now and then a drop in each at a place and a moment of its own; given: the slope the
            // rings add there. `rate`: such drops a square metre a second.
            // (a number from 0 to 1 that a cell and a salt give by chance)
            float ringChance(float2 c, float salt)
            {
                float3 q = frac(((float3)(c.xyx)) * float3(0.1031, 0.1030, 0.0973) + salt);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            // Smooth noise from 0 to 1 over the sea, `cells` lattice cells to every 1024 m and repeating there (the place it is read
            // at wraps every 1024 m), with a quintic blend so that no lattice line shows.
            float pawNoise(float2 p, float cells)
            {
                float2 q = p * (cells / 1024.0);
                float2 i = floor(q), f = q - i;
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float2 i0 = Mod(i, cells), i1 = Mod(i + 1.0, cells);
                float a = ringChance(i0, 0.13), b = ringChance(float2(i1.x, i0.y), 0.13), c = ringChance(float2(i0.x, i1.y), 0.13), d = ringChance(i1, 0.13);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // The same, made of slants by chance at the corners (gradient noise) and not of values: about 0 to 1, 0.505 on the whole and
            // as spread as pawNoise's sum. pawNoise's values at the corners showed in the gusts as squares, 64 m and more across, from a
            // few hundred metres up.
            float pawSlant(float2 p, float cells)
            {
                float2 q = p * (cells / 1024.0);
                float2 i = floor(q), f = q - i;
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float2 i0 = Mod(i, cells), i1 = Mod(i + 1.0, cells);
                float ta = ringChance(i0, 0.13) * 6.2832, tb = ringChance(float2(i1.x, i0.y), 0.13) * 6.2832;
                float tc = ringChance(float2(i0.x, i1.y), 0.13) * 6.2832, td = ringChance(i1, 0.13) * 6.2832;
                float a = dot(float2(cos(ta), sin(ta)), f), b = dot(float2(cos(tb), sin(tb)), f - float2(1.0, 0.0));
                float c = dot(float2(cos(tc), sin(tc)), f - float2(0.0, 1.0)), d = dot(float2(cos(td), sin(td)), f - float2(1.0, 1.0));
                return 0.505 + 0.9 * lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float2 rainRings(float2 at, float cell, float clock, float rate)
            {
                float2 slope = ((float2)(0.0));
                float2 base = floor(at / cell);
                float period = 1.0 / max(rate * cell * cell, 0.02);
                { [loop] for (int j = -1; j <= 1; j++)
                    { [loop] for (int i = -1; i <= 1; i++)
                    {
                        float2 c = base + float2(float(i), float(j));
                        float hx = ringChance(c, 0.0), hy = ringChance(c, 0.37), hz = ringChance(c, 0.71);
                        float t = Mod(clock + hz * period, period);
                        if (t > 0.7) continue;
                        float2 d = at - (c + float2(hx, hy)) * cell;
                        float r = length(d);
                        float front = 0.24 * t;
                        float x = r - front;
                        float env = exp(-x * x / (0.0003 + 0.004 * t)) * exp(-t / 0.22) / (1.0 + 25.0 * front);
                        slope += (r > 1e-4 ? d / r : ((float2)(0.0))) * 0.55 * env * cos(314.16 * x);
                    } } }
                return slope;
            }

            // How much weather lies along so many metres of a line that climbs mu a metre, where the weather thins out by e every
            // "soft" metres of height, counted in metres of it as thick as it is at the line's start (as in veil.glsl).
            float veilThinning(float len, float mu, float soft)
            {
                float k = len * mu / soft;
                return abs(k) < 0.001 ? len * (1.0 - 0.5 * k) : soft / mu * (1.0 - exp(-k));
            }

            // And along a line from a camera above the weather's top, "top" metres under it (below nothing), staying above it, as
            // veil.glsl has it: each power of e is of a height above the top and at most 1. (Written as the weather where the camera
            // is times veilThinning(), a camera a few hundred metres over a shallow fog had nothing times infinite, which is not a
            // number: the whole sea, and everything in front of it, came out black.)
            float veilThinningAbove(float top, float len, float mu, float soft)
            {
                float k = len * mu / soft;
                float here = exp(top / soft);
                return abs(k) < 0.001 ? here * len * (1.0 - 0.5 * k) : soft / mu * (here - exp((top - len * mu) / soft));
            }

            // How far off the first solid thing behind a point of the picture is (m, along the camera's axis); 1e7: nothing.
            // The game on OpenGL draws what is near with one camera (to 400 m) and what is far with another, and each knows how far
            // off things are only within its own sight. A point of the sea 390 m off, drawn by the near camera, has its bottom 395 m
            // or 420 m off: past the near camera's sight, so that camera's depth picture has nothing there. Taken for a bottom at
            // 400 m, the sea went clear in a line across the picture; taken for deep water (as it then was), every shallow lying
            // across the hand-over turned the navy of the open sea in a wedge with a ruled edge, its point on the shore. So where the
            // camera drawing has nothing within its sight, the far camera's own depth picture is asked, kept from when it drew (the
            // two cameras stand at the same place and look the same way: the same point of the picture in both). `byFar`: 1 if it
            // was: then what is seen there under the water is to be taken from the far camera's picture as it was BEFORE that camera
            // drew its own sea (_OceanFarScene), not from the picture so far: the two cameras' sights overlap by three metres, both
            // draw the sea there, and the near one's water over the far one's was the bottom seen through water twice, a dark thread
            // across the shallows along the hand-over.
            float behindAt(float2 uv, out float byFar)
            {
                byFar = 0.0;
                float behind = 1.0 / (_ZBufferParams.z * SampleFlat(_CameraDepthTexture, uv).r + _ZBufferParams.w);
                if (behind > 0.998 * _ProjectionParams.z)
                {
                    behind = 1e7;
                    { [loop] for (int once = _OceanFarZ.w > 0.5 ? 0 : 1; once < 1; once++)
                    {
                        float further = 1.0 / (_OceanFarZ.x * SampleFlat(_OceanFarDepth, uv).r + _OceanFarZ.y);
                        if (further < 0.985 * _OceanFarZ.z) { behind = further; byFar = 1.0; }
                    } }
                }
                return behind;
            }

            // How much of the sun's light the waves gather on the bottom at a place, seen from above (1: as much as under a flat sea).
            // The surface is a sheet of lenses: light going down through sloping water is bent towards the uphill side, so under a crest
            // it closes in and at some depth meets (the bright net of lines that runs over sand under clear shallow water), and under a
            // trough it spreads. As the lens's own pass reckons it for things under water (oceanunder.glsl, gathered), here for the
            // bottom seen through the water from the air: `spot` the bottom's place on the sea's two axes, `down` how deep it lies (m),
            // `came` how far towards the sun the light that reaches it entered the water, for each metre down (on the sea's two axes).
            float gatheredOn(float2 spot, float down, float2 came1)
            {
                float2 came = spot + came1 * down;
                float2 turned = float2(_OceanTwin.y * came.x - _OceanTwin.z * came.y, _OceanTwin.z * came.x + _OceanTwin.y * came.y) / _OceanTwin.x;
                float one = _OceanLay.x * _OceanLay.y * _OceanLay.y * _OceanLay.y * _OceanLay.y;
                float closes = 0.0;
                { [loop] for (int n = 4; n < 7; n++)
                {
                    float4 at = n == 4 ? _OceanAt4 : n == 5 ? _OceanAt5 : _OceanAt6;
                    float k = 35.2 * one;
                    float past = 0.25 * down * k * 0.15;
                    float blurred = log2(1.0 + 0.02 * down * one * _OceanLay.z);
                    float first = SampleLod(_OceanSlope, float3(at.xy + came * one, float(n)), blurred).b;
                    float second = SampleLod(_OceanSlope, float3(at.zw + turned * one, float(n)), blurred).b;
                    closes += 0.25 * down * k * (first + second / _OceanTwin.x) / (1.0 + past * past);
                    one *= _OceanLay.y;
                } }
                return 1.0 / max(abs(1.0 + 1.5 * closes * _OceanTwin.w), 0.3);
            }

            float4 frag(v2f IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float3 p = IN.vs_TEXCOORD0;
                float2 spot = IN.vs_TEXCOORD1.xy;
                float3 up = cross(_OceanAxisB.xyz, _OceanAxisA.xyz);
                // (the lens's map: how high the water stands above the middle of the glass, m, and that there is water here)
                if (_OceanMapAt.w > 0.0) { result = float4(dot(p - _OceanMapAt.xyz, up), 0.0, 0.0, 1.0); return result; }
                float3 toEye = _WorldSpaceCameraPos - p;
                float far = length(toEye);
                float3 V = toEye / max(far, 1e-3);
                // ---- the clouds' shadow here (2026-10-17): followed down the sun's line to the ground the weather worked it out on, as
                // its own pass does for what is drawn before the sea (cloudshade.glsl), and fading far off into the craft's own light. The
                // sun's glitter and its light on and in the water go by it; what the water mirrors of the sky does not. (Until then the
                // sea had the craft's one shade over all of it: under broken cloud no sunlit patches and no shadows on the water.)
                float sunW = _OceanSun.w;
                { [loop] for (int shadowed = _WeatherShadowSun.w > 0.5 ? 0 : 1; shadowed < 1; shadowed++)
                {
                    float3 fromMiddle = p - _WeatherShadowCentre.xyz;
                    float r = length(fromMiddle), h = r - _WeatherShadowCentre.w, sunUpHere = dot(fromMiddle / max(r, 1.0), _WeatherShadowSun.xyz);
                    if (sunUpHere < -0.03) break;
                    float3 rel = p - _WeatherShadowSun.xyz * ((h - _WeatherShadowX.w) / max(sunUpHere, 0.06)) - _WeatherShadowAt.xyz;
                    float2 st = float2(dot(rel, _WeatherShadowX.xyz), dot(rel, _WeatherShadowY.xyz)) / _WeatherShadowAt.w + 0.5;
                    if (st.x <= 0.0 || st.x >= 1.0 || st.y <= 0.0 || st.y >= 1.0) { sunW *= _WeatherShadowFade.x; break; }
                    float4 m = SampleLod(_WeatherShadowMap, st, 0.0);
                    float lowest = m.g * 1000.0, highest = m.b * 1000.0;
                    float above = m.r * clamp((highest - h) / max(highest - lowest, 60.0), 0.0, 1.0);
                    float2 edge = min(st, 1.0 - st);
                    float fade = clamp(min(edge.x, edge.y) * 12.0, 0.0, 1.0) * _WeatherShadowFade.y / (_WeatherShadowFade.y + far) * (1.0 - _WeatherShadowFade.z);
                    sunW *= lerp(_WeatherShadowFade.x, exp(-above), fade);
                } }

                // ---- the surface's slope, from every patch and its second laying. A patch read from far off is read smoothed, and what
                // the smoothing takes away is not lost: each patch carries the squares of its slopes beside the slopes, and the mean of
                // the squares less the square of the mean is exactly the roughness that went (Olano and Baker's LEAN mapping, 2010). It
                // widens the mirror instead: the sun's path and the sky's blur are as wide as the whole sea's slopes at any distance.
                float2 dx = ddx(spot), dy = ddy(spot);
                float across = sqrt(max(dot(dx, dx), dot(dy, dy)));
                // (how much the surface is pulled out here: its own area against that of the still sea it was laid on; one on quiet
                // water, two and more on the back of a crest that has gone over)
                float pulledOut = sqrt(length(cross(ddx(p), ddy(p))) / max(abs(dx.x * dy.y - dx.y * dy.x), 1e-9));
                float4 ats[8] = {_OceanAt0, _OceanAt1, _OceanAt2, _OceanAt3, _OceanAt4, _OceanAt5, _OceanAt6, _OceanAt7};
                float all_[8] = {_OceanRoughA.x, _OceanRoughA.y, _OceanRoughA.z, _OceanRoughA.w, _OceanRoughB.x, _OceanRoughB.y, _OceanRoughB.z, _OceanRoughB.w};
                float high[8] = {_OceanHighA.x, _OceanHighA.y, _OceanHighA.z, _OceanHighA.w, _OceanHighB.x, _OceanHighB.y, _OceanHighB.z, _OceanHighB.w};
                // (how deep the water is here, still: in the shallows each patch's waves are only as high as the depth allows, as the mesh has them)
                float deep = max(-IN.vs_TEXCOORD3.x, 0.0);
                float tc = _OceanTwin.y, ts = _OceanTwin.z;
                float2 turned = float2(tc * spot.x - ts * spot.y, ts * spot.x + tc * spot.y) / _OceanTwin.x;
                float2 tdx = float2(tc * dx.x - ts * dx.y, ts * dx.x + tc * dx.y) / _OceanTwin.x, tdy = float2(tc * dy.x - ts * dy.y, ts * dy.x + tc * dy.y) / _OceanTwin.x;
                float2 slope = ((float2)(0.0));
                float squeeze = 0.0, rough = 0.0;
                float one = _OceanLay.x;
                // ---- gusts on the water, and slicks. The wind is never even: its gusts sweep over the sea in patches tens to hundreds
                // of metres across that roughen the short waves and ripples where they are (the darker patches sailors call cat's
                // paws), and drift down the wind; between them the ripples die away in seconds. And where the surface is calm enough,
                // a film of the sea's own oils lies in long smooth streaks down the wind, slicks that shine like a mirror. Both are
                // most of what one sees of a light breeze from afar; in a strong wind the sea is rough all over. (The long waves and
                // the swell are not touched: they do not answer a gust.) Without it a light sea was one even sheen, like oil.
                float gust = 1.0;
                { [loop] for (int once = _OceanGust.z > 0.01 ? 0 : 1; once < 1; once++)
                {
                    // (a smooth pattern of the shader's own, in three sizes, 512, 171 and 64 m, each repeating every 1024 m as the
                    // place it is laid by does; the foam's lace, first used for it, is laid on a square lattice, and from afar its
                    // blotches lined up into a tiled floor. Of slants at its corners, not values: with values its patches were squares)
                    // (from high up the 1024 m that pattern repeats at is a few dozen pixels, and it showed as a grid of dots in the sun's
                    // path (2026-10-14: "clouds and ocean look horrible from far away"). Where a pixel is metres wide the gusts and slicks
                    // are read from the planet itself, which never repeats, at the sizes a sea shows them from the air: patches and rows
                    // of a few hundred metres to kilometres. They hardly drift in the time one looks down from so high. The near pattern
                    // is worked out only where it is seen: from high up it was a sixth of the sea's time on the card for nothing.)
                    float farFrom = smoothstep(1.5, 6.0, across);
                    float2 drifted = _OceanAtSpot.xy + spot - _OceanGust.xy;
                    float fbm = 0.505;
                    { [loop] for (int near_ = farFrom < 0.999 ? 0 : 1; near_ < 1; near_++)
                        fbm = 0.55 * pawSlant(drifted, 2.0) + 0.3 * pawSlant(drifted + 311.0, 6.0) + 0.15 * pawSlant(drifted + 97.0, 16.0); }
                    { [loop] for (int far_ = farFrom > 0.001 ? 0 : 1; far_ < 1; far_++)
                    {
                        float3 onPlanet = normalize(_WeedUp.xyz * _WeedUp.w + _WeedFirst.xyz * spot.x + _WeedSecond.xyz * spot.y) * _WeedUp.w;
                        // (Of slants, each size's lattice turned its own way. With weedSlow's values the cubes showed from high up as
                        // blotches in blocks, a quarter of a kilometre each, and the sun's glint followed them (2026-10-19). The finest
                        // size is let go where a pixel is wider than a tenth of it.)
                        float3 turnedA = float3(dot(onPlanet, float3(0.8165, -0.4082, 0.4082)), dot(onPlanet, float3(0.0, 0.7071, 0.7071)), dot(onPlanet, float3(-0.5774, -0.5774, 0.5774)));
                        float3 turnedB = float3(dot(onPlanet, float3(0.4286, 0.2857, 0.8571)), dot(onPlanet, float3(-0.8571, 0.4286, 0.2857)), dot(onPlanet, float3(-0.2857, -0.8571, 0.4286)));
                        float3 turnedC = float3(dot(onPlanet, float3(0.6, 0.0, -0.8)), dot(onPlanet, float3(0.48, 0.8, 0.36)), dot(onPlanet, float3(0.64, -0.6, 0.48)));
                        float fbmFar = 0.5 + 0.5 * gustNoise(turnedA / 2600.0) + 0.3 * gustNoise(turnedB / 850.0 + 17.0);
                        float fine = 1.0 - smoothstep(30.0, 90.0, across);
                        { [loop] for (int fine_ = fine > 0.001 ? 0 : 1; fine_ < 1; fine_++)
                            fbmFar += 0.2 * fine * gustNoise(turnedC / 290.0 + 41.0); }
                        fbm = lerp(fbm, fbmFar, farFrom);
                    } }
                    // (From afar, calm goes over to rough by a wider ramp. A pixel there holds many patches, and with the near ramp every
                    // patch was outlined by a thin bright line: the glint is brightest at one roughness, between calm and rough, which
                    // a narrow ramp lays as a line round each patch (the "stained glass" from 20-50 km). The same mean roughness.)
                    float paw = smoothstep(lerp(0.36, 0.19, farFrom), lerp(0.72, 0.85, farFrom), fbm);
                    gust = lerp(1.0, lerp(0.15, 1.7, paw), _OceanGust.z);
                } }
                // ---- slicks (2026-10-17). In a light wind the sea's own film (oils from its plankton and the like) is swept by the rolls
                // the wind turns in the water's top layer (Langmuir's) into the lines where they meet, which lie down the wind tens of
                // metres apart and hundreds of metres long; there it damps the ripples and the short waves, and the line lies smooth: a
                // slick, shining like a mirror where the rest of the sea is a sheen. Gone by 8 m/s (Sea.cs).
                { [loop] for (int once = _OceanGust.w > 0.01 ? 0 : 1; once < 1; once++)
                {
                    float2 water = _OceanAtSpot.xy + spot;
                    float2 down = normalize(_OceanWind.xy + float2(1e-5, 0.0)), aside = float2(-down.y, down.x);
                    // (Rows side by side, about 25 m apart, as the rolls lie: the crests of a wave across the wind whose place wanders, a few
                    // metres wide each;
                    // a smooth pattern breaks them into lengths of some hundreds of metres; too far off for a row to be seen, none. Drawn
                    // as where a blotchy pattern passes through its middle, as at first, they were a few long loops with round ends.)
                    // (And not ruled: the rows wander along their length over a few hundred metres, crowd together and spread apart (15 to
                    // 40 m), and are each of their own strength. With one slow wander only, they were straight lines ruled on the sun's path.)
                    float along = dot(water, down), side = dot(water, aside);
                    float rowAt = side * (6.2832 / 25.0) * lerp(0.62, 1.6, pawNoise(float2(along / 10.0, side / 3.0) + 7.0, 4.0))
                                + 14.0 * (pawNoise(float2(along / 3.0, side) + 59.0, 6.0) - 0.5) + 5.0 * (pawNoise(float2(along / 1.5, side) + 131.0, 16.0) - 0.5);
                    float line_ = smoothstep(0.9, 0.98, cos(rowAt)) * lerp(0.35, 1.0, pawNoise(float2(along / 2.0, side) + 211.0, 12.0));
                    float lengths = smoothstep(0.35, 0.6, pawNoise(float2(along / 4.0, side) + 173.0, 10.0));
                    gust *= 1.0 - 0.75 * line_ * lengths * _OceanGust.w * (1.0 - smoothstep(0.6, 3.0, across));
                } }
                { [loop] for (int n = 0; n < 8; n++)
                {
                    // (which of the patch's smaller copies a pixel here wants; past the last of them there is nothing left of it but its roughness)
                    float level = log2(max(across * one * _OceanLay.z, 1e-6));
                    float kept = clamp(0.45 * deep / max(high[n], 1e-3), 0.0, 1.0);
                    float gone = 2.0 * all_[n];
                    // (A patch whose tile is a few dozen pixels across or less is not drawn wave by wave. One tile repeats, and with
                    // every smaller patch already smoothed away it is all that is left of the sea's shape: from the air its repeats
                    // lined up into a grid of bright dots in the sun's path (from 10 km the 147 m patch, from 30 km the 514 m one).
                    // Its slopes go into the roughness instead, where they widen the mirror as a real sea's do: the sun's path from
                    // high up is one broad sheen. Near the camera only the ripples, tens of tiles to the metre, go so.)
                    float shown = smoothstep(16.0, 48.0, 1.0 / max(across * one, 1e-6));
                    { [loop] for (int once = level < 5.5 && shown > 0.001 ? 0 : 1; once < 1; once++)
                    {
                        float4 a = SampleGradient(_OceanSlope, float3(ats[n].xy + spot * one, float(n)), dx * one, dy * one);
                        float4 b = SampleGradient(_OceanSlope, float3(ats[n].zw + turned * one, float(n)), tdx * one, tdy * one);
                        // (the short waves only answer the gusts: the 42 m patch a little, the shorter ones wholly)
                        float answers = lerp(1.0, gust, clamp(float(n) - 2.7, 0.0, 1.0));
                        slope += (a.rg + float2(tc * b.r + ts * b.g, tc * b.g - ts * b.r)) * kept * answers * shown;
                        squeeze += (a.b + b.b) * step(float(n) + 0.5, _OceanLay.w) * kept * shown;
                        // (nothing is taken away where the patch is seen larger than it is kept: there the squares between two points are only smoothed)
                        float lean = (max(a.a - dot(a.rg, a.rg), 0.0) + max(b.a - dot(b.rg, b.rg), 0.0)) * clamp(level + 1.0, 0.0, 1.0);
                        gone = lerp(gone, lean, shown * shown);
                    } }
                    float answersToo = lerp(1.0, gust, clamp(float(n) - 2.7, 0.0, 1.0));
                    rough += gone * kept * kept * answersToo * answersToo;
                    one *= _OceanLay.y;
                } }
                float shore = _OceanShore.x;
                slope = slope * _OceanTwin.w * shore + IN.vs_TEXCOORD3.yz;
                if (_OceanDentAt.w > 0.5)
                {
                    float2 inDent = (spot - _OceanDentAt.xy) * _OceanDentAt.z + 0.5;
                    if (inDent.x > 0.0 && inDent.y > 0.0 && inDent.x < 1.0 && inDent.y < 1.0) slope += SampleLod(_OceanDent, inDent, 0.0).gb;
                }
                if (_OceanWakeAt.w > 0.0)
                {
                    float2 inWake = (spot - _OceanWakeAt.xy) * _OceanWakeAt.z + 0.5;
                    if (inWake.x > 0.0 && inWake.y > 0.0 && inWake.x < 1.0 && inWake.y < 1.0) slope += SampleLod(_OceanWake, inWake, 0.0).gb * _OceanWakeAt.w;
                }
                squeeze *= _OceanTwin.w * shore;
                rough = 0.0004 + rough * _OceanTwin.w * _OceanTwin.w * shore * shore;
                // ---- rain on the sea. Each drop roughens the water where it falls with a ring of ripples a couple of centimetres long:
                // near the camera the rings of the large drops are seen one by one; further off, where a pixel is wider than a ripple,
                // all of them together are a fine roughness that dulls the sea's mirror (as a rain-swept sea looks matt and grey).
                { [loop] for (int once = _OceanRain.x > 0.003 ? 0 : 1; once < 1; once++)
                {
                    float ringsSeen = 1.0 - smoothstep(0.006, 0.02, across);
                    // (the large drops: about six a square metre a second in a steady 5 mm an hour, more than in proportion as rain grows heavier)
                    float big = 60.0 * pow(_OceanRain.x, 1.3);
                    { [loop] for (int near_ = ringsSeen > 0.01 ? 0 : 1; near_ < 1; near_++)
                        slope += ringsSeen * (rainRings(_OceanAtSpot.xy + spot, 0.35, _OceanRain.y, big) + 0.6 * rainRings(_OceanAtSpot.xy + spot + 17.0, 0.17, _OceanRain.y * 1.07 + 3.1, big * 2.0)); }
                    rough += 0.006 * clamp(_OceanRain.x * 3.0, 0.0, 1.0) * (1.0 - ringsSeen * 0.6);
                } }
                float3 N = normalize(up - _OceanAxisA.xyz * slope.x - _OceanAxisB.xyz * slope.y);
                // ---- A crest going over (curlAt, in the vertex program). The waves' slopes know nothing of it: the surface is
                // turned here as the breaker's shape turns it, in the upright plane the wind lies in: over the top of the curl and
                // down its front, facing down at its tip, and inside the tube facing the tube's middle.
                float lipMuch = abs(IN.vs_TEXCOORD4.y), lipAt = IN.vs_TEXCOORD4.z;
                { [loop] for (int once = lipMuch > 0.001 ? 0 : 1; once < 1; once++)
                {
                    // (The water of the curl is drawn out smooth as it goes over: glass, with lines along the way it runs. The
                    // ripples' slopes, which belong to the level sea, made crumpled foil of it; most of them are let go there.)
                    float smoothed = lipMuch * smoothstep(-6.0, -3.0, lipAt) * (1.0 - smoothstep(3.7, 5.0, lipAt));
                    N = normalize(lerp(N, up, 0.8 * smoothed));
                    // (a blow's ring throws its lip the way it runs, out from the crater's middle across its ellipse: see dentAt)
                    float2 lipGoes = _OceanWind.xy;
                    if (IN.vs_TEXCOORD4.y < 0.0)
                    {
                        float2 fromMid = spot - _OceanDentMid.xy;
                        float2 athwartMid = float2(-_OceanDentMid.w, _OceanDentMid.z);
                        float2 runsMid = _OceanDentMid.zw * (dot(fromMid, _OceanDentMid.zw) * _OceanDentCurl.w * _OceanDentCurl.w) + athwartMid * dot(fromMid, athwartMid);
                        lipGoes = length(runsMid) > 1e-4 ? normalize(runsMid) : _OceanDentMid.zw;
                    }
                    float3 downwind = normalize(_OceanAxisA.xyz * lipGoes.x + _OceanAxisB.xyz * lipGoes.y);
                    float2 turn = float2(IN.vs_TEXCOORD4.x, IN.vs_TEXCOORD4.w);
                    float sure = length(turn);
                    turn = sure > 1e-3 ? turn / sure : float2(1.0, 0.0);
                    float c = turn.x, s = turn.y;
                    float nw = dot(N, downwind), nu = dot(N, up);
                    N = normalize(N + downwind * (nw * c - nu * s - nw) + up * (nw * s + nu * c - nu));
                } }
                // (Where on the breaker: under the curl, which is in its shade (the tube's roof, its back wall and the floor under
                // it), only once it has come over; the lip's thin end; its very edge; the top of the curl; and the floor where the
                // lip comes down.)
                float lipOver = smoothstep(0.45, 0.85, lipMuch);
                float lipUnder = lipMuch * lipOver * smoothstep(0.1, 0.5, lipAt) * (1.0 - smoothstep(4.2, 6.0, lipAt));
                float lipWall = lipMuch * smoothstep(0.15, 0.5, lipAt) * (1.0 - smoothstep(3.2, 3.8, lipAt));
                float lipThin = lipMuch * (1.0 - smoothstep(0.1, 1.2, abs(lipAt)));
                float lipEdge = lipMuch * (1.0 - smoothstep(0.03, 0.14, abs(lipAt)));
                float lipFloor = lipMuch * lipOver * smoothstep(3.4, 4.0, lipAt) * (1.0 - smoothstep(4.8, 6.2, lipAt));
                float lipTop = lipMuch * smoothstep(-5.0, -2.6, lipAt) * (1.0 - smoothstep(-0.2, -0.04, lipAt));
                float facing = max(dot(N, V), 0.02);

                // ---- how much is mirrored (Fresnel). A rough sea seen along mirrors less than a flat one would: its facets that face the
                // eye are the ones that are seen (Bruneton, Neyret and Holzschuch, 2010: the mean over the facets, fitted).
                float sigma = sqrt(rough);
                float mirror = 0.02 + 0.98 * pow(1.0 - facing, 5.0 * exp(-2.69 * sigma)) / (1.0 + 22.7 * pow(sigma, 1.5));
                float3 R = reflect(-V, N);
                float lift = dot(R, up);
                // (a surface that faces down, the underside of a lip, mirrors the water below it and not the sky: see where the two are put together)
                float mirrorsWater = clamp(-lift * 2.5, 0.0, 1.0) * step(0.001, lipMuch);
                if (lift < 0.02) R = normalize(R + up * (0.02 - lift) * 2.0);
                // (the mean of what a rough sea's facets mirror lies higher than what a flat one would: the facets that tip the sky down
                // towards the eye are hidden behind their neighbours)
                // (for finding what the sea costs, from the development build: tests 41 and 42 leave the mirrored sky's readings out)
                float3 sky = abs(_OceanShore.y - 41.0) < 0.5 || abs(_OceanShore.y - 42.0) < 0.5 ? _OceanSkyTop.rgb : skyMirrored(normalize(R + up * 0.5 * sigma), up, sigma);

                // ---- the sun's path: every facet turned so as to mirror the sun, as many as there are at that tilt
                float3 L = _OceanSun.xyz;
                float3 H = normalize(L + V);
                float nh = max(dot(N, H), 1e-3);
                // (`rough` is the mean square slope both ways together: half of it each way)
                float spread = max(rough, 0.0004);
                float tilt2 = (1.0 - nh * nh) / (nh * nh);
                float facets = exp(-tilt2 / spread) / (PI * spread * nh * nh * nh * nh);
                float glint = facets * (0.02 + 0.98 * pow(1.0 - max(dot(H, V), 0.0), 5.0)) / (4.0 * facing * max(dot(N, L), 0.05) + 0.05);
                // The sun's path is not a sheen but points of light: each is one place on a ripple turned just so. How many there are in
                // a patch of water is known (Longuet-Higgins, 1960): as many as there are facets of that tilt (worked out above) times
                // how sharply the ripples curve, which for a wind sea comes to some hundreds to the square metre where the path is
                // brightest, one every few centimetres, and a hundredth of that at its edges. A pixel of sea far off holds thousands
                // and is a sheen; near the eye, or at the path's edge, it holds one or two or none, and that is the sparkle. So the
                // brightness above, which is the mean, is dealt out by chance: a patch of water the size of a pixel (pinned to the
                // water, changing twenty times a second, as ripples do) is given a number of bright points by the odds for that mean.
                float cellSize = exp2(floor(log2(max(across, 1e-3))));
                float sparkle = 1.0, expectedShown = 0.0;
                { [loop] for (int once = glint > 1e-5 ? 0 : 1; once < 1; once++)
                {
                    // (450 to the square metre at the full count; a fifteenth of that here. A pixel of this picture is five times as wide
                    // as what the eye can tell apart, so it holds twenty-five times the points an eye's "pixel" would, and with the full
                    // count the path was a dull sheen at any distance: the eye, looking at a real sea, sees it sparkle. So the points
                    // are made as few and as bright as the eye would have them.)
                    float expected = cellSize * cellSize / max(facing, 0.05) * facets * 30.0;
                    expectedShown = expected;
                    // (chance from whole numbers: the usual trick of a sine of a large number gave nought for every patch on this
                    // graphics card, and every patch had its bright point)
                    int2 cell = ((int2)(floor((spot + _OceanAtSpot.xy) / cellSize)));
                    float h1 = chance(uint3(uint(cell.x + 65536), uint(cell.y + 65536), 17u));
                    int tick = int(floor(_OceanFoam.w * 20.0 + h1 * 7.0));
                    float h2 = chance(uint3(uint(cell.x + 65536), uint(cell.y + 65536), uint(tick) + 101u));
                    if (expected > 12.0) sparkle = 1.0 + (h2 - 0.5) * 3.46 / sqrt(expected);
                    else
                    {
                        // (how many points, by Poisson's odds: none with odds e to the minus the mean, and so on up)
                        float odds = exp(-expected), sum = odds, count = 0.0;
                        { [loop] for (int k = 1; k < 24; k++)
                        {
                            if (h2 <= sum) break;
                            count += 1.0; odds *= expected / count; sum += odds;
                        } }
                        sparkle = count / max(expected, 1e-5);
                        // (Between the two there is a kind of patch that is neither: one that holds a point or two or three. Dealt out
                        // by chance it is a coarse grey grain, soot on the water, which is what the sea looked like from eighty metres
                        // up with the sun overhead. A patch that would hold more than a point or so is given the mean instead: a sheen.)
                        sparkle = lerp(sparkle, 1.0, smoothstep(0.35, 2.5, expected));
                    }
                } }
                // (In the game's numbers a white thing in the sun is about 1: the sun's light over pi. So the facets' mirror of the sun
                // comes to pi times the share worked out above: at the middle of the path, taken over all its points of light, about as
                // bright as the sky. At six times, as it first was, the middle of the path was a white sheet with a glow round it. A
                // single point of light is far brighter than white and is let burn out, but not without limit: the lens spreads what
                // is over white into a glow.)
                float3 sunPath = _OceanSunTint.rgb * sunW * min(glint * 3.14 * sparkle, 14.0) * max(dot(N, L), 0.0);

                // ---- the water's own colour: sunlight and skylight scattered back out of it
                float sunHigh = max(dot(L, up), 0.0);
                float3 lit_ = _OceanSunTint.rgb * sunW * (0.35 + 0.65 * sunHigh) + skyAt(up, up) * 0.9;
                // (under a lip: less of the sky's light gets in)
                lit_ *= 1.0 - 0.3 * lipUnder;
                float3 body = _OceanWater.rgb * lit_;
                // (the lip's end is thin water with light coming through it from above and behind: a little paler and greener than the
                // sea, the more the nearer its edge. All of the lip so, and as bright as the milky water under foam, it was a strip
                // of lagoon lying on a grey sea.)
                body = lerp(body, float3(0.05, 0.19, 0.19) * lit_, 0.5 * lipThin);
                // (A crest seen against the light: the sun comes through its thin top, green. Only where the water really is thin: a
                // sharp crest, which is where it is squeezed, and the higher on its wave the more. Lit by height alone, as it first was,
                // the whole back of a long swell glowed in a green band across the sea.)
                float waveHigh = max(_OceanSunTint.w, 0.2);
                float thin = clamp(-squeeze * 3.2 - 0.5, 0.0, 1.0) * clamp(0.3 + dot(p - _OceanMid.xyz, up) / waveHigh, 0.0, 1.0);
                float against = pow(max(dot(V, -normalize(L - up * dot(L, up) + up * 0.15)), 0.0), 3.0);
                body += float3(0.03, 0.21, 0.15) * _OceanSunTint.rgb * sunW * thin * against * (0.4 + 0.6 * (1.0 - sunHigh)) * 1.3;

                // ---- what is under the water, seen through it where it is shallow
                float2 screen = FragCoord(IN.pos).xy / _ScreenParams.xy;
                // (how far off the bottom is: see behindAt for the two cameras)
                float byFar;
                float behind = behindAt(screen, byFar);
                // (the ground as the picture has it, kept for the surf seen from far off: see below)
                float behindDrawn = behind;
                // (Within the seabed's maps and past the first kilometre or two, how far off the bottom is, from the maps: the game draws the
                // ground far from the camera in coarse triangles, flat between their corners, and a triangle running from land out to deep
                // water showed its shallow end through the water inside a straight edge, which changed as the camera moved (the owner: "the
                // ocean glitches a lot with land where it interacts", 2026-10-19). The maps have the ground as it is, from the game's own
                // heights. Near the camera, where the ground is drawn finely and a sunken craft or a pier's legs stand on it, the camera's
                // picture of depths as before.)
                // (and only where the maps have been asked: a point not yet asked holds deep water (Seabed.Unknown, -1000 m), and coming into
                // new sky fast the maps fill a second or two behind the camera; there the picture of depths, as before, rather than a square of
                // deep water that turns shallow as they fill)
                float mapsKnow = (1.0 - smoothstep(0.30, 0.38, max(abs(spot.x), abs(spot.y)) / (128.0 * _SeabedCell[3]))) * smoothstep(800.0, 2500.0, far)
                               * smoothstep(-900.0, -600.0, IN.vs_TEXCOORD3.x);
                float behindMaps = IN.vs_TEXCOORD1.z * (1.0 + max(-IN.vs_TEXCOORD3.x, 0.0) / max(dot(V, up), 0.02) / max(far, 1.0));
                behind = lerp(behind, behindMaps, mapsKnow);
                float through = max(behind - IN.vs_TEXCOORD1.z, 0.0);
                // (bent by the surface: looked up a little to one side of where it is)
                float2 bent = screen + slope * 0.04 * clamp(through / 4.0, 0.0, 1.0) / max(IN.vs_TEXCOORD1.z * 0.05, 1.0);
                float byFarBent;
                float behindBent = lerp(behindAt(bent, byFarBent), behindMaps, mapsKnow);
                if (behindBent < IN.vs_TEXCOORD1.z) { bent = screen; byFarBent = byFar; } else through = max(behindBent - IN.vs_TEXCOORD1.z, 0.0);
                float3 under = byFarBent > 0.5 ? SampleFlat(_OceanFarScene, bent).rgb : SampleFlat(_OceanScene, bent).rgb;
                // (the game's picture is kept as numbers for the screen, not as light: turned to light here, and the sea's colour turned back at the end)
                if (_OceanFoam.y > 0.5) under = pow(max(under, ((float3)(0.0))), ((float3)(2.2)));
                // (Beds of weed on the bottom, seen from above as the dark patches in pale water over sand: where they are is a matter of
                // the place on the planet, by the same slow pattern the plants themselves are stood by near the camera: Weed.cs, Bed.
                // The place and its depth are those of the bottom as it is DRAWN, worked back from how far off the picture has it: the
                // game's terrain, asked how high it stands, answers with bumps of several metres that its drawn ground, away from the
                // craft, does not have, and beds set by the asked depth lay in blocks that followed nothing one could see.)
                float bedShown = 0.0;
                { [loop] for (int once = through < 40.0 && behindBent < 1e6 ? 0 : 1; once < 1; once++)
                {
                    float3 bottom = _WorldSpaceCameraPos - V * (behindBent * far / max(IN.vs_TEXCOORD1.z, 1e-3)) - _OceanMid.xyz;
                    float below = dot(bottom, up);
                    float3 level = bottom - up * below;
                    float deepSeen = -(below + dot(level, level) / (2.0 * _OceanMid.w));
                    float3 planet = normalize(_WeedUp.xyz * _WeedUp.w + _WeedFirst.xyz * dot(level, _OceanAxisA.xyz) + _WeedSecond.xyz * dot(level, _OceanAxisB.xyz)) * _WeedUp.w;
                    float bed = weedBed(planet) * smoothstep(0.8, 1.8, deepSeen) * (1.0 - smoothstep(8.0, 11.0, deepSeen)) * (1.0 - _WeedFirst.w);
                    bedShown = bed;
                    // (as dark as a meadow of sea grass is from above: nearly black-green against the bright sand)
                    under = lerp(under, under * float3(0.10, 0.20, 0.09) + float3(0.0015, 0.004, 0.0015) * lit_, bed * 0.92);
                    // (And the sun's light on the bottom, gathered by the waves into the net of bright lines that runs over sand under
                    // clear shallow water, seen from the air as from a boat (2026-10-18): the game drew the bottom lit as under no water
                    // at all, so its light is gathered here by as much as the waves gather it there. The light that reaches the bottom
                    // came in towards the sun, bent down by the water (1.333); the net is sharpest a metre or two down, and spread out
                    // and lost by some ten metres, as the gathering's own blur has it; none where the sun is not on the water.)
                    float3 bentSun = refract(-L, up, 0.75);
                    float downward = max(-dot(bentSun, up), 0.2);
                    float2 towardsSun = -float2(dot(bentSun, _OceanAxisA.xyz), dot(bentSun, _OceanAxisB.xyz)) / downward;
                    float2 bottomSpot = float2(dot(level, _OceanAxisA.xyz), dot(level, _OceanAxisB.xyz));
                    float netted = sunW * smoothstep(0.03, 0.2, dot(L, up)) * smoothstep(0.08, 0.5, deepSeen) * (1.0 - smoothstep(6.0, 14.0, deepSeen))
                                 * (1.0 - bed * 0.8) * (1.0 - step(abs(_OceanShore.y - 50.0), 0.5));     // (development build: test 60 leaves it out)
                    { [loop] for (int caustic = netted > 0.01 ? 0 : 1; caustic < 1; caustic++)
                        under *= lerp(1.0, gatheredOn(bottomSpot, deepSeen, towardsSun), netted); }
                } }
                // (water takes the red first: what comes back from the bottom is green, then blue, then nothing)
                float3 kept = exp(-through * float3(0.42, 0.075, 0.045) * (12.0 / max(_OceanWater.w, 1.0)));
                body = lerp(body, under, kept * clamp(1.0 - through / (4.0 * max(_OceanWater.w, 1.0)), 0.0, 1.0));

                // (the tube is dim, not black: the sky's light comes in at its open side)
                sky = lerp(sky * (1.0 - 0.4 * lipUnder), body * 1.2, mirrorsWater);
                float3 colour = lerp(body, sky, mirror) + sunPath * (1.0 - lipUnder);

                // ---- foam (see Foam.cs): how white the water here is, from the finest of the four maps that reaches this far; and the
                // same read much blurred, for the bubbles that a breaker drives under the water round where it broke. A map's points are
                // read a little to one side or the other by a fine pattern pinned to the water (the lace's blotches), more for the
                // coarser maps: one white point of a coarse map is then a torn speck, not a soft round blob.
                float white = 0.0, under2 = 0.0;
                // (how far apart the points are of the map the white is read from here, 0 to 1: past every map, as if far apart)
                float mapCoarse = 1.0;
                float out_ = max(abs(spot.x), abs(spot.y));
                float2 shake = ((float2)(0.0)), tear = ((float2)(0.0));
                { [loop] for (int once = _FoamReach.x > 0.5 ? 0 : 1; once < 1; once++)
                {
                    shake = SampleGradient(_OceanLace, _OceanLaceAt.xy + spot * _FoamReach.z * 0.37, dx * _FoamReach.z * 0.37, dy * _FoamReach.z * 0.37).ab
                          + SampleGradient(_OceanLace, _OceanLaceAt.zw + spot * _FoamReach.z * 2.1, dx * _FoamReach.z * 2.1, dy * _FoamReach.z * 2.1).ba - 1.0;
                    // (and a finer pattern, a metre or so, for the edge of fresh froth: see below)
                    tear = SampleGradient(_OceanLace, _OceanLaceAt.zw * 1.7 + spot * _FoamReach.w * 0.55 + 0.27, dx * _FoamReach.w * 0.55, dy * _FoamReach.w * 0.55).ab * 2.0 - 1.0;
                } }
                { [loop] for (int l = 3; l >= 0; l--)
                {
                    float span = _FoamReach.y * Pick(_FoamTexel, l);
                    float inside = 1.0 - smoothstep(0.34, 0.42, out_ / span);
                    { [loop] for (int once = inside > 0.0 ? 0 : 1; once < 1; once++)
                    {
                        float2 at = l == 0 ? _FoamAtA.xy : l == 1 ? _FoamAtA.zw : l == 2 ? _FoamAtB.xy : _FoamAtB.zw;
                        float2 uv = at + spot / span;
                        // (how many of the map's points a pixel covers: where it covers less than one, the map is being enlarged)
                        float coarse = clamp(Pick(_FoamTexel, l) / max(across, 1e-3) - 1.0, 0.0, 1.0);
                        float wide = SampleLod(_OceanFoamMap, uv, max(log2(max(across / Pick(_FoamTexel, l), 1e-3)), 0.0) + 2.6)[l];
                        // (Fresh froth is not a sheet with a clean edge, "a floe of snow" from a mast: it is torn at its edge into tongues and
                        // fingers a metre or two long, with holes, as the breaker throws it and the water under it boils. So where a patch is
                        // fresh, its map is read pulled about by a pattern of a metre or so as well, by up to three of its points.)
                        // (the development build's test 61 leaves this out, to compare)
                        float fresh = smoothstep(0.35, 0.75, wide) * step(0.5, abs(_OceanShore.y - 61.0));
                        float here = SampleGradient(_OceanFoamMap, uv + (shake * 0.9 + tear * 2.2 * fresh) * (coarse / _FoamReach.y), dx / span, dy / span)[l];
                        here = lerp(here, smoothstep(0.06, 0.3, here) * max(here, 0.5), coarse * step(0.5, float(l)));
                        white = lerp(white, here, inside);
                        mapCoarse = lerp(mapCoarse, smoothstep(12.0, 28.0, Pick(_FoamTexel, l)), inside);
                        under2 = lerp(under2, wide, inside);
                    } }
                } }
                mapCoarse *= step(0.5, abs(_OceanShore.y - 65.0));     // (the development build's test 75 leaves the surf seen from far off out, to compare)
                // (The surf seen from far off (2026-10-20). Where the white water's map has its points tens of metres apart, the band the
                // breakers leave along a shore is a point or two of it wide; read as the open sea's white is, pulled about by up to three
                // points and cut at a level, it came out a pale band torn into teeth along the land (the owner, of a strong wind seen from a
                // few kilometres off: "still ocean glitches against the land"). A real one from there is a band of white from the water's
                // edge out to where the biggest waves break, about as deep as they are high, its outer edge in lobes where the sets break
                // further out. So there it is drawn so: by the water's depth over the ground as the picture has it, which puts its inner
                // edge exactly on the shore that is seen, and only where the maps of the ground find it shallow too (a coarse triangle of
                // the far ground can lie shallow far out; the maps reach some seven kilometres). The map's own white goes from those
                // shallows; out at sea it stays.)
                { [loop] for (int farSurf = mapCoarse > 0.001 ? 0 : 1; farSurf < 1; farSurf++)
                {
                    float sets = pawSlant(_OceanAtSpot.xy + spot, 12.0);
                    float breaksIn = (1.3 * _OceanSunTint.w + 0.3) * lerp(0.6, 1.4, sets);
                    float overDrawn = max(behindDrawn - IN.vs_TEXCOORD1.z, 0.0) * far / max(IN.vs_TEXCOORD1.z, 1e-3) * max(dot(V, up), 0.0);
                    float band = (1.0 - smoothstep(0.3 * breaksIn, breaksIn, overDrawn)) * step(behindDrawn, 1e6) * step(0.0, dot(toEye, up))
                               * (1.0 - smoothstep(breaksIn + 4.0, 3.0 * breaksIn + 12.0, deep));
                    float shallows = 1.0 - smoothstep(2.0 * breaksIn + 6.0, 4.0 * breaksIn + 20.0, deep);
                    float surfFar = band * lerp(0.55, 0.95, smoothstep(0.4, 3.0, _OceanSunTint.w)) * lerp(0.75, 1.0, sets);
                    white = lerp(white, max(white * (1.0 - shallows), surfFar), mapCoarse);
                    under2 = lerp(under2, max(under2 * (1.0 - shallows), surfFar), mapCoarse);
                } }
                // (how far inside a patch of white this is: 1 well inside, towards a half at its edge: the white over a wider round
                // against the white here)
                float inner = clamp(under2 / max(white, 0.02), 0.0, 1.0);
                white *= shore; under2 *= shore;
                // (rain knocks the sea down: it damps the short waves, as a film of oil does, and fewer crests break)
                white *= 1.0 - 0.35 * clamp(_OceanRain.x * 3.0, 0.0, 1.0);
                // (A crest going over. The white water's maps lay the froth on the forward face from the crest's line down: on the
                // breaker's shape that is the lip's underside and the wall of the hollow, which are clear dark water in a real one,
                // with the froth at the lip's edge, where it tears into drops, and on the hollow's floor, where it comes down.)
                // (And the top of the lip is water that has not yet broken: smooth and dark, with strings of old foam drawn over it,
                // not a sheet of froth. All white, top, edge and floor, a breaker seen from in front was one white band with no
                // shape in it.)
                // (The floor's froth is where the lip comes down, a size or two wide, and torn: laid evenly over all the floor it was
                // a white rug spread in front of every breaker.)
                white = max(white * (1.0 - 0.95 * lipWall) * (1.0 - 0.8 * lipTop), max(0.95 * lipEdge, 0.62 * lipFloor * clamp(0.35 + 1.3 * shake.x, 0.0, 1.0)));
                // (the surf's breaking front as the mesh has it this moment: the maps have it too, a frame late and less finely; far off,
                // where the mesh's points are tens of metres apart too, the band above has it)
                white = max(white, IN.vs_TEXCOORD3.w * (1.0 - mapCoarse));
                // (And the water's very edge: wherever the water standing over the drawn ground is a hand deep or less, it is white:
                // the thin sheet that runs up the sand and back. By the ground as the picture has it, so it lies exactly along the shore
                // that is seen.)
                float standing = max(behind - IN.vs_TEXCOORD1.z, 0.0) * far / max(IN.vs_TEXCOORD1.z, 1e-3) * max(dot(V, up), 0.0);
                white = max(white, (1.0 - smoothstep(0.02, 0.35, standing)) * step(behind, 1e6) * step(0.0, dot(toEye, up)) * 0.7);
                // (the bubbles under: the water milky and green there, long after the white on top has gone)
                // (not on the curl, outside or in: that water has not been under a breaker yet)
                colour += (float3(0.16, 0.50, 0.46) * lit_ * 0.55 - body) * clamp(under2 * 1.5, 0.0, 0.7) * (1.0 - mirror) * (1.0 - 0.85 * lipMuch * smoothstep(-5.0, -2.6, lipAt) * (1.0 - smoothstep(3.4, 4.0, lipAt)));
                // Fresh foam is solid froth; as it thins it opens into holes, then a net of lines with a thin film of small bubbles
                // between them, then nothing. The lace says in which order the points of a patch go (1 the last); it is laid along the
                // wind and stretched that way (a breaker leaves its foam in a trail, and the wind draws it out), bent by its own slow
                // blotches so that no two patches open alike. Too far off for the lace to be seen, foam is simply that much white.
                float foam = 0.0;
                // In a gale there is a second kind of white: long streaks of foam lying down the wind (from about 14 m/s; in a storm the
                // sea is "white with dense streaks"). They are the old foam of many breakers, gathered into lines by the rolls the wind
                // sets up in the upper water (Langmuir's), which last far longer than the foam maps remember. So they are drawn as a
                // pattern pinned to the water, forty times as long along the wind as across it, over as much of the sea as the wind
                // makes streaked (a twentieth in a gale, a seventh in a storm), and more where breakers have lately been.
                float streaked = _OceanStreak.x * shore * step(0.02, deep);
                float order = 0.5, grain = 0.5, blotch = 0.5, lines = 0.5;
                // (for finding what the sea costs, from the development build: tests 40 and 42 leave the foam's pattern out)
                bool costFoam = abs(_OceanShore.y - 40.0) > 0.5 && abs(_OceanShore.y - 42.0) > 0.5;
                { [loop] for (int once = (white > 0.003 || streaked > 0.004) && costFoam ? 0 : 1; once < 1; once++)
                {
                    float seenBig = 1.0 - smoothstep(0.04, 0.25, across * _FoamReach.z), seenFine = 1.0 - smoothstep(0.04, 0.25, across * _FoamReach.w);
                    { [loop] for (int way = 0; way < 2; way++)
                    {
                        float share = way == 0 ? 1.0 - _OceanLaceMix.x : _OceanLaceMix.x;
                        { [loop] for (int again = share > 0.001 && _FoamReach.x > 0.5 ? 0 : 1; again < 1; again++)
                        {
                            float2 along = way == 0 ? _OceanLaceTurn.xy : _OceanLaceTurn.zw;
                            float4 origin = way == 0 ? _OceanLaceAt : _OceanLaceAtB;
                            float4 counted = way == 0 ? _OceanLaceCount : _OceanLaceCountB;
                            float2 across2 = float2(-along.y, along.x);
                            float2 la = float2(dot(spot, along) / _OceanLaceMix.y, dot(spot, across2));
                            float2 lx = float2(dot(dx, along) / _OceanLaceMix.y, dot(dx, across2)), ly = float2(dot(dy, along) / _OceanLaceMix.y, dot(dy, across2));
                            // (the net is pulled about by its own slow blotches read much larger, by up to a metre: left as made, its holes
                            // are all of a size and stand in rows, and a patch of foam is a sheet of punched card)
                            float2 pulled = la + (SampleGradient(_OceanLace, origin.xy * 0.23 + la * _FoamReach.z * 0.23 + 0.31, lx * _FoamReach.z * 0.23, ly * _FoamReach.z * 0.23).ba - 0.5) * 2.0;
                            float4 big = laceVaried(counted.xy + origin.xy + pulled * _FoamReach.z, lx * _FoamReach.z, ly * _FoamReach.z);
                            float2 bent = pulled + (big.ab - 0.5) * 0.22;
                            float4 fine = laceVaried(counted.zw + origin.zw + bent * _FoamReach.w, lx * _FoamReach.w, ly * _FoamReach.w);
                            // (The small net comes round again every metre and a bit, and over a wide sheet of foam seen from the air that
                            // showed as a lattice. So it is read a second time, turned and a little larger, and which of the two is used
                            // changes from place to place with the slow blotches: no two stretches of a sheet are netted alike.)
                            // (laceVaried does it now, for both nets: the second reading is gone)
                            float o = lerp(0.5, big.r, seenBig) * (1.0 - 0.38 * seenFine) + fine.g * 0.38 * seenFine;
                            // The roller. Where the water is breaking this moment the froth is not lying on the sea: it is tumbling
                            // down the wave's forward face, faster than the wave, and that running white is what a breaking crest
                            // mostly is (nearly every breaker on deep water spills: wiki/ksp-natural-ocean/Research.md, 8). So
                            // there the froth's pattern is not the one pinned to the water but the same pattern running down the
                            // wind, at a third of the wind's speed; as the froth falls in behind the crest (the white going from 1
                            // towards 0.7 in half a second) it is handed back to the pinned one, and lies.
                            float rolls = smoothstep(0.72, 0.96, white);
                            { [loop] for (int once2 = rolls > 0.001 ? 0 : 1; once2 < 1; once2++)
                            {
                                float2 ran = float2(_OceanFoam.w * _OceanWind.z * 0.33 / _OceanLaceMix.y, 0.0);
                                float2 la2 = pulled - ran;
                                float4 big2 = laceVaried(counted.xy + origin.xy + la2 * _FoamReach.z, lx * _FoamReach.z, ly * _FoamReach.z);
                                float4 fine2 = laceVaried(counted.zw + origin.zw + (la2 + (big2.ab - 0.5) * 0.22) * _FoamReach.w, lx * _FoamReach.w, ly * _FoamReach.w);
                                o = lerp(o, lerp(0.5, big2.r, seenBig) * (1.0 - 0.38 * seenFine) + fine2.g * 0.38 * seenFine, rolls);
                                fine.b = lerp(fine.b, fine2.b, rolls);
                                big.a = lerp(big.a, big2.a, rolls);
                            } }
                            order = way == 0 ? o : lerp(order, o, share);
                            grain = way == 0 ? fine.b : lerp(grain, fine.b, share);
                            blotch = way == 0 ? big.a : lerp(blotch, big.a, share);
                            // (the streaks: the same picture, far longer along the wind, at two widths)
                            float2 so = way == 0 ? _OceanStreakAt.xy : _OceanStreakAt.zw;
                            float2 sa = float2(la.x * _OceanStreak.z, la.y) * _OceanStreak.y, sx = float2(lx.x * _OceanStreak.z, lx.y) * _OceanStreak.y, sy = float2(ly.x * _OceanStreak.z, ly.y) * _OceanStreak.y;
                            // (A streak lies where a slow pattern, drawn far out along the wind, passes through its middle value: a thin
                            // line that wanders, never a band. A second pattern, less drawn out, breaks the lines into lengths. The net of
                            // cells the lace itself is made of will not do: drawn out, its cross-walls made a brickwork of the sea.)
                            float wander = SampleGradient(_OceanLace, so + sa, sx, sy).a, wander2 = SampleGradient(_OceanLace, so * 2.9 + sa * 2.9 + 0.19, sx * 2.9, sy * 2.9).a;
                            float lengths = SampleGradient(_OceanLace, so * 0.7 + float2(sa.x * 6.0, sa.y * 0.7) + 0.43, float2(sx.x * 6.0, sx.y * 0.7), float2(sy.x * 6.0, sy.y * 0.7)).a;
                            float l = max(1.0 - abs(wander - 0.5) * 2.0, (1.0 - abs(wander2 - 0.5) * 2.0) * 0.96) * smoothstep(0.3, 0.6, lengths);
                            lines = way == 0 ? l : lerp(lines, l, share);
                        } }
                    } }
                    // (Where the surface is pulled out, the foam's pattern, which is laid on the still sea, is pulled out with it
                    // into streaks ("texture stretches too for the white water tallest points"). There the froth is drawn without
                    // its pattern: plain, as dense froth is.)
                    // (nor on a curl at all: its water is being drawn out and rolled, and the level sea's pattern on it was streaks:
                    // "a lot of stretched textures", the user)
                    float smeared = max(smoothstep(1.35, 2.2, pulledOut), lipMuch * smoothstep(-6.0, -4.0, lipAt) * (1.0 - smoothstep(4.5, 6.5, lipAt)));
                    order = lerp(order, 0.72, smeared); grain = lerp(grain, 0.5, smeared);
                    // (uneven: here it goes sooner, there it lasts; and sooner at a patch's edge than well inside it, so that the edge
                    // is torn into tongues and strings, not cut)
                    // (And the fading white shows for longer: a breaker's patch is still plainly white when half its froth has gone, which
                    // is how photographs of the sea come to the cover they measure (Monahan and O'Muircheartaigh: 1.8% of the sea at 12 m/s,
                    // 4.9% at 16, 10.5% at 20). The white water's maps hold that much (any white at all: 2.1, 4.9 and 10.9%, measured
                    // 2026-10-17); taken as it is, the fading part was a thin lace hardly seen, and the sea showed a third of it.)
                    float much = clamp(pow(white, 0.7) * (0.62 + 0.76 * blotch) * lerp(0.45, 1.0, smoothstep(0.35, 0.9, inner)), 0.0, 1.0);
                    // (And old foam is not one even lace (2026-10-17): as it ages the rolls the wind turns in the water and the water's own
                    // cells gather it into bands down the wind and strings between them, with thin and open water between; fresh froth is
                    // still one sheet. As it was, a patch long broken went white everywhere alike, and a gale's sea from the air was the
                    // same speckle over tens of metres. Bands some seven metres apart, five times as long down the wind, and finer
                    // strings in them; too far off for them to be seen, foam as before.)
                    { [loop] for (int aging = white < 0.65 ? 0 : 1; aging < 1; aging++)
                    {
                        float2 dn = normalize(_OceanWind.xy + float2(1e-5, 0.0)), sd = float2(-dn.y, dn.x);
                        float2 wat = _OceanAtSpot.xy + spot;
                        float bands = pawNoise(float2(dot(wat, dn) / 5.0, dot(wat, sd)), 150.0) * 0.65 + pawNoise(float2(dot(wat, dn) / 2.0, dot(wat, sd)) + 77.0, 380.0) * 0.35;
                        float aged = smoothstep(0.65, 0.2, white) * (1.0 - smoothstep(0.5, 2.5, across));
                        much *= lerp(1.0, 0.25 + 1.5 * smoothstep(0.3, 0.75, bands), aged);
                    } }
                    // (How much of the water a patch covers rises fast with how much foam there is: the skin of bubbles a breaker leaves is
                    // white over most of its patch for some seconds, then a lace, then streaks. Measured at sea, most of a gale's white is this.)
                    float cover = 1.0 - pow(1.0 - much, 2.2);
                    float soft = 0.07 + 0.2 * (1.0 - seenBig);
                    float net = smoothstep(-soft, soft, order - (1.0 - cover));
                    float film = 0.42 * smoothstep(0.0, 0.4, much) * (0.55 + 0.45 * grain * seenFine + 0.45 * (1.0 - seenFine) * 0.5);
                    foam = lerp(cover * 0.85, max(net * (0.84 + 0.16 * grain), film), seenBig);
                    // (as much of the sea streaked as the wind makes it, more round where breakers have been; too far off for single
                    // streaks to be told apart, that much of a pale wash)
                    float much2 = clamp(streaked * (0.6 + 0.8 * blotch) * (1.0 + 2.0 * clamp(under2 * 3.0, 0.0, 1.0)), 0.0, 0.3);
                    float seenLines = 1.0 - smoothstep(0.02, 0.12, across * _OceanStreak.y * 2.9);
                    float streak = lerp(much2 * 0.5, smoothstep(1.0 - much2 - 0.03, 1.0 - much2 + 0.03, lines), seenLines);
                    // (a streak is a string of scraps of foam, not a painted line: the lace breaks it up, and it is thin stuff)
                    foam = max(foam, streak * (0.16 + 0.12 * grain) * lerp(1.0, 0.25 + 1.3 * smoothstep(0.3, 0.8, order), seenBig));
                } }
                // (the lip's very edge, where it tears into drops: solid froth, its width uneven by the lace)
                foam = max(foam, smoothstep(0.35, 0.6, lipEdge * (0.55 + 0.9 * order)));
                foam = clamp(foam, 0.0, 1.0);
                // (thick foam is white as snow in the sun; thin foam lets the water show through its bubbles)
                float thick = smoothstep(0.3, 0.75, white);
                // Froth stands up from the water: a hand's breadth where it is fresh, a finger's where it has thinned to lace. Its
                // edges and the lips of its holes slope, and take the light or lose it: without that it is paint on the water. The
                // slope is found from how the froth's height changes from this pixel to its neighbours (and is let go where a pixel is
                // too far off to hold a string of the lace).
                // (Nor is dense froth one white. It lies in heaps and hollows: thick where it is piled, which is white as snow, and
                // thin between, where the water's colour comes through it and it is a blue-grey. The lace's own numbers say where.)
                float heaped = clamp(order * 0.7 + grain * 0.45 + (blotch - 0.5) * 0.4 - 0.1, 0.0, 1.0);
                float stands = foam * lerp(0.02, 0.09, thick) * (0.4 + 1.2 * heaped);
                // (and the roller, where the crest is breaking now: the mesh is raised by as much, where it is fine enough)
                stands += 0.3 * smoothstep(0.6, 0.98, white) * (1.0 - smoothstep(0.6, 1.6, IN.vs_TEXCOORD1.w));
                float2 change = float2(ddx(stands), ddy(stands));
                float turnOf = dx.x * dy.y - dx.y * dy.x;
                float2 leans = abs(turnOf) > 1e-9 ? float2(change.x * dy.y - dx.y * change.y, dx.x * change.y - change.x * dy.x) / turnOf : ((float2)(0.0));
                leans = clamp(leans, ((float2)(-1.2)), ((float2)(1.2))) * (1.0 - smoothstep(0.03, 0.2, across));
                float3 Nf = normalize(N - _OceanAxisA.xyz * leans.x - _OceanAxisB.xyz * leans.y);
                float3 foamLight = (_OceanSunTint.rgb * sunW * (0.25 + 0.75 * max(dot(Nf, L), 0.0)) * (1.0 - 0.8 * lipUnder) + skyAt(up, up) * (1.15 - 0.35 * length(leans)) * (1.0 - 0.5 * lipUnder)) * lerp(0.5, 0.68, thick);
                foamLight *= lerp(((float3)(1.0)), lerp(float3(0.66, 0.80, 0.88), ((float3)(1.04)), smoothstep(0.15, 0.8, heaped)), thick * (1.0 - smoothstep(0.05, 0.4, across)));
                colour = lerp(colour, foamLight, foam * lerp(0.8, 1.0, thick));

                // ---- the air between. Far land is pale with the air's own light (the game's, or Scatterer's, lays that on the ground
                // from how far off it is), and the sea beside it has to be as pale, or from a few kilometres up it is a dark slab let
                // into a hazy world. The air thins upwards (by e every 5.6 km on Kerbin), so a line of sight from high up meets less of
                // it for its length than one along the ground does: the thickness met is worked out for the line from the camera down
                // to the sea. Its light is the sky's just over the horizon that way, taken from last frame's picture where that
                // shows it. In a gale spray thickens the air near the water besides.
                float camHigh = max(dot(_WorldSpaceCameraPos - _OceanMid.xyz, up), 1.0);
                float thickness = far / 18000.0 * (5600.0 / camHigh) * (1.0 - exp(-camHigh / 5600.0)) + far / max(_OceanSkyLow.w, 1000.0) * exp(-camHigh / 250.0);
                float seen = exp(-thickness);
                float horizonThere;
                float3 ahead2 = -V - up * dot(-V, up);
                float3 airLight = lastSeen(normalize(normalize(ahead2 + up * 1e-5) + up * 0.07), 4.0, horizonThere);
                airLight = lerp(_OceanSkyLow.rgb, airLight, horizonThere);
                colour = lerp(airLight, colour, seen);

                // ---- from under the water. Looked at from below, the surface is two things. Straight up and for 48 degrees round is a
                // window: all the sky, squeezed into that circle by the bending of light as it enters the water, wobbling with the
                // waves, the sun in it. Outside the circle no light from above can reach the eye at all: there the surface is a mirror of
                // the water below, dark. Foam is seen from below as a shadow with a bright edge. And the water itself takes the light
                // over some tens of metres: far off, the surface is lost in the colour of the water, which is read from the picture
                // the game finished last frame (whoever made that colour: the game's own fog or another mod's).
                // (Which side of the surface is seen. With the lens's own pass on: whichever side of the mesh this pixel shows, which is
                // exact. Without it: from below if the eye is lower than the place, which is wrong for the near flank of a wave seen
                // from the air by a low eye. Seen from below with that pass on, how far off the surface is goes into the picture's
                // fourth number, one added and the sign turned (below nought: nothing else writes that, and a see-through thing drawn
                // over it afterwards, which adds a little, leaves it below nought; where the picture cannot keep such numbers it comes
                // out as nought): the pass lays the water between the glass and the surface by it, since the game's own picture of
                // how far off things are knows nothing of the sea. (First it was the distance and two: an engine's unlit plume, which
                // adds one to the fourth number of whatever is behind it, was then taken for the sea's underside at no distance.)
                // (Always by which side of the mesh the pixel shows, since 2026-10-11, 16:40. Until then, with the lens's pass off, it
                // was "is the eye lower than the place", and a far crest standing higher than a camera down in a trough was taken as
                // seen from below: dark once, and bright blue-green from the day the underside began to mirror the water's light.)
                // (And only with the camera at the water or under it. With the camera in the air, the underside that shows is that of a
                // crest's lip gone over, seen from outside: water seen from the air like any other, not the mirror and window that
                // the underside is to an eye in the water. Drawn as that, a breaking crest had bright blue teeth.)
                bool below = !isFront && _OceanShore.w > 0.5;
                if (below)
                {
                    float3 down = -N;
                    float came = max(dot(V, down), 0.0);
                    float3 out_ray = refract(-V, down, 1.333);
                    float through2 = dot(out_ray, out_ray) > 0.5 ? 1.0 : 0.0;
                    float window = through2 * smoothstep(0.661, 0.70, came);
                    float skyWard = max(dot(out_ray, N), 0.0);
                    float lost2 = 0.02 + 0.98 * pow(1.0 - skyWard, 5.0);
                    float3 above = skyAt(normalize(out_ray + N * 1e-4), up) * (1.0 - lost2);
                    float toSun = max(dot(out_ray, L), 0.0);
                    above += _OceanSunTint.rgb * sunW * (pow(toSun, 900.0) * 40.0 + pow(toSun, 24.0) * 0.7 + pow(toSun, 4.0) * 0.12) * (1.0 - lost2);
                    // (the water's colour: the foot of last frame's picture, much blurred. Under water the foot of the picture is water
                    // unless the eye looks straight down at a shallow bottom. Taken a set way down from level instead, as it first was, it
                    // fell outside the picture whenever the eye looked a little up, and the far surface ended in a hard line against
                    // water of another colour.)
                    float levelThere;
                    float3 ahead = normalize(_OceanMirrorW.xyz);
                    float3 levelWay = ahead - up * dot(ahead, up);
                    float3 levelSeen = lastSeen(normalize(normalize(levelWay + up * 1e-5) - up * 0.06), 4.0, levelThere);
                    float3 water = SampleLod(_OceanMirror, float2(0.5, 0.07), 5.0).rgb;
                    if (_OceanFoam.y > 0.5) water = pow(max(water, ((float3)(0.0))), ((float3)(2.2)));
                    // (better still than the picture's foot, which in shallow water is the sand: where the picture looked level through
                    // the water, a little under the far surface)
                    water = lerp(water, levelSeen, levelThere);
                    float3 own = pow(max(_UnderwaterFogColor.rgb, ((float3)(0.0))), ((float3)(_OceanFoam.y > 0.5 ? 2.2 : 1.0))) * (0.35 + 0.9 * max(dot(L, up), 0.0)) * sunW + skyAt(up, up) * 0.1;
                    water = lerp(own, water, _OceanMirrorX.w);
                    // (Outside the window the underside is a mirror, and what it mirrors is the water below: its own light, as the eye
                    // would see it looking the mirrored way. With the lens's own pass that is the very light that pass fogs with, by the
                    // same rule (darker downward, brightest level), so that the far underside and the water under it are one colour
                    // where they meet at eye level, as they are; before, the mirror was a flat dark slab with a hard lower edge.)
                    float3 mirrored = water * 0.55;
                    if (_OceanShore.z > 0.5)
                    {
                        float falls = dot(reflect(-V, down), up);
                        water = _UnderLight.rgb;
                        mirrored = _UnderLight.rgb * lerp(0.4, 1.2, smoothstep(-0.7, 0.45, falls)) + float3(0.012, 0.004, 0.0) * dot(_UnderLight.rgb, ((float3)(1.0)));
                    }
                    float3 from_below = lerp(mirrored, above, window);
                    // (foam: a shadow where it is thick, light scattered down through it where it thins)
                    from_below = lerp(from_below, water * 0.45 + foamLight * 0.25 * (1.0 - thick), foam * 0.8);
                    // (the water between the eye and the surface: laid here, unless the lens's own pass lays it over the whole picture)
                    colour = _OceanShore.z > 0.5 ? from_below : lerp(water, from_below, exp(-far / 45.0));
                }
                if (_OceanFoam.y > 0.5) colour = pow(max(colour, ((float3)(0.0))), ((float3)(1.0 / 2.2)));
                // ---- the weather in the air between the eye and the water: rain, snow, fog, dust (Natural Light's veil: its numbers,
                // and the same sums as its own pass, veil.glsl). That pass is laid over a camera's picture before the sea is drawn,
                // but for the far camera's of two, which comes after everything: so everywhere else the sea takes its veil here, by
                // how far off the water is. Without it the sea within 400 m stood clear and dark in rain and fog, and grey beyond
                // it, with a ruled line between. (Not from under the water: there is no air between.)
                { [loop] for (int once = _WeatherVeilAir.x > 0.0 && isFront && !(_OceanFarZ.w > 0.5 && _ProjectionParams.z > 0.5 * _OceanFarZ.z) ? 0 : 1; once < 1; once++)
                {
                    float mu = -dot(V, up);
                    float top = _WeatherVeilAir.y, soft = max(_WeatherVeilAir.z, 1.0), tau, inLayer = 0.0;
                    if (top > 0.0)
                    {
                        float toTop = mu > 0.00001 ? top / mu : 1.0e9;
                        tau = far <= toTop ? far : toTop + veilThinning(far - toTop, mu, soft);
                    }
                    else
                    {
                        float toTop = mu < -0.00001 ? top / mu : 1.0e9;
                        inLayer = max(far - toTop, 0.0);
                        tau = far <= toTop ? veilThinningAbove(top, far, mu, soft) : veilThinningAbove(top, toTop, mu, soft) + (far - toTop);
                    }
                    float veiled = 1.0 - exp(-_WeatherVeilAir.x * tau);
                    float g = clamp(_WeatherVeilSun.w, 0.0, 0.95);
                    float c = -dot(V, _WeatherVeilSun.xyz);
                    float lobe = (1.0 - g) * (1.0 - g) / pow(max(1.0 + g * g - 2.0 * g * c, 0.0001), 1.5) * (1.0 - g * g) / (1.0 + g);
                    // (from over the weather's top, the sunlight it throws back up, by as much of what is veiled as lies under
                    // its top: veil.glsl)
                    float lid = 3.0 * _WeatherVeilSunColour.a * inLayer / max(tau, 1e-3);
                    colour = colour * (1.0 - veiled) + (_WeatherVeilColour.rgb + _WeatherVeilSunColour.rgb * (lobe + lid)) * veiled;
                } }
                // (for finding faults, from the development build: one thing shown alone)
                if (_OceanShore.y > 0.5)
                {
                    float show = _OceanShore.y;
                    colour = show > 29.5 ? colour + float3(0.5 * lipMuch, 0.0, 0.0) : show < 1.5 ? ((float3)(sqrt(rough) * 2.0)) : show < 2.5 ? ((float3)(glint * 3.14)) : show < 3.5 ? float3(sparkle * 0.05, expectedShown, facets * 0.02) : show < 4.5 ? sky : show < 5.5 ? ((float3)(mirror)) : show < 6.5 ? float3(white, foam, under2) : show < 7.5 ? float3(bedShown, clamp(deep / 20.0, 0.0, 1.0), clamp(through / 40.0, 0.0, 1.0)) : show < 8.5 ? float3(lipMuch, lipAt > 0.0 ? clamp(lipAt / 8.0, 0.0, 1.0) : 0.0, lipAt < 0.0 ? clamp(-lipAt / 6.0, 0.0, 1.0) : 0.0) * (lipMuch > 0.001 ? 1.0 : 0.15) + float3(0.0, 0.0, lipMuch > 0.001 ? 0.0 : 0.1 * white) : IN.vs_TEXCOORD4.yzw * IN.vs_TEXCOORD4.x * 0.8 + ((float3)(0.06)) * (1.0 - IN.vs_TEXCOORD4.x) + ((float3)(0.2 * smoothstep(0.3, 0.75, white)));
                }
                result = float4(colour, _OceanShore.w > 0.5 && below ? -(far + 1.0) : 1.0);
                return result;
            }
            ENDCG
        }
    }
}
