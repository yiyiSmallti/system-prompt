// Shared by the latent-space shaders (Dungeon/DungeonWorld.cs sets the globals). The sea has its own
// fog instead of Unity's, so it survives the build's fog-variant stripping and can tint by depth.
#ifndef SEA_COMMON_INCLUDED
#define SEA_COMMON_INCLUDED

#include "UnityCG.cginc"

float4 _SeaFogColor;    // what everything fades into
float4 _SeaSurfaceColor; // the light coming down from the surface
float4 _SeaGroundColor;  // the light the seabed of the player's zone throws back up, into the figures' shade
float4 _SeaLightDir;    // xyz: toward the light
float _SeaFogDensity;   // per metre
float _SeaTime;         // unscaled seconds
float4 _SeaGlowPos;     // xyz: the player's avatar, which lights the seabed around it; w: radius
float4 _SeaGlowColor;
float4 _SeaFlashPos;    // xyz: where the battle's effects flare up (Dungeon/BattleEffects.cs); w: how far their light reaches
float4 _SeaFlashColor;  // that light, past 1 when bright; 0 when nothing flares
float4 _SeaFade;        // the maze's scenery thins out x: near the lens, y: in front of the avatar; from z metres, whole at w; 0 in a battle
float4 _SeaFocus;       // xyz: the avatar's chest, which the scenery between it and the camera makes way for; w: its height
sampler2D _SeaCausticTex;  // the surface's light net (Dungeon/SeaArt.Caustics), tiling
float _SeaCausticStrength; // how much of it reaches this deep (DungeonDef.Caustics)
float4 _SeaBlink;          // the racks' LEDs: x how fast they blink, y how low a blinking one dips, z how bright a passing packet is

// The zone's air (Dungeon/SeaAtmos.cs, set by DungeonWorld.ApplySea; all 0 is the sea as it was before zones had air,
// which is also what a camera reads that draws before the first ApplySea):
float4 _SeaAir;    // x: how much of the surface's light the open water above the horizon takes (SeaWater), y: how much
                   // thicker the water is low over the seabed, z: how much further lights carry through it (SeaReach)
float4 _SeaAbsorb; // xyz: the fog thicker per channel, as a power on what is left past 1 (red goes first)

// 1 near the camera, 0 deep in the fog. reach under 1 thins the fog for what is seen through it (a light, SeaReach).
inline float SeaClear(float3 worldPos, float reach)
{
    float d = distance(worldPos, _WorldSpaceCameraPos);
    // Thicker low over the seabed, a mist the far floor and the racks' feet sink into before their tops.
    float mist = 1.0 + _SeaAir.y * exp(-max(worldPos.y, 0.0) * 0.5);
    return exp(-pow(max(d - 4.0, 0.0) * _SeaFogDensity * mist * reach, 1.5));
}

inline float SeaClear(float3 worldPos)
{
    return SeaClear(worldPos, 1.0);
}

// How far into the fog a light seen through it carries, from the material's own reach (_FogReach, 1 as far as what it
// lights): by as much as the zone lets lights carry.
inline float SeaReach(float reach)
{
    return lerp(1.0, reach, _SeaAir.z);
}

// How much of the surface's light the open water takes looking up at `up` (the sine of the angle over the horizon): none
// at and below it (still the black of the deep), rising past 5 degrees to a fifth by 15 and over half by 45, so a far
// tall shape seen against it fades into the mid-water rather than standing black on black.
inline float SeaWater(float up)
{
    return (smoothstep(0.09, 0.26, up) * 0.2 + smoothstep(0.26, 0.7, up) * 0.35) * _SeaAir.x;
}

// The 集成's black and white (BattleStage.AllOut): the field goes to ink with its lines faintly kept, noise bodies to
// the sea's blue and the party to paper, so the dashes read as silhouettes and only the effects (which are not fogged)
// keep their colour. _InkRole comes per renderer by MaterialPropertyBlock: 0 the field, 0.5 a noise body, 1 the party.
float _SeaInk;
float _InkRole;

inline float3 SeaInk(float3 col)
{
    [branch] if (_SeaInk <= 0) return col;
    float l = dot(col, float3(0.299, 0.587, 0.114));
    float3 tone = float3(0.02, 0.025, 0.04) + float3(0.05, 0.075, 0.1) * smoothstep(0.1, 0.45, l);
    if (_InkRole > 0.75) tone = float3(0.93, 0.93, 0.9);
    else if (_InkRole > 0.01) tone = float3(0.2, 0.42, 0.62);
    return lerp(col, tone, _SeaInk);
}

