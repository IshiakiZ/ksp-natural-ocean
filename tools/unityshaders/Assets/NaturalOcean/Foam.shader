// NaturalOcean/Foam: made from tools/shaderpack/oceanfoam.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalOcean/Foam"
{
    Properties
    {
        _OceanSlope ("OceanSlope", 2DArray) = "" {}
        _FoamPrev ("FoamPrev", 2D) = "" {}
        _OceanSeabed ("OceanSeabed", 2D) = "" {}
        _OceanSurfTime ("OceanSurfTime", 2D) = "" {}
        _OceanSurfDeep ("OceanSurfDeep", 2D) = "" {}
        _OceanLace ("OceanLace", 2D) = "" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One Zero
            ZWrite Off
            ZTest Always
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

            float4 _OceanLay;     // the patches of waves: x one over the largest one's side (1/m); y how many times smaller each next one is; z points across one; w unused here
            float4 _OceanTwin;     // the second laying of every patch: x its size against the first; y, z the cosine and sine of its turn; w what the two added are multiplied by
            float4 _OceanAt0;     // where the point of the sea under the camera is in each of the five largest patches (xy as first laid, zw the second time)
            float4 _OceanAt1;
            float4 _OceanAt2;
            float4 _OceanAt3;
            float4 _OceanAt4;
            float4 _FoamTexel;     // how far apart each map's points are, m
            float4 _FoamCellA;     // which point of the picture the camera's own place has in maps 0 (xy) and 1 (zw)
            float4 _FoamCellB;     // and in maps 2 and 3
            float4 _FoamMovedA;     // how many points the camera has moved on since each map was last worked out (maps 0 and 1); very many: begin the map afresh
            float4 _FoamMovedB;
            float4 _FoamSnapA;     // from the point of the sea under the camera to the corner of the camera's own point, m (maps 0 and 1)
            float4 _FoamSnapB;
            float4 _FoamAtA;     // where the point of the sea under the camera is in each map's picture, in pictures (maps 0 and 1)
            float4 _FoamAtB;
            float4 _FoamBreaks;     // the squeeze below which a breaker of each size of wave begins: the second patch to the fifth (-9: that size does not break)
            float4 _FoamCoarse;     // the squeeze below which as much of the sea lies as each of those sizes has breaking at any moment
            float4 _FoamFront;     // the running fronts: x how much less squeezed than where it begins a crest still carries a front; y how long a front runs, as a share of its wave's period; z this time's number from 0 to 1 (what spreads slower than a point a time spreads when the number says so); w gravity, m/s2
            float4 _FoamRows;     // the windrows: x how fast the surface water runs sideways into them at most, m/s (0: none); yz the way they lie (down the wind), in the sea's two axes; w how fast the last thin foam goes, against still air's 1
            float4 _FoamRowsAt;     // where the point of the sea under the camera is in the windrows' pattern, at its two sizes
            float4 _FoamPass;     // x: 0 the fronts are being worked out (the second picture), 1 the white (the first); y: how steeply down the wind the forward face slopes where the band of froth ends
            float4 _FoamTime;     // seconds since each map was last worked out; nought: it is left as it is this time
            float4 _FoamCount;     // y: how much of the longer patches' squeeze counts with a patch's own; z: over how much squeeze past its level a wave goes from not breaking to breaking outright
            float4 _FoamRagged;     // x: for finding faults: 1 to 4, the maps hold, in place of the white, the steep stretches of that patch's crests (the second patch is 1); 9, where fronts are running; y: how uneven the squeeze for breaking is from place to place (0: the lace is not made yet); zw: the way the crests lie (across the wind), in the sea's two axes
            float4 _FoamMode;     // x: 1 the maps have each become the next finer one (the camera has climbed), -1 the next coarser; y: how readily the crests whiten (0 none); z: points across the picture
            float4 _FoamHull0;     // what floats (Floating.cs): up to twelve places where a hull churns the water just now. xy: the place, m from the point of the sea under the camera; z: how wide, m; w: how white (0: none)
            float4 _FoamHull1;
            float4 _FoamHull2;
            float4 _FoamHull3;
            float4 _FoamHull4;
            float4 _FoamHull5;
            float4 _FoamHull6;
            float4 _FoamHull7;
            float4 _FoamHull8;
            float4 _FoamHull9;
            float4 _FoamHull10;
            float4 _FoamHull11;
            float4 _SeabedAtA;     // the maps of the ground under the sea (Seabed.cs), as the sea's own shader has them
            float4 _SeabedAtB;
            float4 _SeabedCell;
            float4 _OceanHighA;     // how high each of the four largest patches' waves are (the mean of the highest third, m)
            float4 _OceanHighB;     // x: the fifth's
            float4 _OceanSurfA;     // the surf's two trains (the swell's and the wind sea's): x, z how long between crests (s); y, w how high out at sea (m)
            float4 _OceanSurfB;     // x, z: how far each train's count has got (the part of a period); y, w: how many whole periods have gone (wrapped)
            float4 _OceanAtSpot;     // xy: where the point of the sea under the camera is, m, wrapped every 1024 m (for patterns pinned to the water)
            Texture2DArray _OceanSlope; SamplerState sampler_OceanSlope;
            Texture2D _OceanSeabed; SamplerState sampler_OceanSeabed;
            Texture2D _OceanSurfTime; SamplerState sampler_OceanSurfTime;     // how long a wave takes from each point to the shore, s: four maps, laid out as the seabed's
            Texture2D _OceanSurfDeep; SamplerState sampler_OceanSurfDeep;     // how deep the water is at each point as a wave feels it (taken over some metres round), m: laid out the same
            Texture2D _FoamPrev; SamplerState sampler_FoamPrev;     // the picture as last worked out
            Texture2D _FoamFronts; SamplerState sampler_FoamFronts;     // the second picture, the running fronts: as last worked out while it is being worked out, as just worked out while the white is
            Texture2D _OceanLace; SamplerState sampler_OceanLace;     // the lace (Foam.cs); its fourth number, slow blotches, makes the edges of the white ragged

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                OUT.pos = float4(in_POSITION0.xy, 0.5, 1.0);
                return OUT;
            }

            // Natural Ocean: the white water, kept from one moment to the next (Foam.cs). A picture of four maps of the sea round
            // the camera, one in each of its four numbers, each map's points four times as far apart as the last one's (an eighth of a
            // metre, half a metre, two metres, eight, when the camera is low). Each point of a map is a place ON THE WATER, not in
            // space: the water under a crest swings to and fro as the crest passes and its foam goes with it, so the white a breaker
            // leaves stays where it was left while the crest runs on. A place keeps its point of the picture for as long as it is in
            // reach of the camera (the picture wraps round: what comes into reach on one side takes the points of what left on the other).
            //
            // Every time it is worked out: where a breaker's front is just now (see "the running fronts" below, and Waves.cs for which
            // waves break) the point goes white at once; everywhere else the white there fades: to a half within a second (the froth
            // falling in), then slowly, over a dozen seconds (the skin of bubbles it leaves). So a whitecap is a bright curved front
            // that runs with its crest for a second or two, and the patch it has swept, paling behind it. Every map reads the same
            // waves at its own points; a map too coarse to hold the fronts of a size of wave has that size's white as specks, as many
            // as that size has breaking.
            //
            // It is worked out twice over each time: first the fronts (a second picture of the same four maps, kept the same way),
            // then the white, which reads the fronts as just worked out. `_FoamPass` says which.

            // <surf> (this stretch is word for word the same in ocean.glsl and oceanfoam.glsl: glcheck_all.py sees that it stays so)
            // The ground's height against the sea's level at a place (m; below nought: under so much water), from the finest of the four
            // maps of it that reaches so far (Seabed.cs); deep water where none does.
            float groundAt(float2 spot)
            {
                float out_ = max(abs(spot.x), abs(spot.y));
                float ground = -1000.0;
                [loop] for (int l = 3; l >= 0; l--)
                {
                    float span = 128.0 * Pick(_SeabedCell, l);
                    float inside = 1.0 - smoothstep(0.38, 0.46, out_ / span);
                    if (inside > 0.0)
                    {
                        float2 at = l == 0 ? _SeabedAtA.xy : l == 1 ? _SeabedAtA.zw : l == 2 ? _SeabedAtB.xy : _SeabedAtB.zw;
                        ground = lerp(ground, SampleLod(_OceanSeabed, at + spot / span, 0.0)[l], inside);
                    }
                }
                return ground;
            }

            // How long a wave takes from a place to the shore (s), from the same maps' second picture; 5000 where no map knows of a shore.
            float shoreTimeAt(float2 spot)
            {
                float out_ = max(abs(spot.x), abs(spot.y));
                float time = 5000.0;
                [loop] for (int l = 3; l >= 0; l--)
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
                }
                return time;
            }

            // How deep the water is at a place as a wave feels it (m): taken over some metres round, land counting as no depth. The
            // game's seabed is all bumps a few metres across, which a wave ten times as long passes over without noticing.
            float feltDepthAt(float2 spot)
            {
                float out_ = max(abs(spot.x), abs(spot.y));
                float deep = 1000.0;
                [loop] for (int l = 3; l >= 0; l--)
                {
                    float span = 128.0 * Pick(_SeabedCell, l);
                    float inside = 1.0 - smoothstep(0.38, 0.46, out_ / span);
                    if (inside > 0.0)
                    {
                        float2 at = l == 0 ? _SeabedAtA.xy : l == 1 ? _SeabedAtA.zw : l == 2 ? _SeabedAtB.xy : _SeabedAtB.zw;
                        deep = lerp(deep, SampleLod(_OceanSurfDeep, at + spot / span, 0.0)[l], inside);
                    }
                }
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
                [loop] for (int j = 0; j < 2; j++)
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
                }
                return float4(lift, front, thrown, share);
            }
            // </surf>

            float4 shuffled(float4 p)
            {
                return _FoamMode.x > 0.5 ? float4(p.gba, 0.0) : _FoamMode.x < -0.5 ? float4(0.0, p.rgb) : p;
            }

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float2 point_ = floor(IN.pos.xy);
                float4 was = shuffled(TexelFetch(_FoamPrev, ((int2)(point_)), 0));
                float4 lit_ = shuffled(TexelFetch(_FoamFronts, ((int2)(point_)), 0));
                float4 ats[5] = {_OceanAt0, _OceanAt1, _OceanAt2, _OceanAt3, _OceanAt4};
                float across = _FoamMode.z, half_ = 0.5 * across;
                bool fronts = _FoamPass.x < 0.5;
                float4 result_ = fronts ? lit_ : was;
                float2 downwind = float2(_FoamRagged.w, -_FoamRagged.z);
                [loop] for (int l = 0; l < 4; l++)
                {
                    float seconds = Pick(_FoamTime, l);
                    [loop] for (int once = seconds > 0.0 ? 0 : 1; once < 1; once++)
                    {
                        float texel = Pick(_FoamTexel, l);
                        float2 cell = l == 0 ? _FoamCellA.xy : l == 1 ? _FoamCellA.zw : l == 2 ? _FoamCellB.xy : _FoamCellB.zw;
                        float2 moved = l == 0 ? _FoamMovedA.xy : l == 1 ? _FoamMovedA.zw : l == 2 ? _FoamMovedB.xy : _FoamMovedB.zw;
                        float2 snap = l == 0 ? _FoamSnapA.xy : l == 1 ? _FoamSnapA.zw : l == 2 ? _FoamSnapB.xy : _FoamSnapB.zw;
                        // (how many points from the camera's own this one is: the picture wraps, so the nearest way round)
                        float2 from = Mod(point_ - cell + half_, across) - half_;
                        float2 rel = (from + 0.5) * texel + snap;
                        // (a point that has only now come into reach held some place a whole picture away)
                        float2 before = from + moved;
                        bool fresh = before.x < -half_ || before.x >= half_ || before.y < -half_ || before.y >= half_;

                        // ---- the waves here: how squeezed each size of wave has the water (its patch and the patch's second laying), with
                        // half of the longer patches' squeeze counted in (short waves break on the crests of long ones), and how steeply
                        // its water faces down the wind (the forward face of a crest). The patches as they are, not smoothed for the map's
                        // coarseness: smoothing takes the extremes off, and the levels were found from the patches as they are.
                        float2 turned = float2(_OceanTwin.y * rel.x - _OceanTwin.z * rel.y, _OceanTwin.z * rel.x + _OceanTwin.y * rel.y) / _OceanTwin.x;
                        float one = _OceanLay.x;
                        // (In the shallows the waves are only as high as the depth allows (see the sea's shader), and what they lose they lose
                        // by breaking: the more of them the bottom has taken, the less of a squeeze it needs for the water to go white.)
                        float deep = max(-groundAt(rel), 0.0);
                        float taken = 0.0;
                        [loop] for (int n = 0; n < 5; n++)
                        {
                            float high = n < 4 ? Pick(_OceanHighA, n) : _OceanHighB.x;
                            taken = max(taken, (1.0 - clamp(0.45 * deep / max(high, 1e-3), 0.0, 1.0)) * smoothstep(0.15, 0.6, high));
                        }
                        float wet = step(0.02, deep);
                        // (and not evenly: the picture of blotches, pinned to the water at two sizes, lets it break a little sooner here and
                        // later there, so that a patch of white has a torn edge and not a smooth one)
                        float2 here = (point_ + 0.5) / across;
                        float uneven = SampleLod(_OceanLace, here * 3.0, 0.0).a + SampleLod(_OceanLace, here * 11.0 + 0.37, 0.0).a - 1.0;
                        float sooner = _FoamRagged.y * uneven + 0.5 * taken * wet * smoothstep(0.0, 0.5, deep);
                        float squeezed[5], faces[5], followed[5], speed[5];
                        float longer = 0.0;
                        [loop] for (int n = 0; n < 5; n++)
                        {
                            float high = n < 4 ? Pick(_OceanHighA, n) : _OceanHighB.x;
                            float kept = clamp(0.45 * deep / max(high, 1e-3), 0.0, 1.0);
                            float4 first = SampleLod(_OceanSlope, float3(ats[n].xy + rel * one, float(n)), 0.0);
                            float4 second = SampleLod(_OceanSlope, float3(ats[n].zw + turned * one, float(n)), 0.0);
                            float own = kept * _OceanTwin.w * (first.b + second.b);
                            // (the second laying's slope turned back to the sea's own axes)
                            float2 slope = first.rg + float2(_OceanTwin.y * second.r + _OceanTwin.z * second.g, _OceanTwin.y * second.g - _OceanTwin.z * second.r);
                            squeezed[n] = own + _FoamCount.y * longer - sooner;
                            faces[n] = -dot(slope, downwind) * kept * _OceanTwin.w;
                            float side = 1.0 / one;
                            // (a map follows the fronts of a size of wave if its points are fine enough to hold one: a sixth of the
                            // middle length of that size, which is its patch's side over 5.6)
                            followed[n] = step(texel, side / 34.0);
                            // (how fast that size of wave runs, m/s: the middle length again; 5.6 * 2 pi = 35.2)
                            speed[n] = sqrt(_FoamFront.w * side / 35.2);
                            longer += own;
                            one *= _OceanLay.y;
                        }

                        if (fronts)
                        {
                            // ---- the running fronts (the second picture). A breaker begins where a crest is squeezed past its size's level:
                            // a rare thing, a spot. From there it runs: every point of that crest's steep stretch (squeezed to within
                            // `_FoamFront.x` of the level) that lies beside a point where the front already is, joins it; down the wind at
                            // once (the crest carries the front with it), along the crest and back at half the wave's speed (a breaker
                            // spreads along its crest). It runs for a set share of the wave's period and then is over, wherever it has
                            // got to. Each point holds: which size of wave (4 the longest that breaks, to 1) and, after the point, how
                            // much of the front's time is left.
                            float e = 0.0;
                            [loop] for (int t = 0; t < 12; t++)
                            {
                                // (the eight round and the point itself; then two, three and four points up the wind, for the fast waves)
                                float2 o = t < 9 ? float2(float(t - 3 * (t / 3)) - 1.0, float(t / 3) - 1.0) : -downwind * float(t - 7);
                                float2 q = Mod(floor(point_ + o + 0.5), across);
                                float v = shuffled(TexelFetch(_FoamFronts, ((int2)(q)), 0))[l];
                                int n = clamp(5 - int(v), 1, 4);
                                float level = Pick(_FoamBreaks, n - 1);
                                float upwind = step(dot(o, downwind), -0.3) + (t == 4 ? 1.0 : 0.0);
                                float spreads = step(_FoamFront.z, 0.5 * speed[n] * seconds / texel);
                                if (v >= 1.0 && followed[n] > 0.5 && max(upwind, spreads) > 0.5 && squeezed[n] < level + _FoamFront.x) e = max(e, v);
                            }
                            if (e >= 1.0)
                            {
                                int n = clamp(5 - int(e), 1, 4);
                                // (the wave's period is 2 pi times its speed over gravity)
                                float lasts = _FoamFront.y * 6.2832 * speed[n] / _FoamFront.w;
                                float left = frac(e) - seconds / max(lasts, 0.05);
                                e = left > 0.0 ? floor(e) + left : 0.0;
                            }
                            if (fresh) e = 0.0;
                            [loop] for (int n = 1; n < 5; n++)
                                if (followed[n] > 0.5 && squeezed[n] < Pick(_FoamBreaks, n - 1)) e = max(e, float(5 - n) + 0.98);
                            result_ = Put(result_, l, e);
                        }
                        else
                        {
                            // ---- the white (the first picture)
                            float white = Pick(was, l);
                            // (a point new in reach begins with what the next coarser map has here, the coarsest with nothing)
                            if (fresh)
                            {
                                white = 0.0;
                                if (l < 3)
                                {
                                    float2 at = l == 0 ? _FoamAtA.zw : l == 1 ? _FoamAtB.xy : _FoamAtB.zw;
                                    white = shuffled(SampleLod(_FoamPrev, at + rel / (across * Pick(_FoamTexel, l + 1)), 0.0))[l + 1];
                                }
                            }
                            float e = Pick(lit_, l), breaking = 0.0, shown = 0.0;
                            [loop] for (int n = 1; n < 5; n++)
                            {
                                float level = Pick(_FoamBreaks, n - 1);
                                // (where a front of this size of wave is running: white on the forward face of the crest's steep stretch;
                                // and where it begins)
                                float steep = clamp((level + _FoamFront.x - squeezed[n]) / _FoamCount.z, 0.0, 1.0);
                                // (The froth of a breaker is a band at the crest and a little way down its forward face, as deep as a
                                // thirtieth of the wave's length: where the water slopes down the wind by up to a set slope
                                // (`_FoamPass.y`), which is that share of any wave near breaking, long or short.)
                                float band = smoothstep(-0.006, 0.01, faces[n]) * (1.0 - smoothstep(_FoamPass.y, _FoamPass.y * 1.6, faces[n]));
                                float running = e >= 1.0 && clamp(5 - int(e), 1, 4) == n ? steep * band : 0.0;
                                running = max(running, clamp((level - squeezed[n]) / _FoamCount.z, 0.0, 1.0));
                                // (a size of wave too small for this map's points to follow its fronts: white where it is squeezed past a
                                // lesser level, below which as much of the sea lies as that size has breaking at any moment)
                                float plain = clamp((Pick(_FoamCoarse, n - 1) - squeezed[n]) / _FoamCount.z, 0.0, 1.0);
                                breaking = max(breaking, lerp(plain, running, followed[n]));
                                // (for finding faults: one size of wave's steep stretches alone)
                                if (int(_FoamRagged.x + 0.5) == n) shown = steep;
                            }
                            if (_FoamRagged.x > 8.5) shown = e >= 1.0 ? 0.25 + 0.75 * frac(e) : 0.0;
                            breaking *= max(_FoamMode.y, taken * wet);
                            // (The white at the very edge of the water is not made here: the sea's own shader lays it where the water as DRAWN is
                            // thin. Made here from the depth the game's terrain gives when asked, it lay in a square on water a metre deep
                            // wherever the asked ground and the drawn ground differed.)
                            // (the surf: its breaking fronts and bores, and the edge of the sheet it sends up the sand)
                            [loop] for (int once2 = deep < 9.0 * max(_OceanSurfA.y, _OceanSurfA.w) + 1.0 ? 0 : 1; once2 < 1; once2++)
                            {
                                float4 surf = surfAt(rel, -deep, feltDepthAt(rel), shoreTimeAt(rel));
                                breaking = max(breaking, surf.y);
                            }
                            // ---- Windrows. A wind over water sets the top few metres of it turning in long rolls laid along the wind
                            // (Langmuir's), side by side and turning opposite ways, so that the surface water runs together along lines
                            // down the wind and apart between them; and what floats is gathered into those lines. That is what the streaks
                            // of foam in a gale are: old foam, carried sideways a hand's breadth a second into rows. So the white here is
                            // what lay a little to one side a moment ago, and more of it where the water is running together. The rolls
                            // are a slow pattern pinned to the water, far longer along the wind than across it, at two sizes (rows eight
                            // metres apart and thirty-two); a map whose points are too far apart to hold a row leaves that size out.
                            [loop] for (int once3 = !fresh && _FoamRows.x > 0.0 ? 0 : 1; once3 < 1; once3++)
                            {
                                float2 along = _FoamRows.yz, athwart = float2(-_FoamRows.z, _FoamRows.y);
                                float2 carried = ((float2)(0.0));
                                float gathers = 0.0;
                                // (The rows wander: the place across the wind is pushed about by up to half a row's width, by the same
                                // pattern read slowly. Left straight, the rows stood exactly evenly apart, as the pattern's own points
                                // do, and a gale's sea was corduroy.)
                                float2 wander = SampleLod(_OceanLace, _FoamRowsAt.zw * 1.7 + float2(dot(rel, along) / 290.0, dot(rel, athwart) / 61.0) + 0.11, 0.0).ab - 0.5;
                                [loop] for (int k = 0; k < 2; k++)
                                {
                                    float apart = k == 0 ? 8.0 : 32.0;
                                    float held = step(texel, apart / 8.0);
                                    float2 uv = (k == 0 ? _FoamRowsAt.xy : _FoamRowsAt.zw) + float2(dot(rel, along) / (k == 0 ? 146.2857 : 512.0), (dot(rel, athwart) + (k == 0 ? wander.x : wander.y) * apart * 1.4) / (1.6 * apart));
                                    float lo = SampleLod(_OceanLace, uv - float2(0.0, 0.05), 1.5).a, mid = SampleLod(_OceanLace, uv, 1.5).a, hi = SampleLod(_OceanLace, uv + float2(0.0, 0.05), 1.5).a;
                                    // (the water runs down the pattern's slope across the wind, into its valleys; where it runs together,
                                    // there is the more foam, by as much a second as the water's speed over a quarter of the rows' distance)
                                    carried -= athwart * (_FoamRows.x * held * clamp((hi - lo) * 5.0, -1.0, 1.0));
                                    gathers += _FoamRows.x * held * clamp((hi - 2.0 * mid + lo) * 31.25, -1.0, 1.0) / (0.25 * apart);
                                }
                                white = shuffled(SampleLod(_FoamPrev, (point_ + 0.5 - carried * seconds / texel) / across, 0.0))[l];
                                white = clamp(white * (1.0 + clamp(gathers, -0.2, 0.2) * seconds), 0.0, 1.0);
                            }
                            // (Foam spreads a little as it lies: the edge of a patch is sharp where it has just been laid and soft where
                            // it is old. By about a fifth of a metre in a second, whatever the map's fineness.)
                            float spread = fresh ? 0.0 : clamp(0.03 * seconds / (0.5 * texel * texel), 0.0, 0.25);
                            white = lerp(white, shuffled(SampleLod(_FoamPrev, (point_ + 0.5) / across, 1.0))[l], spread);
                            // (What a front leaves behind it fades in three stages, as it does at sea. For half a second it is dense froth,
                            // the bright part of a whitecap (1 to 0.7 here); then the froth falls in and opens into lace over some five
                            // seconds (to 0.25); what is left is a thin skin of bubbles drawn into strings, gone in eight more. Photographs
                            // of the sea have a fifth of a hundredth of it bright at 15 m/s and three or four hundredths white of any kind:
                            // the bright stage is a fifteenth of the whole. At a second and a half, as it first was here, a whitecap was
                            // a sheet of snow twenty metres deep.)
                            // (In a gale the last of it lasts longer: there is more of what holds a bubble's skin together in water that
                            // has been breaking for hours, and it is that long-lived foam the wind draws into streaks.)
                            // (And not evenly: by the same blotches that make a patch's edge torn, the froth falls in sooner here and later
                            // there, so that the back of the bright part is ragged and holes open in it, where it was cut straight.)
                            // ---- What floats. Where a hull churns the water (it moves through the water, or the water past it, or it has
                            // come down into it) the water goes white as under a breaker, whatever the wind: a round patch as wide as
                            // the part, its edge torn by the same blotches, and in a map whose points are wider than the patch only
                            // as white as the patch's share of a point.
                            float4 hulls[12] = {_FoamHull0, _FoamHull1, _FoamHull2, _FoamHull3, _FoamHull4, _FoamHull5, _FoamHull6, _FoamHull7, _FoamHull8, _FoamHull9, _FoamHull10, _FoamHull11};
                            [loop] for (int h = 0; h < 12; h++)
                            {
                                float4 hull = hulls[h];
                                [loop] for (int once4 = hull.w > 0.0 ? 0 : 1; once4 < 1; once4++)
                                {
                                    float wide = max(hull.z, 0.75 * texel);
                                    float off = length(rel - hull.xy) / wide;
                                    float share = min(hull.z / wide, 1.0);
                                    breaking = max(breaking, hull.w * share * share * (1.0 - smoothstep(0.75, 1.1, off * (1.0 + 0.35 * uneven))));
                                }
                            }
                            white = max(white - seconds * (0.028 * _FoamRows.w + 0.10 * smoothstep(0.2, 0.45, white) + 0.5 * smoothstep(0.68, 0.8, white)) * (1.0 + 0.7 * uneven), breaking);
                            result_ = Put(result_, l, _FoamRagged.x > 0.5 ? shown : white);
                        }
                    }
                }
                result = result_;
                return result;
            }
            ENDCG
        }
    }
}
