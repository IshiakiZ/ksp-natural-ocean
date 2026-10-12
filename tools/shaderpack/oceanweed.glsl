#ifdef VERTEX
#version 150
// Natural Ocean: seaweed (Weed.cs). A plant is a handful of ribbons rising from one point of the seabed, and its mesh is
// only that point said over and over, once for every corner of every ribbon: which corner a point is meant for is read from
// its number in the mesh, and the ribbon is laid out here. (The shaders packed by hand are given positions and nothing
// else.) What differs from plant to plant is carried in the point's own last digits: the place is kept to the nearest two
// centimetres and what is left under that is a number from 0 to 1 (how it turns, how it sways, how tall it stands).
uniform vec4 hlslcc_mtx4x4unity_ObjectToWorld[4];
uniform vec4 hlslcc_mtx4x4unity_MatrixVP[4];
uniform vec4 _WeedSway;       // xyz: the way the water moves to and fro, in the scene; w: how far a plant's tip is carried, as a share of its height
uniform vec4 _WeedClock;      // x: where the sway is in its swing (radians); y: the same for a quicker flutter; z: ribbons to a plant; w: rows of points up a ribbon
uniform vec4 _WeedEye;        // xyz: the camera, in the scene; w: how far from it plants stand at their full height (m); half as far again and there are none
in vec3 in_POSITION0;
out vec4 vs_TEXCOORD0;        // x: how far up the ribbon (0 foot, 1 tip); y: the plant's own shade (0 to 1); z: how far under the sea's level; w: how far from the camera
out vec3 vs_TEXCOORD1;        // the point in the scene
out vec2 vs_TEXCOORD2;        // x: from one edge of the ribbon (0) to the other (1); y: the ribbon's own number (0 to 1)

void main()
{
    int ribbons = int(_WeedClock.z + 0.5), rows = int(_WeedClock.w + 0.5);
    int corner = gl_VertexID % (ribbons * rows * 2);
    int ribbon = corner / (rows * 2), row = (corner / 2) % rows, side = corner % 2;
    // (the point, and the three numbers under its two centimetres)
    vec3 kept = floor(abs(in_POSITION0) * 50.0) / 50.0;
    vec3 chance = fract(abs(in_POSITION0) * 50.0) / 0.975;
    vec3 foot = kept * sign(in_POSITION0 + 1e-9);
    float deep = kept.y;
    // (short close grass in the shallows, tall weed deeper, never to the surface)
    float tall = min(mix(0.35, 0.9, chance.y) + smoothstep(2.5, 6.0, deep) * mix(0.8, 2.8, chance.y), 0.7 * deep);
    // (towards the edge of where plants are made they dwindle to nothing: beyond it the beds are only the dark the sea's
    // shader lays on the bottom, and the two must meet without an edge)
    vec4 stands = hlslcc_mtx4x4unity_ObjectToWorld[0] * kept.x + hlslcc_mtx4x4unity_ObjectToWorld[1] * (-kept.y) + hlslcc_mtx4x4unity_ObjectToWorld[2] * kept.z + hlslcc_mtx4x4unity_ObjectToWorld[3];
    tall *= 1.0 - smoothstep(_WeedEye.w, _WeedEye.w * 1.5, length(stands.xyz - _WeedEye.xyz));
    float t = float(row) / float(rows - 1);
    float turn = (float(ribbon) + chance.x) * 6.2832 / float(ribbons) + chance.z * 1.7;
    vec3 outward = vec3(cos(turn), 0.0, sin(turn)), sideways = vec3(-sin(turn), 0.0, cos(turn));
    float own = fract(chance.x * 7.3 + float(ribbon) * 0.37);
    float high = tall * (0.6 + 0.4 * own);
    float wide = mix(0.016, 0.055, smoothstep(2.5, 6.0, deep)) * (0.7 + 0.6 * own) * (1.0 - 0.8 * t * t);
    vec3 local = foot + vec3(0.0, high * t, 0.0) + outward * (0.05 + high * 0.28 * t * t * (0.4 + own)) + sideways * ((float(side) - 0.5) * 2.0 * wide);
    vec4 world = hlslcc_mtx4x4unity_ObjectToWorld[0] * local.x + hlslcc_mtx4x4unity_ObjectToWorld[1] * local.y + hlslcc_mtx4x4unity_ObjectToWorld[2] * local.z + hlslcc_mtx4x4unity_ObjectToWorld[3];
    // (carried to and fro by the water, the tip furthest, each plant a little out of step with its neighbours)
    float swing = sin(_WeedClock.x + chance.x * 2.4 + chance.z * 1.3) * 0.8 + sin(_WeedClock.y + chance.z * 6.0 + float(ribbon)) * 0.2;
    world.xyz += _WeedSway.xyz * (_WeedSway.w * high * t * t * swing);
    vec3 p = world.xyz;
    vec4 clip = hlslcc_mtx4x4unity_MatrixVP[0] * p.x + hlslcc_mtx4x4unity_MatrixVP[1] * p.y + hlslcc_mtx4x4unity_MatrixVP[2] * p.z + hlslcc_mtx4x4unity_MatrixVP[3];
    gl_Position = clip;
    vs_TEXCOORD0 = vec4(t, fract(chance.z * 3.1 + own), deep - high * t, clip.w);
    vs_TEXCOORD1 = p;
    vs_TEXCOORD2 = vec2(float(side), own);
}
#endif
#ifdef FRAGMENT
#version 150
#extension GL_ARB_explicit_attrib_location : require
// A ribbon of weed: dark at the foot, olive towards the tip, lit by what sun and sky reach down to it (water takes the light
// with depth); seen from under water it is lost in the water's colour over some tens of metres, as the sea's surface is (the
// colour read from the foot of the picture the game finished last frame). Seen from above it needs nothing: the sea's own
// shader looks at it through the water.
uniform vec4 _WeedLight;      // rgb: the sun's light at the sea's surface (as light); w: 1 if the game keeps its picture as numbers for the screen
uniform vec4 _WeedSky;        // rgb: the sky's light at the surface; w: how far one sees under water, m
uniform vec4 _WeedWater;      // rgb: the water's colour where last frame's picture cannot say; w: 1 if the camera is under water
uniform vec4 _OceanMirrorX;   // the picture the game finished last frame: xyz, with the next two, turn a way in the scene into where that way was in it; w: 1 if there is one
uniform vec4 _OceanMirrorY;
uniform vec4 _OceanMirrorW;
uniform vec4 _WeedUpHere;     // xyz: which way is up, in the scene; w: 1 while the lens's own pass is on (oceanunder.glsl)
uniform sampler2D _OceanMirror;
in vec4 vs_TEXCOORD0;
in vec3 vs_TEXCOORD1;
in vec2 vs_TEXCOORD2;
layout(location = 0) out vec4 SV_Target0;

