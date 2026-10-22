using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // A PNG portrait moved like a Live2D model, for a character with a rig (CharacterDef.Rig) but no model.
    // The picture is drawn as a mesh that is bent every frame: she breathes, the upper body and the head sway
    // around the neck, the face turns toward the pointer (or lets the gaze wander), the long hair swings behind
    // with a lag, and on the default face the eyes blink: the closed-eye patch fades in over them. The other
    // faces keep their eyes open, as theirs sit elsewhere than the patch.
    public sealed class RiggedPortrait : PortraitView
    {
        const float Width = 1024f, Height = 1536f; // the portrait PNGs; the rig is in their pixels
        const int Columns = 32, Rows = 48;
        const int PatchColumns = 8, PatchRows = 4;
        const float BreathPixels = 4.5f;  // how far the shoulders rise
        const float BreathSeconds = 3.8f;
        const float BodyPixels = 3f;      // upper-body sway
        const float SwayDegrees = 1.4f;   // idle head sway
        const float LeanDegrees = 2f;     // head lean toward where she looks
        const float TurnPixels = 14f;     // how far the face shifts inside the head at a full turn
        const float NodPixels = 5f;       // head bob while she talks
        const float HairPixels = 7f;      // the hair ends' swing in the breeze
        const float Falloff = 120f;       // pixels over which the head, the body and the hair blend into each other
        const float LookReach = 700f;     // panel pixels from her face for a full turn
        const float PointerHold = 2.5f;   // seconds she keeps looking where the pointer last was
        const float BlinkClose = 0.07f, BlinkShut = 0.05f, BlinkOpen = 0.13f;

        static ushort[] gridIndices, patchIndices;

        readonly PortraitRig rig;
        readonly Texture2D blinkTexture;
        readonly Vertex[] grid = new Vertex[(Columns + 1) * (Rows + 1)];
        readonly Vertex[] patch = new Vertex[(PatchColumns + 1) * (PatchRows + 1)];
        readonly float seed;
        Texture2D texture;
        Color tint = Color.white;
        bool blinks;

        float time;
        float breath, body, tilt;
        Vector2 look, turn, turnVelocity, pointer;
        float pointerAt = -99f;
        float hairLeft, hairLeftVelocity, hairRight, hairRightVelocity;
        float blink, blinkStart = -1f, nextBlink;
        float talk, talkUntil;
        float startle;

        public RiggedPortrait(CharacterDef def) : base(def, null)
        {
            rig = def.Rig;
            blinkTexture = rig.Blink.width > 0 ? Ui.Tex(def.PortraitKey("blink")) : null;
            seed = Random.value * 100f;
            nextBlink = Random.Range(0.8f, 3f);
            gridIndices ??= Indices(Columns, Rows);
            patchIndices ??= Indices(PatchColumns, PatchRows);
            Element.generateVisualContent += Draw;
            Element.schedule.Execute(Step).Every(16);
        }

        public override Color Tint
        {
            set
            {
                tint = value;
                Element.MarkDirtyRepaint();
            }
        }

        public override bool HasIdleMotion => true;

        protected override void ShowFace(string face)
        {
            texture = Ui.Tex(Def.PortraitKey(face));
            blinks = face == Def.DefaultFace && blinkTexture != null;
            Element.MarkDirtyRepaint();
        }

        public override void Talk(float seconds) => talkUntil = time + seconds;

        public override void LookAt(Vector2 panelPosition)
        {
            if (Element.panel == null || !Fit(out var origin, out var scale)) return;
            var away = (panelPosition - Element.LocalToWorld(origin + rig.Face * scale)) / LookReach;
            pointer = new Vector2(Mathf.Clamp(away.x, -1f, 1f), Mathf.Clamp(away.y, -1f, 1f));
            pointerAt = time;
        }

        // The hair flies out to both sides and the head gives a little shake.
        public override void Poke()
        {
            startle = 1f;
            hairLeftVelocity -= Random.Range(90f, 150f);
            hairRightVelocity += Random.Range(90f, 150f);
        }

        void Step(TimerState timer)
        {
            var dt = Mathf.Min(timer.deltaTime / 1000f, 0.05f);
            if (dt <= 0f) return;
            time += dt;
            var t = time + seed;

            breath = BreathPixels * (0.5f - 0.5f * Mathf.Cos(t * 2f * Mathf.PI / BreathSeconds));
            body = BodyPixels * Mathf.Sin(t * 0.61f);

            var target = time - pointerAt < PointerHold
                ? pointer
                : new Vector2(Mathf.PerlinNoise(t * 0.13f, seed) * 2f - 1f, (Mathf.PerlinNoise(seed, t * 0.11f) * 2f - 1f) * 0.6f) * 0.7f;
            Spring(ref turn.x, ref turnVelocity.x, target.x, 30f, 11f, dt);
            Spring(ref turn.y, ref turnVelocity.y, target.y, 30f, 11f, dt);

            talk = Mathf.MoveTowards(talk, time < talkUntil ? 1f : 0f, dt * 4f);
            startle = Mathf.MoveTowards(startle, 0f, dt * 2.2f);
            var nod = talk * Mathf.Abs(Mathf.Sin(t * 7f)) * NodPixels;
            look = new Vector2(turn.x * TurnPixels, turn.y * TurnPixels * 0.6f + nod);
            var degrees = SwayDegrees * (0.7f * Mathf.Sin(t * 0.83f) + 0.3f * Mathf.Sin(t * 0.37f + 1.3f))
                + turn.x * LeanDegrees + startle * Mathf.Sin(time * 18f) * 2.5f;
            tilt = degrees * Mathf.Deg2Rad;

            // Each side swings toward its own breeze, underdamped so a poke rings out.
            Spring(ref hairLeft, ref hairLeftVelocity, HairPixels * (0.65f * Mathf.Sin(t * 1.1f) + 0.35f * Mathf.Sin(t * 0.53f)), 16f, 2.4f, dt);
            Spring(ref hairRight, ref hairRightVelocity, HairPixels * (0.65f * Mathf.Sin(t * 1.1f + 1.7f) + 0.35f * Mathf.Sin(t * 0.53f + 1f)), 16f, 2.4f, dt);

            if (blinkStart < 0f && time >= nextBlink) blinkStart = time;
            if (blinkStart >= 0f)
            {
                var s = time - blinkStart;
                blink = s < BlinkClose ? s / BlinkClose
                    : s < BlinkClose + BlinkShut ? 1f
                    : Mathf.Clamp01(1f - (s - BlinkClose - BlinkShut) / BlinkOpen);
                if (s >= BlinkClose + BlinkShut + BlinkOpen)
                {
                    blinkStart = -1f;
                    blink = 0f;
                    nextBlink = time + (Random.value < 0.2f ? 0.12f : Random.Range(2.2f, 5.5f)); // now and then a double blink
                }
            }
            Element.MarkDirtyRepaint();
        }

        static void Spring(ref float value, ref float velocity, float target, float stiffness, float damping, float dt)
        {
            velocity += ((target - value) * stiffness - velocity * damping) * dt;
            value += velocity * dt;
        }

        // Where the picture sits in the element, fitted like ScaleMode.ScaleToFit.
        bool Fit(out Vector2 origin, out float scale)
        {
            var r = Element.contentRect;
            scale = Mathf.Min(r.width / Width, r.height / Height);
            origin = new Vector2(r.x + (r.width - Width * scale) / 2f, r.y + (r.height - Height * scale) / 2f);
            return scale > 0f;
        }

        void Draw(MeshGenerationContext mgc)
        {
            if (texture == null || !Fit(out var origin, out var scale)) return;
            Fill(mgc, texture, grid, gridIndices, Columns, Rows, new Rect(0f, 0f, Width, Height), tint, origin, scale);
            if (!blinks || blink <= 0f) return;
            var shut = tint;
            shut.a *= blink;
            var b = rig.Blink;
            Fill(mgc, blinkTexture, patch, patchIndices, PatchColumns, PatchRows, new Rect(b.x, b.y, b.width, b.height), shut, origin, scale);
        }

        void Fill(MeshGenerationContext mgc, Texture source, Vertex[] vertices, ushort[] indices, int columns, int rows, Rect area,
            Color color, Vector2 origin, float scale)
        {
            var mesh = mgc.Allocate(vertices.Length, indices.Length, source);
            var uv = mesh.uvRegion;
            Color32 tinted = color;
            var i = 0;
            for (var y = 0; y <= rows; y++)
            for (var x = 0; x <= columns; x++, i++)
            {
                var u = (float)x / columns;
                var v = (float)y / rows;
                var p = Warp(new Vector2(area.x + u * area.width, area.y + v * area.height));
                vertices[i].position = new Vector3(origin.x + p.x * scale, origin.y + p.y * scale, Vertex.nearZ);
                vertices[i].uv = new Vector2(uv.x + u * uv.width, uv.y + (1f - v) * uv.height); // texture rows run bottom-up
                vertices[i].tint = tinted;
            }
            mesh.SetAllVertices(vertices);
            mesh.SetAllIndices(indices);
        }

        // Where a point of the picture is drawn this frame (picture pixels in, picture pixels out). The weights
        // come from where the point is in the picture, the moves add up from the hips to the hair ends.
        Vector2 Warp(Vector2 at)
        {
            var p = at;
            var rise = Mathf.Clamp01((rig.Hips - at.y) / (rig.Hips - rig.Neck.y));
            p += new Vector2(body, -breath) * rise;

            var head = Smooth((rig.Neck.y + Falloff / 3f - at.y) / Falloff);
            if (head > 0f)
            {
                // The face moves more than the head's outline, which reads as the head turning.
                var off = (at - rig.Face) / rig.FaceRadius;
                var bump = Mathf.Max(0f, 1f - off.sqrMagnitude);
                p += look * ((0.35f + 0.65f * bump * bump) * head);
                var neck = rig.Neck + new Vector2(body, -breath);
                p = neck + Rotate(p - neck, tilt * head);
            }

            var side = at.x < rig.BodyLeft ? Smooth((rig.BodyLeft - at.x) / Falloff)
                : at.x > rig.BodyRight ? Smooth((at.x - rig.BodyRight) / Falloff)
                : 0f;
            if (side > 0f)
            {
                var hang = Smooth((at.y - rig.Shoulders) / (Height - rig.Shoulders));
                p.x += (at.x < rig.Neck.x ? hairLeft : hairRight) * side * hang;
            }
            return p;
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // Two clockwise triangles per cell, as UI Toolkit expects.
        static ushort[] Indices(int columns, int rows)
        {
            var indices = new ushort[columns * rows * 6];
            var i = 0;
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var topLeft = (ushort)(y * (columns + 1) + x);
                var topRight = (ushort)(topLeft + 1);
                var bottomLeft = (ushort)(topLeft + columns + 1);
                var bottomRight = (ushort)(bottomLeft + 1);
                indices[i++] = bottomLeft;
                indices[i++] = topLeft;
                indices[i++] = topRight;
                indices[i++] = topRight;
                indices[i++] = bottomRight;
                indices[i++] = bottomLeft;
            }
            return indices;
        }
    }
}
