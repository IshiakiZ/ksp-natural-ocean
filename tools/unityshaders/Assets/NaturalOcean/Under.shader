// NaturalOcean/Under: made from tools/shaderpack/oceanunder.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalOcean/Under"
{
    Properties
    {
        _OceanSlope ("OceanSlope", 2DArray) = "" {}
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

            float4 _UnderLens;     // x, y: the tangents of half the view, across and up; z: how far in front of the camera its glass is (the near plane), m; w: the clock, s
            float4 _UnderPlane;     // the water's surface at the camera taken as flat, as the camera has it (x to its right, y up the picture, z ahead): xyz its upward normal; w: how high the camera's middle stands above it, m (below nought: under). Gone by only where the map has nothing.
            float4 _UnderUp;     // xyz: straight up, the same way; w: 1 if there is a map of the water's height at the glass this frame
            float4 _UnderMapA;     // xyz: the sea's first level axis, the same way, divided by half the map's side (m); w: half the map's side
            float4 _UnderMapB;     // xyz: its second, likewise
            float4 _UnderLight;     // rgb: the water's own light where the camera is, as light; w: 1 if the game keeps its picture as numbers for the screen
            float4 _UnderSky;     // rgb: the sky's light low down, as light (the glint at the lip of the water on the glass); w: (development build) one thing shown alone
            float4 _UnderTakes;     // rgb: how much of red, green and blue a metre of this water takes (the light left after a metre is e to the minus that); w: how far up the glass the water climbs at its edge, m
            float4 _OceanLay;     // the patches of waves (Waves.cs): x one over the largest one's side (1/m); y how many times smaller each next one is; z points across one
            float4 _OceanTwin;     // the second laying of every patch: x its size against the first; y, z the cosine and sine of its turn; w what the two added are multiplied by
            float4 _OceanAt3;     // where the point of the sea under the camera is in patches 3 to 6 (waves of seven metres, two metres, sixty centimetres and a hand), in the patch's own sides: xy as first laid, zw as laid the second time
            float4 _OceanAt4;
            float4 _OceanAt5;
            float4 _OceanAt6;
            float4 _UnderSun;     // xy: how far towards the sun, along the sea's two axes, the sun's light came in at the surface for every metre it has gone down (its way in water); z: how much of the sun there is (0: night, overcast)
            float4 _UnderSunWay;     // xyz: the way to the sun as its light goes in water, as the camera has it (x to its right, y up the picture, z ahead)
            float4 _UnderEye;     // xy: where the camera is on the sea's two axes, m from the point of the sea under it as the mesh was laid
            Texture2DArray _OceanSlope; SamplerState sampler_OceanSlope;     // each patch of waves: rg the surface's slope; b: how squeezed the water is (below nought at the crests)
            Texture2D _UnderScene; SamplerState sampler_UnderScene;     // the picture as drawn
            Texture2D _UnderMap; SamplerState sampler_UnderMap;     // r: how high the sea's surface, as drawn, stands above the middle of the glass (m), from straight above; a: 1 where the sea's mesh is
            Texture2D _CameraDepthTexture; SamplerState sampler_CameraDepthTexture;     // how far off each thing in the picture is (the sea itself is not in it)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 vs_TEXCOORD0 : TEXCOORD0;
            };

            // Natural Ocean: the lens in the water (Sea.cs, Lens). One triangle over the whole picture.

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                OUT.pos = float4(in_POSITION0.xy, 0.5, 1.0);
                // (drawn into whatever the camera is drawing into, which may lie either way: as for the lens's second go, above; Natural
                // Light's veil and the sea's lens in the water the same)
                float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
                way = _ProjectionParams.x;
#endif
                OUT.vs_TEXCOORD0 = float2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);
                return OUT;
            }

            // A camera held at the water's surface has the surface across its glass: the upper part of the glass is in air and the lower
            // in water, and the picture is two pictures with a line between them. Above the line it is the world as ever. Below it the
            // camera looks through water from the glass on: what is near is clear and what is far is lost in the water's own light, red
            // first (water takes the red, then the green; a white thing ten metres off is blue-green). The line is the water's edge on
            // the glass, and it is exactly where the sea, as it is drawn, cuts the glass: a clean line, nearly straight when the water
            // is quiet (a glass is a hand across, and the waves that reach it are longer than that), tilting and rising and falling
            // with the waves and with nothing else. The water climbs the glass a couple of millimetres there, as it does up any wet
            // wall, and that little slope of water is the dark thread a real camera shows along the line: seen through it the light
            // is thrown back down into the water, and at its upper lip there is a glint of the sky.
            //
            // (As a camera behind a domed glass sees it: things under water are where they are and as large as they are, so a thing
            // that stands through the surface meets itself at the line. A flat glass would show them a third larger; that was tried
            // and taken out again: the picture under the line swam whenever the line moved.)
            //
            // This is laid over the finished picture (before the lens's own effects), only while the glass is at the water or under
            // it. Where the water cuts the glass is read from a small map of the sea's height round the glass, which the sea's own
            // shader draws once more a frame for this (the same mesh, seen from straight above: ocean.glsl). The waves' numbers at
            // the camera, which is what this went by at first, are a finger's breadth off the mesh as drawn, and a finger's breadth
            // on the glass is a twentieth of the picture.
            // The game's own fog under water is kept off while this is on: that one fogs the whole picture or none of it, by whether the
            // middle of the camera is under.

            // How far under the water's surface a place on the glass is (m, straight down; below nought: so far above it). `at`: the
            // place in the picture, -1 to 1 each way.
            float under(float2 at)
            {
                // (from the middle of the glass, as the camera has it)
                float3 off = _UnderLens.z * float3(at.x * _UnderLens.x, at.y * _UnderLens.y, 0.0);
                float flat_ = -(dot(off, _UnderPlane.xyz) + _UnderLens.z * _UnderPlane.z + _UnderPlane.w);
                float4 map = SampleLod(_UnderMap, float2(dot(off, _UnderMapA.xyz), dot(off, _UnderMapB.xyz)) * 0.5 + 0.5, 0.0);
                return _UnderUp.w > 0.5 && map.a > 0.999 ? map.r - dot(off, _UnderUp.xyz) : flat_;
            }

            float3 asLight(float3 c) { return _UnderLight.w > 0.5 ? pow(max(c, ((float3)(0.0))), ((float3)(2.2))) : c; }

            // How much of the sun's light the waves gather at a place under water (1: as much as under a flat sea; more under a crest,
            // less under a trough). The surface is a sheet of lenses: light going down through sloping water is bent a quarter of the
            // slope towards the uphill side, so under a crest, where the slopes face each other, it closes in, and at some depth meets
            // (the bright lines that dance on a sandy bottom, and the shafts in the water above them); under a trough it spreads. How
            // fast it closes is how fast the slope changes across the crest, and that is read from the waves themselves: each patch
            // carries how squeezed its water is, which is the same thing but for the waves' own length. A short wave's light meets a
            // little way down and below that is spread and mixed with its neighbours' (nothing left of it); a long wave's meets deep.
            // So each size of wave counts most about the depth where its light meets, and the pattern grows coarser with depth, as it
            // does. `spot`: the place on the sea's two axes, m from the point of the sea under the camera; `down`: m under the surface;
            // `over`: the stretch (m) the answer is to stand for, the waves read smoothed over so much (a place on a thing: nought).
            float gathered(float2 spot, float down)
            {
                float2 came = spot + _UnderSun.xy * down;
                float2 turned = float2(_OceanTwin.y * came.x - _OceanTwin.z * came.y, _OceanTwin.z * came.x + _OceanTwin.y * came.y) / _OceanTwin.x;
                float one = _OceanLay.x * _OceanLay.y * _OceanLay.y * _OceanLay.y * _OceanLay.y;
                float closes = 0.0;
                { [loop] for (int n = 4; n < 7; n++)
                {
                    float4 at = n == 4 ? _OceanAt4 : n == 5 ? _OceanAt5 : _OceanAt6;
                    // (a patch's waves are some 5.6 to its side; `past`: 1 at the depth where waves of a usual steepness for it meet their light)
                    float k = 35.2 * one;
                    float past = 0.25 * down * k * 0.15;
                    float blurred = log2(1.0 + 0.02 * down * one * _OceanLay.z);
                    float first = SampleLod(_OceanSlope, float3(at.xy + came * one, float(n)), blurred).b;
                    float second = SampleLod(_OceanSlope, float3(at.zw + turned * one, float(n)), blurred).b;
                    closes += 0.25 * down * k * (first + second / _OceanTwin.x) / (1.0 + past * past);
                    one *= _OceanLay.y;
                } }
                // (half as much again as read: the squeeze is read smoothed between a patch's points, which is less than the water's own
                // where it is most squeezed, and it is there that the light meets)
                return 1.0 / max(abs(1.0 + 1.5 * closes * _OceanTwin.w), 0.3);
            }

            // The same for the water itself, where the gathered light shows as shafts: from the waves of two metres and seven only.
            // What is seen in the water at a pixel is all the light along the way through it, and along a way of some metres the
            // short waves' light and dark come many times over and leave nothing; the long waves' beams are broad enough to last.
            // (With the short waves in, and the way looked at in eight places, the water was a curtain of fine streaks, like rain.)
            float beams(float2 spot, float down)
            {
                float2 came = spot + _UnderSun.xy * down;
                float2 turned = float2(_OceanTwin.y * came.x - _OceanTwin.z * came.y, _OceanTwin.z * came.x + _OceanTwin.y * came.y) / _OceanTwin.x;
                float one = _OceanLay.x * _OceanLay.y * _OceanLay.y * _OceanLay.y;
                float closes = 0.0;
                { [loop] for (int n = 3; n < 5; n++)
                {
                    float4 at = n == 3 ? _OceanAt3 : _OceanAt4;
                    float k = 35.2 * one;
                    float first = SampleLod(_OceanSlope, float3(at.xy + came * one, float(n)), 1.0).b;
                    float second = SampleLod(_OceanSlope, float3(at.zw + turned * one, float(n)), 1.0).b;
                    closes += 0.25 * down * k * (first + second / _OceanTwin.x);
                    one *= _OceanLay.y;
                } }
                return 1.0 / max(abs(1.0 + 1.5 * closes * _OceanTwin.w), 0.3);
            }

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float2 at = IN.vs_TEXCOORD0 * 2.0 - 1.0;
                float deep = under(at);
                // (how many pixels under the line this is, by how fast the depth changes from one pixel to the next; and how far
                // that is along the glass itself, m)
                float per = max(length(float2(ddx(deep), ddy(deep))), 1e-7);
                float pixels = deep / per;
                float along = pixels * _UnderLens.z * _UnderLens.y * abs(ddy(at.y));
                float wet = clamp(0.5 + pixels / 1.5, 0.0, 1.0);
                float4 scene = SampleFlat(_UnderScene, IN.vs_TEXCOORD0);
                float3 colour = asLight(scene.rgb);
                float shown = 0.0;
                // (where the thing at this pixel is, as the camera has it, and which way its surface faces: from how that changes from
                // one pixel to the next, which has to be asked here, before anything is left out)
                float3 way = float3(at.x * _UnderLens.x, at.y * _UnderLens.y, 1.0);
                float long_ = length(way);
                float3 toward = way / long_;
                float eyeDepth = 1.0 / (_ZBufferParams.z * SampleFlat(_CameraDepthTexture, IN.vs_TEXCOORD0).r + _ZBufferParams.w);
                float3 place = way * eyeDepth;
                float3 faces = -normalize(cross(ddx(place), ddy(place)));
                if (dot(faces, place) > 0.0) faces = -faces;
                { [loop] for (int once = wet > 0.0 ? 0 : 1; once < 1; once++)
                {
                    // ---- what the wet glass sees: how much water lies between the glass and the thing
                    float water = (eyeDepth - _UnderLens.z) * long_;
                    // (to the thing itself; or to the sea's surface seen from below, which says how far off it is in the picture's
                    // fourth number, below nought; or, where the picture cannot keep that, to the surface taken as flat if the way goes up)
                    float rises = dot(toward, _UnderUp.xyz);
                    if (scene.a < -0.5) water = -scene.a - 1.0 - _UnderLens.z * long_;
                    else if (rises > 1e-3) water = min(water, max(deep, 0.002) / rises);
                    float3 left = exp(-max(water, 0.0) * _UnderTakes.rgb);
                    shown = max(water, 0.0);
                    // (the water's own light is not the same every way: looking down it is the dark of the deep, looking along it is
                    // at its brightest and bluest, looking up it is lit from the surface)
                    float3 light = _UnderLight.rgb * lerp(0.4, 1.2, smoothstep(-0.7, 0.45, rises)) + float3(0.012, 0.004, 0.0) * dot(_UnderLight.rgb, ((float3)(1.0)));
                    // (And the sun's light is taken by the water over the thing, too: what lies a few metres down is lit blue-green and
                    // dimmer. The scene is drawn as from the air, with the sun as it is above; so the thing's own light is cut here by
                    // how far under the surface it is, the surface taken as level from the glass on. Not the sea's underside, which is
                    // at the surface, nor the weed, which does this for itself.)
                    float thingDown = max(deep - rises * max(water, 0.0), 0.0);
                    float3 lit_ = scene.a < -0.5 ? ((float3)(1.0)) : exp(-thingDown * _UnderTakes.rgb * float3(0.9, 0.6, 0.6));
                    // ---- the sun's light gathered by the waves: on the thing itself (the lines of light that run over a hull or a sandy
                    // bottom), fainter the deeper it lies, and in the water between (shafts)
                    float3 axisA = _UnderMapA.xyz * _UnderMapA.w, axisB = _UnderMapB.xyz * _UnderMapA.w;
                    float2 run = float2(dot(toward, axisA), dot(toward, axisB));
                    float2 spot0 = _UnderEye.xy + run * (_UnderLens.z * long_);
                    { [loop] for (int once = _UnderSun.z > 0.01 && scene.a > -0.5 && left.g > 0.03 ? 0 : 1; once < 1; once++)
                    {
                        // (only on what faces the sun)
                        float sunward = smoothstep(-0.05, 0.35, dot(faces, _UnderSunWay.xyz));
                        lit_ *= lerp(1.0, gathered(spot0 + run * max(water, 0.0), thingDown), _UnderSun.z * sunward * exp(-thingDown / 14.0));
                    } }
                    { [loop] for (int once = _UnderSun.z > 0.01 ? 0 : 1; once < 1; once++)
                    {
                        // (The water's own light along the way comes mostly from near the glass: over half of it from the first ten
                        // metres. That part is looked at in ten places, each standing for a tenth of it; what comes from beyond is
                        // taken as even.)
                        float takes = _UnderTakes.g;
                        float reach = min(max(water, 0.0), 10.0);
                        float near_ = 1.0 - exp(-takes * reach), all_ = 1.0 - exp(-takes * max(water, 0.0));
                        float sum = 0.0;
                        { [loop] for (int i = 0; i < 10; i++)
                        {
                            float far_ = -log(1.0 - (float(i) + 0.5) / 10.0 * near_) / takes;
                            float down = deep - rises * far_;
                            sum += down > 0.0 ? lerp(1.0, beams(spot0 + run * far_, down), exp(-down / 20.0)) : 1.0;
                        } }
                        light *= lerp(1.0, (sum / 10.0 * near_ + (all_ - near_)) / max(all_, 1e-4), _UnderSun.z);
                    } }
                    colour = lerp(colour, colour * lit_ * left + light * (1.0 - left), wet);
                } }
                // ---- the line: the water's edge on the glass, where the water climbs it. From the water's level up to two thirds of
                // the climb the glass shows the water's own dark (the light thrown back down); at the lip, a glint of the sky.
                float climbs = _UnderTakes.w;
                float above_ = -along;
                float dark = smoothstep(-0.3 * climbs, 0.05 * climbs, above_) * (1.0 - smoothstep(0.55 * climbs, 0.8 * climbs, above_));
                float lip = smoothstep(0.55 * climbs, 0.8 * climbs, above_) * (1.0 - smoothstep(0.85 * climbs, 1.15 * climbs, above_));
                colour = lerp(colour, _UnderLight.rgb * 0.22, 0.88 * dark);
                colour = lerp(colour, _UnderSky.rgb, 0.35 * lip);
                if (_UnderLight.w > 0.5) colour = pow(max(colour, ((float3)(0.0))), ((float3)(1.0 / 2.2)));
                // (Where the glass is dry and the line is not, the picture is handed on exactly as it came. It is the picture before the
                // lens's own effects, and its numbers are not all between nought and one: the sky's red is below nought, and a sky
                // taken as light and back, which loses that, came out of the game's colour grading green.)
                colour = max(wet, max(dark, lip)) > 0.0 ? colour : scene.rgb;
                // (for finding faults, from the development build: 1: red where the picture says the sea's underside is seen, green how wet the glass is, blue the depth in bands of five centimetres; 2: the picture's fourth number: white below nought (the sea's underside), red more than 1, green 1, blue less; 3: how much water lies between: red to 5 m, green to 20, blue to 80)
                if (_UnderSky.w > 2.5) colour = wet > 0.0 ? float3(clamp(shown / 5.0, 0.0, 1.0), clamp(shown / 20.0, 0.0, 1.0), clamp(shown / 80.0, 0.0, 1.0)) : scene.rgb;
                else if (_UnderSky.w > 0.5) colour = _UnderSky.w < 1.5 ? float3(scene.a < -0.5 ? 1.0 : 0.0, wet, frac(deep * 20.0)) : (scene.a < -0.5 ? ((float3)(1.0)) : scene.a > 1.001 ? float3(1.0, 0.0, 0.0) : scene.a > 0.999 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0));
                result = float4(colour, wet > 0.0 || scene.a < 0.0 ? 1.0 : scene.a);
                return result;
            }
            ENDCG
        }
    }
}
