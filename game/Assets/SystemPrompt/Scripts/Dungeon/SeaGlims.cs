using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The light the maze's events throw round them (a block seating, a beacon lit, a wall dissolving) and the ring that
    // runs out over the seabed from them. They borrow the battle's light (SeaCommon.cginc SeaFlash, read by Toon,
    // Seabed, Glitch and Ice), which nothing else writes while the maze is shown, and add one of their own, the seabed's
    // ring (SeaPulse, Seabed only, so it does not climb the walls). Everything runs on the maze's own time, so a hurried
    // cutaway hurries them too. Put out (Clear) when the maze is hidden or dropped, so a battle starts dark.
    public sealed class SeaGlims
    {
        // All the lights together never past this, so what they light keeps its own colour and only the lights
        // themselves cross the lens's bloom. Halved with 减弱闪光, as each light is.
        public const float MaxLight = 0.6f;

        struct Glim
        {
            public Vector3 At;
            public Color Color;
            public float Radius, Start, Seconds;
        }

        readonly List<Glim> glims = new List<Glim>();
        bool lit, ringing;
        Vector3 ringAt;
        Color ringColor;
        float ringStart, ringLife, ringSpeed, ringWidth;

        static bool Calm => GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;

        // A light at `at` reaching `radius` metres: up in a moment and dying away over `seconds`.
        public void Light(Vector3 at, Color color, float radius, float seconds, float time)
        {
            if (Calm) color *= 0.5f;
            glims.Add(new Glim { At = at, Color = color, Radius = radius, Start = time, Seconds = Mathf.Max(seconds, 0.05f) });
        }

        // A ring running out over the seabed from `at` at `speed` metres a second, `width` metres wide, gone after
        // `life` seconds. One at a time: a new one takes over.
        public void Pulse(Vector3 at, Color color, float time, float speed = 9f, float life = 0.9f, float width = 0.6f)
        {
            if (Calm) color *= 0.5f;
            ringing = true;
            ringAt = at;
            ringColor = color;
            ringStart = time;
            ringLife = life;
            ringSpeed = speed;
            ringWidth = width;
        }

        // Every frame the maze is shown, after everything has moved: the lights burning as one (where the brightest
        // are, as bright as all of them up to MaxLight), and the ring where it has got to.
        public void Shine(float time)
        {
            var sum = Color.black;
            var at = Vector3.zero;
            float weight = 0f, radius = 0f;
            for (var i = glims.Count - 1; i >= 0; i--)
            {
                var g = glims[i];
                var u = (time - g.Start) / g.Seconds;
                if (u >= 1f)
                {
                    glims.RemoveAt(i);
                    continue;
                }
                var k = (1f - u) * (1f - u);
                if (u < 0.08f) k *= u / 0.08f; // not up in a single frame
                var w = k * g.Color.maxColorComponent;
                sum += g.Color * k;
                at += g.At * w;
                weight += w;
                radius = Mathf.Max(radius, g.Radius);
            }
            var any = weight > 1e-4f;
            if (any || lit)
            {
                if (any) at /= weight;
                var most = sum.maxColorComponent;
                var cap = Calm ? MaxLight * 0.5f : MaxLight;
                if (most > cap) sum *= cap / most;
                Shader.SetGlobalVector("_SeaFlashPos", new Vector4(at.x, at.y, at.z, any ? radius : 0.01f));
                Shader.SetGlobalColor("_SeaFlashColor", any ? new Color(sum.r, sum.g, sum.b, 0f) : Color.clear);
                lit = any;
            }
            if (!ringing) return;
            var age = time - ringStart;
            if (age >= ringLife)
            {
                ringing = false; // DungeonWorld.ApplySea has put the ring out for this frame already
                return;
            }
            var f = 1f - age / ringLife;
            var c = ringColor * (f * f);
            Shader.SetGlobalVector("_SeaPulse", new Vector4(ringAt.x, ringAt.y, ringAt.z, age * ringSpeed));
            Shader.SetGlobalColor("_SeaPulseColor", new Color(c.r, c.g, c.b, ringWidth));
        }

        public void Clear()
        {
            glims.Clear();
            lit = ringing = false;
            Shader.SetGlobalColor("_SeaFlashColor", Color.clear);
            Shader.SetGlobalVector("_SeaPulse", Off);
            Shader.SetGlobalColor("_SeaPulseColor", Color.clear);
        }

        public static readonly Vector4 Off = new Vector4(0f, 0f, 0f, -1f);
    }
}