inline float3 SeaFog(float3 col, float3 worldPos, float reach)
{
    // Higher up the fog takes the surface light's colour; and looking up, as much as the open water behind it takes
    // (SeaWater, as the sky does), so what is far melts into the water behind it.
    float3 v = worldPos - _WorldSpaceCameraPos;
    float up = v.y / max(length(v), 1e-4);
    float toSurface = max(saturate(worldPos.y / 60.0) * 0.5, SeaWater(up));
    float3 fog = lerp(_SeaFogColor.rgb, _SeaSurfaceColor.rgb, toSurface);
    float clear = SeaClear(worldPos, reach);
    float3 k = clear;
    if (any(_SeaAbsorb.xyz != 0)) k = pow(clear, 1.0 + _SeaAbsorb.xyz);
    return SeaInk(lerp(fog, col, k));
}

inline float3 SeaFog(float3 col, float3 worldPos)
{
    return SeaFog(col, worldPos, 1.0);
}

// What shines of itself (an LED, a crack, a lamp) seen through the fog: as dim as what it lights at first, then less,
// so a far rack's body sinks into the water while its lights still hang there.
inline float3 SeaFogGlow(float3 emission, float3 worldPos)
{
    float clear = SeaClear(worldPos);
    return emission * lerp(clear, sqrt(clear), _SeaAir.z) * (1.0 - _SeaInk);
}

// The light a flare in a battle throws on a surface facing n: fading out to its reach, wrapped a little round
// the side turned away, so a blow lights the noise body it lands on and the figures and seabed round it.
inline float3 SeaFlash(float3 worldPos, float3 n)
{
    float3 to = _SeaFlashPos.xyz - worldPos;
    float d = length(to);
    float reach = saturate(1.0 - d / max(_SeaFlashPos.w, 0.01));
    return _SeaFlashColor.rgb * reach * reach * saturate(dot(n, to / max(d, 0.001)) * 0.75 + 0.25);
}

// How much of a piece of the maze's scenery stays, 0 to 1: it thins out where the lens comes close to it, and round
// the line from the camera to the avatar's chest, short of the avatar, so a rack or bubble stack never fills the edge of
// the picture or hides the player. What shows below the avatar's ankles (a slab or ledge in front of them, seen from
// above, its edge across their shoes at most) hides next to nothing and stays whole: a dotted patch round the shoes
// would show more than it hides.
inline float SeaFadeKeep(float3 world)
{
    float near = saturate((distance(world, _WorldSpaceCameraPos) - _SeaFade.z) / max(_SeaFade.w - _SeaFade.z, 0.01));
    float3 a = _WorldSpaceCameraPos, ab = _SeaFocus.xyz - a;
    float t = saturate(dot(world - a, ab) / max(dot(ab, ab), 1e-4));
    float3 off = world - (a + ab * t);
    float3 v = normalize(ab), up = float3(0, 1, 0) - v * v.y;
    float rise = max(length(up), 1e-3);
    float under = -dot(off, up) / (rise * max(t, 0.05)), feet = _SeaFocus.w * rise;
    float between = smoothstep(0.03, 0.1, t) * (1.0 - smoothstep(0.8, 0.92, t)) * (1.0 - smoothstep(feet - 0.5, feet - 0.2, under));
    float sight = lerp(1.0, smoothstep(0.55, 1.3, length(off)), between);
    return min(lerp(1.0, near, _SeaFade.x), lerp(1.0, sight, _SeaFade.y));
}

// A fixed 4 x 4 ordered dither, 0 to 1 at a screen pixel: what stays at k is drawn on k of every 16 pixels. It never
// changes over time, which would shimmer.
inline float SeaBayer(float2 pixel)
{
    static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    int2 p = int2(fmod(pixel, 4.0));
    return (m[p.y * 4 + p.x] + 0.5) / 16.0;
}

// The same for what is added on (Glow, Text, Shaft): it just fades out near the lens instead, so a beam or a sign
// passing by it doesn't fill the picture with light.
inline float SeaNearGlow(float3 world)
{
    return lerp(1.0, saturate((distance(world, _WorldSpaceCameraPos) - _SeaFade.z) / max(_SeaFade.w - _SeaFade.z, 0.01)), _SeaFade.x);
}

