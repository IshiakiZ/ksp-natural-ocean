// NaturalOcean/Weed: made from tools/shaderpack/oceanweed.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "NaturalOcean/Weed"
{
    Properties
    {

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

            float4 _WeedSway;     // xyz: the way the water moves to and fro, in the scene; w: how far a plant's tip is carried, as a share of its height
            float4 _WeedClock;     // x: where the sway is in its swing (radians); y: the same for a quicker flutter; z: ribbons to a plant; w: rows of points up a ribbon
            float4 _WeedEye;     // xyz: the camera, in the scene; w: how far from it plants stand at their full height (m); half as far again and there are none
            float4 _WeedLight;     // rgb: the sun's light at the sea's surface (as light); w: 1 if the game keeps its picture as numbers for the screen
            float4 _WeedSky;     // rgb: the sky's light at the surface; w: how far one sees under water, m
            float4 _WeedWater;     // rgb: the water's colour where last frame's picture cannot say; w: 1 if the camera is under water
            float4 _OceanMirrorX;     // the picture the game finished last frame: xyz, with the next two, turn a way in the scene into where that way was in it; w: 1 if there is one
            float4 _OceanMirrorY;
            float4 _OceanMirrorW;
            float4 _WeedUpHere;     // xyz: which way is up, in the scene; w: 1 while the lens's own pass is on (oceanunder.glsl)
            Texture2D _OceanMirror; SamplerState sampler_OceanMirror;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 vs_TEXCOORD0 : TEXCOORD0;
                float3 vs_TEXCOORD1 : TEXCOORD1;
                float2 vs_TEXCOORD2 : TEXCOORD2;
            };

            // Natural Ocean: seaweed (Weed.cs). A plant is a handful of ribbons rising from one point of the seabed, and its mesh is
            // only that point said over and over, once for every corner of every ribbon: which corner a point is meant for is read from
            // its number in the mesh, and the ribbon is laid out here. (The shaders packed by hand are given positions and nothing
            // else.) What differs from plant to plant is carried in the point's own last digits: the place is kept to the nearest two
            // centimetres and what is left under that is a number from 0 to 1 (how it turns, how it sways, how tall it stands).

            v2f vert(float3 in_POSITION0 : POSITION, uint vertexId : SV_VertexID)
            {
                v2f OUT = (v2f)0;
                int ribbons = int(_WeedClock.z + 0.5), rows = int(_WeedClock.w + 0.5);
                int corner = int(vertexId) % (ribbons * rows * 2);
                int ribbon = corner / (rows * 2), row = (corner / 2) % rows, side = corner % 2;
                // (the point, and the three numbers under its two centimetres)
                float3 kept = floor(abs(in_POSITION0) * 50.0) / 50.0;
                float3 chance = frac(abs(in_POSITION0) * 50.0) / 0.975;
                float3 foot = kept * sign(in_POSITION0 + 1e-9);
                float deep = kept.y;
                // (short close grass in the shallows, tall weed deeper, never to the surface)
                float tall = min(lerp(0.35, 0.9, chance.y) + smoothstep(2.5, 6.0, deep) * lerp(0.8, 2.8, chance.y), 0.7 * deep);
                // (towards the edge of where plants are made they dwindle to nothing: beyond it the beds are only the dark the sea's
                // shader lays on the bottom, and the two must meet without an edge)
                float4 stands = hlslcc_mtx4x4unity_ObjectToWorld[0] * kept.x + hlslcc_mtx4x4unity_ObjectToWorld[1] * (-kept.y) + hlslcc_mtx4x4unity_ObjectToWorld[2] * kept.z + hlslcc_mtx4x4unity_ObjectToWorld[3];
                tall *= 1.0 - smoothstep(_WeedEye.w, _WeedEye.w * 1.5, length(stands.xyz - _WeedEye.xyz));
                float t = float(row) / float(rows - 1);
                float turn = (float(ribbon) + chance.x) * 6.2832 / float(ribbons) + chance.z * 1.7;
                float3 outward = float3(cos(turn), 0.0, sin(turn)), sideways = float3(-sin(turn), 0.0, cos(turn));
                float own = frac(chance.x * 7.3 + float(ribbon) * 0.37);
                float high = tall * (0.6 + 0.4 * own);
                float wide = lerp(0.016, 0.055, smoothstep(2.5, 6.0, deep)) * (0.7 + 0.6 * own) * (1.0 - 0.8 * t * t);
                float3 local = foot + float3(0.0, high * t, 0.0) + outward * (0.05 + high * 0.28 * t * t * (0.4 + own)) + sideways * ((float(side) - 0.5) * 2.0 * wide);
                float4 world = hlslcc_mtx4x4unity_ObjectToWorld[0] * local.x + hlslcc_mtx4x4unity_ObjectToWorld[1] * local.y + hlslcc_mtx4x4unity_ObjectToWorld[2] * local.z + hlslcc_mtx4x4unity_ObjectToWorld[3];
                // (carried to and fro by the water, the tip furthest, each plant a little out of step with its neighbours)
                float swing = sin(_WeedClock.x + chance.x * 2.4 + chance.z * 1.3) * 0.8 + sin(_WeedClock.y + chance.z * 6.0 + float(ribbon)) * 0.2;
                world.xyz += _WeedSway.xyz * (_WeedSway.w * high * t * t * swing);
                float3 p = world.xyz;
                float4 clip_ = hlslcc_mtx4x4unity_MatrixVP[0] * p.x + hlslcc_mtx4x4unity_MatrixVP[1] * p.y + hlslcc_mtx4x4unity_MatrixVP[2] * p.z + hlslcc_mtx4x4unity_MatrixVP[3];
                OUT.pos = clip_;
                OUT.vs_TEXCOORD0 = float4(t, frac(chance.z * 3.1 + own), deep - high * t, clip_.w);
                OUT.vs_TEXCOORD1 = p;
                OUT.vs_TEXCOORD2 = float2(float(side), own);
                return OUT;
            }

            // A ribbon of weed: dark at the foot, olive towards the tip, lit by what sun and sky reach down to it (water takes the light
            // with depth); seen from under water it is lost in the water's colour over some tens of metres, as the sea's surface is (the
            // colour read from the foot of the picture the game finished last frame). Seen from above it needs nothing: the sea's own
            // shader looks at it through the water.

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                float t = IN.vs_TEXCOORD0.x, shade = IN.vs_TEXCOORD0.y, deep = max(IN.vs_TEXCOORD0.z, 0.0);
                float own = IN.vs_TEXCOORD2.y, edge = abs(IN.vs_TEXCOORD2.x * 2.0 - 1.0);
                float3 leaf = lerp(float3(0.010, 0.026, 0.009), float3(0.10, 0.16, 0.03), t) * (0.55 + 0.5 * shade + 0.5 * own);
                // (brown weed here and there among the green; a ribbon is lighter along its middle, where it is thickest, than at its edges)
                leaf = lerp(leaf, leaf.grb * float3(1.1, 0.75, 0.6), step(0.72, shade));
                leaf *= 1.15 - 0.55 * edge * edge;
                float3 lit_ = _WeedLight.rgb * exp(-deep * float3(0.30, 0.09, 0.07)) * 0.85 + _WeedSky.rgb * exp(-deep * float3(0.22, 0.07, 0.05)) * 0.7;
                float3 colour = leaf * lit_ * 2.2;
                if (_WeedWater.w > 0.5)
                {
                    // (the water's colour: where last frame's picture looked level through the water, a little under the far surface; where
                    // that was out of the picture, its foot. As the sea's own shader takes it.)
                    float3 ahead = normalize(_OceanMirrorW.xyz), up = _WeedUpHere.xyz;
                    float3 level = ahead - up * dot(ahead, up);
                    float3 way = normalize(normalize(level + up * 1e-5) - up * 0.06);
                    float w = dot(_OceanMirrorW.xyz, way);
                    float2 uv = float2(dot(_OceanMirrorX.xyz, way), dot(_OceanMirrorY.xyz, way)) / max(w, 1e-4) * 0.5 + 0.5;
                    float2 inside = min(uv, 1.0 - uv);
                    float there = w > 1e-3 ? smoothstep(0.0, 0.1, min(inside.x, inside.y)) : 0.0;
                    float3 water = lerp(SampleLod(_OceanMirror, float2(0.5, 0.07), 5.0).rgb, SampleLod(_OceanMirror, clamp(uv, ((float2)(0.0)), ((float2)(1.0))), 4.0).rgb, there);
                    if (_WeedLight.w > 0.5) water = pow(max(water, ((float3)(0.0))), ((float3)(2.2)));
                    water = lerp(_WeedWater.rgb, water, _OceanMirrorX.w);
                    colour = lerp(water, colour, exp(-IN.vs_TEXCOORD0.w / max(_WeedSky.w, 5.0)));
                }
                if (_WeedLight.w > 0.5) colour = pow(max(colour, ((float3)(0.0))), ((float3)(1.0 / 2.2)));
                // (With the lens's own pass on, how far off the weed is goes into the picture's fourth number, one added and the sign
                // turned, as the sea's underside does it: the game's own picture of how far off things are knows nothing of the weed,
                // and by it a ribbon standing against open water was as far off as the water behind it, and was fogged away.)
                result = float4(colour, _WeedUpHere.w > 0.5 ? -(IN.vs_TEXCOORD0.w + 1.0) : 1.0);
                return result;
            }
            ENDCG
        }
    }
}