void main()
{
    float t = vs_TEXCOORD0.x, shade = vs_TEXCOORD0.y, deep = max(vs_TEXCOORD0.z, 0.0);
    float own = vs_TEXCOORD2.y, edge = abs(vs_TEXCOORD2.x * 2.0 - 1.0);
    vec3 leaf = mix(vec3(0.010, 0.026, 0.009), vec3(0.10, 0.16, 0.03), t) * (0.55 + 0.5 * shade + 0.5 * own);
    // (brown weed here and there among the green; a ribbon is lighter along its middle, where it is thickest, than at its edges)
    leaf = mix(leaf, leaf.grb * vec3(1.1, 0.75, 0.6), step(0.72, shade));
    leaf *= 1.15 - 0.55 * edge * edge;
    vec3 lit = _WeedLight.rgb * exp(-deep * vec3(0.30, 0.09, 0.07)) * 0.85 + _WeedSky.rgb * exp(-deep * vec3(0.22, 0.07, 0.05)) * 0.7;
    vec3 colour = leaf * lit * 2.2;
    if (_WeedWater.w > 0.5)
    {
        // (the water's colour: where last frame's picture looked level through the water, a little under the far surface; where
        // that was out of the picture, its foot. As the sea's own shader takes it.)
        vec3 ahead = normalize(_OceanMirrorW.xyz), up = _WeedUpHere.xyz;
        vec3 level = ahead - up * dot(ahead, up);
        vec3 way = normalize(normalize(level + up * 1e-5) - up * 0.06);
        float w = dot(_OceanMirrorW.xyz, way);
        vec2 uv = vec2(dot(_OceanMirrorX.xyz, way), dot(_OceanMirrorY.xyz, way)) / max(w, 1e-4) * 0.5 + 0.5;
        vec2 inside = min(uv, 1.0 - uv);
        float there = w > 1e-3 ? smoothstep(0.0, 0.1, min(inside.x, inside.y)) : 0.0;
        vec3 water = mix(textureLod(_OceanMirror, vec2(0.5, 0.07), 5.0).rgb, textureLod(_OceanMirror, clamp(uv, vec2(0.0), vec2(1.0)), 4.0).rgb, there);
        if (_WeedLight.w > 0.5) water = pow(max(water, vec3(0.0)), vec3(2.2));
        water = mix(_WeedWater.rgb, water, _OceanMirrorX.w);
        colour = mix(water, colour, exp(-vs_TEXCOORD0.w / max(_WeedSky.w, 5.0)));
    }
    if (_WeedLight.w > 0.5) colour = pow(max(colour, vec3(0.0)), vec3(1.0 / 2.2));
    // (With the lens's own pass on, how far off the weed is goes into the picture's fourth number, one added and the sign
    // turned, as the sea's underside does it: the game's own picture of how far off things are knows nothing of the weed,
    // and by it a ribbon standing against open water was as far off as the water behind it, and was fogged away.)
    SV_Target0 = vec4(colour, _WeedUpHere.w > 0.5 ? -(vs_TEXCOORD0.w + 1.0) : 1.0);
}
#endif