inline float SeaHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// How bright a rack's LED is now, about 1 (Toon.shader _Blink): about half of them blink, each row of each face at its
// own rate, and now and then a packet of data runs along a strip. The rate is picked by the row and by where the face
// stands (its offset along its normal), never by where along the face, and the phase runs on along the face (s): a long
// strip is never cut into pieces that blink apart, what dips or brightens on it moves along it, and neighbouring lights
// blink out of step. amount 0 keeps it steady; under 1 (the battle ring, whose lights stay under the lens's threshold) it
// only dips, never brightens past its steady glow.
inline float SeaBlink(float3 world, float3 n, float amount)
{
    float s = dot(world.xz, float2(n.z, -n.x));
    float h = SeaHash(float2(floor(world.y * 4.0), floor(dot(world.xz, n.xz) * 2.0 + 0.31)) * float2(7.13, 3.71));
    float rate = lerp(0.6, 3.0, frac(h * 13.1)) * _SeaBlink.x;
    float on = step(0.22, frac(_SeaTime * rate + h * 10.0 + s * 1.7));
    float level = lerp(1.0, lerp(_SeaBlink.y, 1.0, on), step(0.55, h));
    float packet = pow(frac(s * 0.12 - _SeaTime * 0.5 * _SeaBlink.x + h), 24.0) * _SeaBlink.z;
    float b = lerp(1.0, level + packet, amount);
    return amount < 1.0 ? min(b, 1.0) : b;
}

// The net of light the surface throws, at p (metres) on a plane under it: the net (Dungeon/SeaArt.Caustics) drifting
// and wobbling as the surface swells, brighter where a second, larger one drifting the other way crosses it, so
// the bright knots wander along the lines.
inline float SeaCausticAt(float2 p)
{
    float t = _SeaTime;
    float2 uv = p * 0.085 + 0.03 * float2(sin(p.y * 0.45 + t * 0.5), sin(p.x * 0.38 - t * 0.43));
    float a = tex2D(_SeaCausticTex, uv + t * float2(0.013, 0.008)).r;
    float b = tex2D(_SeaCausticTex, uv * 1.31 + float2(0.37, 0.71) - t * float2(0.009, 0.011)).r;
    return a * (0.45 + 0.75 * b) * _SeaCausticStrength;
}

// The same at a point in the world, cast down along the light: on an upright face it runs slanted, not in stripes.
// It comes in from a metre and a half off the lens, as right in front of it the net would be a few thick smears.
inline float SeaCaustic(float3 world)
{
    float near = smoothstep(1.5, 4.5, distance(world, _WorldSpaceCameraPos));
    return SeaCausticAt(world.xz - world.y * _SeaLightDir.xz / max(_SeaLightDir.y, 0.2)) * near;
}

float4 _SeaBeamApex;               // xyz: the lighthouse's lamp; w: cos of half a beam's width
float4 _SeaBeamDirA, _SeaBeamDirB; // xz: the way each of its two beams points, flat
float4 _SeaBeamDip;                // tan of how steeply down from the lamp the light comes in (x to y) and goes (z to w)
float4 _SeaBeamColor;              // the light where a beam falls; black (a battle, the deep) turns it off

// The lighthouse's light where its two beams fall (Dungeon/DungeonWorld.cs): each a fan, narrow across and spread up
// and down, so a long stripe of light runs out from some 30 m off the tower's foot to the far edge of the sea and
// sweeps round with the lamp, passing over the seabed and the racks every few seconds. No shadows: a narrow corridor
// between tall racks takes it too.
inline float3 SeaBeam(float3 world)
{
    if (_SeaBeamColor.a <= 0) return 0;
    float3 d = world - _SeaBeamApex.xyz;
    float flat = max(length(d.xz), 1e-3);
    float2 h = d.xz / flat;
    float across = smoothstep(_SeaBeamApex.w, lerp(_SeaBeamApex.w, 1.0, 0.5), max(dot(h, _SeaBeamDirA.xz), dot(h, _SeaBeamDirB.xz)));
    float dip = -d.y / flat;
    float along = smoothstep(_SeaBeamDip.x, _SeaBeamDip.y, dip) * (1.0 - smoothstep(_SeaBeamDip.z, _SeaBeamDip.w, dip));
    return _SeaBeamColor.rgb * across * along / (1.0 + flat * flat * 0.0002);
}

