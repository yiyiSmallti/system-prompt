using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The water's air in the latent sea: its colour and how thick it is (Resources/Shaders/SeaCommon.cginc SeaFog), how
    // much of the surface's light the open water above the horizon takes (SeaWater, so far tall shapes melt into the
    // mid-water instead of standing black on black), a mist low over the seabed, red going first with distance, how
    // much further lights carry through it than what they light, and the lens's grade (Shaders/Bloom.shader, SeaLens).
    // Each area has its own (DungeonDef.Air) and a zone may differ from it (ZoneDef.Air); the maze eases from one zone's
    // to the next as the player walks (DungeonWorld), never switching at a border, and a battle keeps the one it was met
    // in: Current stays as it was while the maze is hidden under the battle. None is the sea as it was before zones had
    // air, every shader global this adds at 0, which is also what a camera reads that draws the sea before the first
    // DungeonWorld.ApplySea (the title, the mirror's first frame).
    public struct SeaAir
    {
        public Color Fog, Surface;   // _SeaFogColor, _SeaSurfaceColor
        public float Density;        // _SeaFogDensity, per metre
        public float Lift;           // 0-1: how much of the surface's light the open water above the horizon takes (_SeaAir.x)
        public float Mist;           // how much thicker the water is low over the seabed (_SeaAir.y)
        public float Carry;          // 0-1: lights carry through the fog further than what they light (_SeaAir.z)
        public Vector3 Absorb;       // the fog thicker per channel, as a power on what is left past 1 (_SeaAbsorb): red goes first
        public Vector3 Gain;         // the lens's grade: each channel scaled
        public float Saturation;
        public Color Shadow;         // the dark tones tinted toward it, by a
        public float Vignette;       // added to the lens's own darkening at the edges
        public Vector4 Floor;        // the seabed (Seabed.shader _SeaFloor): x the walls' light spilling at their feet, y a wet
                                     // sheen toward the far floor, z the far grid lines fainter, w silt in broad patches
        public Vector4 Wet;          // _SeaFloorSpec: x flares and the lamp mirrored in it, y fewer vents (taken off 7%), z the
                                     // rift's crack network where the seabed's wall mask marks it (blue), w how much it breathes

        public static SeaAir Lerp(SeaAir a, SeaAir b, float t) => new SeaAir
        {
            Fog = Color.Lerp(a.Fog, b.Fog, t),
            Surface = Color.Lerp(a.Surface, b.Surface, t),
            Density = Mathf.Lerp(a.Density, b.Density, t),
            Lift = Mathf.Lerp(a.Lift, b.Lift, t),
            Mist = Mathf.Lerp(a.Mist, b.Mist, t),
            Carry = Mathf.Lerp(a.Carry, b.Carry, t),
            Absorb = Vector3.Lerp(a.Absorb, b.Absorb, t),
            Gain = Vector3.Lerp(a.Gain, b.Gain, t),
            Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
            // The tint's colour held while it fades in or out, so it never passes through black.
            Shadow = new Color(
                Mathf.Lerp(a.Shadow.a > 0f ? a.Shadow.r : b.Shadow.r, b.Shadow.a > 0f ? b.Shadow.r : a.Shadow.r, t),
                Mathf.Lerp(a.Shadow.a > 0f ? a.Shadow.g : b.Shadow.g, b.Shadow.a > 0f ? b.Shadow.g : a.Shadow.g, t),
                Mathf.Lerp(a.Shadow.a > 0f ? a.Shadow.b : b.Shadow.b, b.Shadow.a > 0f ? b.Shadow.b : a.Shadow.b, t),
                Mathf.Lerp(a.Shadow.a, b.Shadow.a, t)),
            Vignette = Mathf.Lerp(a.Vignette, b.Vignette, t),
            Floor = Vector4.Lerp(a.Floor, b.Floor, t),
            Wet = Vector4.Lerp(a.Wet, b.Wet, t),
        };

        // A zone's air: its area's with what is given changed (colours as hex; a negative number leaves it; spill: how much
        // of the walls' light pools at their feet, Floor.x).
        public SeaAir With(string fog = null, string surface = null, float density = -1f, float lift = -1f, float mist = -1f,
            float saturation = -1f, string shadow = null, float shadowAmount = 0f, float vignette = float.NaN, Vector3? gain = null,
            float spill = -1f)
        {
            var a = this;
            if (fog != null) a.Fog = Palette.Hex(fog);
            if (surface != null) a.Surface = Palette.Hex(surface);
            if (density >= 0f) a.Density = density;
            if (lift >= 0f) a.Lift = lift;
            if (mist >= 0f) a.Mist = mist;
            if (saturation >= 0f) a.Saturation = saturation;
            if (shadow != null)
            {
                var c = Palette.Hex(shadow);
                c.a = shadowAmount;
                a.Shadow = c;
            }
            if (!float.IsNaN(vignette)) a.Vignette = vignette;
            if (gain.HasValue) a.Gain = gain.Value;
            if (spill >= 0f) a.Floor.x = spill;
            return a;
        }

        // Whether the lens has anything to grade (Bloom.shader skips it when not, drawing exactly as before).
        public bool Graded => Gain != Vector3.one || Saturation != 1f || Shadow.a > 0f;
    }

    public static class SeaAtmos
    {
        public static readonly SeaAir None = new SeaAir
        {
            Fog = Palette.Hex("#061428"), Surface = Palette.Hex("#2F7FB8"), Density = 0.016f, Gain = Vector3.one, Saturation = 1f,
        };

        // Near the surface: the open water above the horizon lifts toward the surface's light, the far shapes fading into
        // it rather than into black; a little denser than before, so what is near stands in front of what is far, with a
        // mist low over the seabed; red goes first, the lights carry, and the darks take the water's blue.
        // Its seabed wet and lit at the walls' feet, the far grid quieter, fewer vents (scene-shallows SH-07).
        public static readonly SeaAir Shallows = None.With(density: 0.018f, lift: 1f, mist: 0.5f, saturation: 1.05f, shadow: "#0A2A40", shadowAmount: 0.08f)
            .Carrying(1f, new Vector3(0.3f, 0f, -0.1f)).Floored(new Vector4(0.55f, 0.35f, 1f, 1f), new Vector4(0.6f, 0.03f, 0f, 0f));

        // Far down: barely any of the surface's light above, thicker low over the floor, a touch colder and greyer, the
        // edges darker. Each zone keeps a blue cast (scene-deep-arena DA-04), never a plain grey.
        public static readonly SeaAir Deep = None.With(fog: "#101823", lift: 0.5f, mist: 0.8f, saturation: 0.95f, vignette: 0.05f,
            gain: new Vector3(0.95f, 0.97f, 1f)).Carrying(1f, new Vector3(0.35f, 0.05f, -0.1f))
            .Floored(new Vector4(0.45f, 0.3f, 1f, 0.6f), new Vector4(1f, 0.02f, 1f, 0.15f));

        // The battle ring's seabed under the zone's air (DungeonWorld.ApplySea in a battle): no walls to spill and its own
        // pool of light, so the grid stays even, wetter and mirroring the effects' flares fully (scene-deep-arena DA-07).
        public static Vector4 RingFloor(SeaAir a) => a.Floor == Vector4.zero ? Vector4.zero : new Vector4(0f, Mathf.Max(a.Floor.y, 0.3f), 0f, a.Floor.w);
        public static Vector4 RingWet(SeaAir a) => a.Wet == Vector4.zero ? Vector4.zero : new Vector4(1f, a.Wet.y, 0f, 0f);

        // What the shaders and the lenses take now: eased by the maze, kept by a battle; None outside a map.
        public static SeaAir Current = None;

        // The lab's switch (E2E/VisualLab.cs, -airless): None everywhere, to compare with the sea from before.
        static bool off;

        public static SeaAir For(DungeonDef def, ZoneDef zone) => off ? None : zone?.Air ?? def.Air;

        public static bool Off => off;

        static SeaAir Carrying(this SeaAir a, float carry, Vector3 absorb)
        {
            a.Carry = carry;
            a.Absorb = absorb;
            return a;
        }

        static SeaAir Floored(this SeaAir a, Vector4 floor, Vector4 wet)
        {
            a.Floor = floor;
            a.Wet = wet;
            return a;
        }
    }
}