// Scenery swaying in the water (Toon.shader _SwayAmp): a point at world moves sideways by up to amp, nothing at
// anchor, all of it from len above it (len below 0: anchored above, swaying more the lower it hangs), in a slow
// figure out of step from one 4 m cell to the next. A head well past len moves whole, without bending; the phase
// creeps up the height a little so a stalk or blade bends as it goes. Works on static-batched meshes, whose
// vertices are already in the world.
// A point kept out of a rounded cone from a to b (xyz, w its radius there): inside it, the point is moved straight
// out from the axis onto the surface. A loose hem laid over the leg under it (Toon.shader's _Legs).
inline float3 SeaOutOf(float3 p, float4 a, float4 b)
{
    float3 along = b.xyz - a.xyz;
    float t = saturate(dot(p - a.xyz, along) / max(dot(along, along), 1e-6));
    float3 on = a.xyz + along * t;
    float3 off = p - on;
    float d = length(off), r = lerp(a.w, b.w, t);
    return d < r ? on + off / max(d, 1e-5) * r : p;
}

inline float3 SeaSway(float3 world, float amp, float anchor, float len)
{
    if (amp <= 0) return world;
    float h = saturate((world.y - anchor) / len);
    float ph = SeaHash(floor(world.xz / 4.0)) * 6.2832 + world.y * 0.25;
    world.xz += float2(sin(_SeaTime * 0.7 + ph), cos(_SeaTime * 0.55 + 1.3 * ph)) * amp * h * h;
    return world;
}

// A ring of light running out over the seabed from an event in the maze (Dungeon/SeaGlims.cs Pulse; Seabed only).
float4 _SeaPulse;       // xyz: where it started; w: how far it has run, in metres (below 0: no ring)
float4 _SeaPulseColor;  // rgb: its light; a: how wide it is, in metres
inline float3 SeaPulse(float3 worldPos)
{
    if (_SeaPulse.w < 0.0 || _SeaPulseColor.a <= 0.0) return 0;
    float d = length(worldPos.xz - _SeaPulse.xz);
    float x = (d - _SeaPulse.w) / max(_SeaPulseColor.a, 0.05);
    float band = exp(-x * x * 4.0);
    float fill = saturate(-x) * exp(x * 1.5) * 0.35; // a faint wash inside the ring, fading towards its middle
    return _SeaPulseColor.rgb * (band + fill);
}

// The figures' and noise bodies' shadows on the seabed (Dungeon/SeaShade.cs draws them from above into a map round the
// player, or round the battle field's middle, before a sea camera draws). All 0, as outside a map and for any camera
// without SeaShade, is no shadow at all.
sampler2D _SeaShadeSoft; // how much of the seabed's light they take, seen along the shadows' light: r close under them, g what stands higher (ShadeSoft.shader)
float4x4 _SeaShadeView; // world to the map's view, looking down along the light
float4 _SeaShade;       // x: 1 / the map's width in metres; y: one texel of it in uv; w: how dark a shadow is (0: none)

// How much of the map covers a point: 1 inside, fading to 0 over its outer tenth, so a shadow leaving it never cuts off.
inline float SeaShadeCover(float3 world)
{
    if (_SeaShade.w <= 0) return 0;
    float2 uv = mul(_SeaShadeView, float4(world, 1)).xy * _SeaShade.x + 0.5;
    float2 e = min(uv, 1.0 - uv);
    return smoothstep(0.0, 0.1, min(e.x, e.y));
}

// How much light a point of the seabed keeps under them, 1 - w to 1: soft at the edges, wider and fainter the higher up
// what casts it is, so a noise body flung up casts a pale, wide shade.
inline float SeaShadow(float3 world)
{
    float cover = SeaShadeCover(world);
    if (cover <= 0) return 1;
    float2 uv = mul(_SeaShadeView, float4(world, 1)).xy * _SeaShade.x + 0.5;
    float2 shade = tex2Dlod(_SeaShadeSoft, float4(uv, 0, 0)).rg;
    return 1.0 - saturate(shade.r + shade.g) * _SeaShade.w * cover;
}

// The maze's own lights nearest the player (Dungeon/SeaLights.cs: a beacon, a pearl, a firewall, the rift's cracks), four at
// most, on what stands round them. _SeaPoints 0 (a battle, outside a map, an unset global) is none.
float4 _SeaPointPos[4];   // xyz: where; w: how far it reaches
float4 _SeaPointColor[4]; // rgb: its light, faded as it comes and goes
float _SeaPoints;         // how many are lit

// Their light on a surface facing n, wrapped a little round the side turned away.
inline float3 SeaPoints(float3 world, float3 n)
{
    float3 sum = 0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float3 to = _SeaPointPos[k].xyz - world;
        float d = max(length(to), 1e-3);
        float r = saturate(1.0 - d / max(_SeaPointPos[k].w, 0.01));
        sum += _SeaPointColor[k].rgb * r * r * saturate(dot(n, to / d) * 0.75 + 0.25);
    }
    return sum;
}

#endif
