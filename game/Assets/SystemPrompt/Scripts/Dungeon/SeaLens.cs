using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The latent space's lens on the maze's camera and the battle field's (Shaders/Bloom.shader): the picture is drawn
    // in HDR so light can go past white, and what does spills into a halo built down and back up a chain of halved
    // copies; then the edges sink toward the deep, the whole rolls off instead of clipping, and a fixed
    // dither keeps the dark blue gradients from banding. In a battle a blow's shock wave bends the picture (Wave). Only what shines crosses the threshold: Toon.shader keeps a
    // lit surface at white or under, so the LEDs, lamps and cracks of the maze glow, and in a battle the effects.
    [RequireComponent(typeof(Camera))]
    sealed class SeaLens : MonoBehaviour
    {
        // Threshold, the knee under it, how strong the halo is, how many halved copies it spreads over, how far the
        // edges darken and how white the brightest turns (Bloom.shader Roll). The battle keeps the soft knee, wide halo
        // and white-hot cores the effects were made with; the maze's knee is short, so a white bubble right at 1 adds
        // no haze, its halo tight, so a beam of light passing near the lens glows without flooding the picture, and its
        // coloured lights keep their colour, so each zone's LEDs stay its own (white ones still go white).
        public readonly struct Look
        {
            public readonly float Threshold, Knee, Intensity, Vignette, WhiteHot;
            public readonly int Levels;

            public Look(float threshold, float knee, float intensity, int levels, float vignette, float whiteHot)
            {
                Threshold = threshold;
                Knee = knee;
                Intensity = intensity;
                Levels = levels;
                Vignette = vignette;
                WhiteHot = whiteHot;
            }
        }

        public static readonly Look Explore = new Look(1.2f, 0.2f, 0.5f, 4, 0.3f, 0.25f);
        // The battle's knee was 0.5: it started at 0.6, so everything pale that was merely lit (bare skin, a white
        // stocking) fed the seven levels of halo a little each and came out washed white, the skin's colour gone.
        // At 0.3 it starts at 0.84: what is lit stays as it is drawn, what shines still glows as wide.
        public static readonly Look Battle = new Look(1.2f, 0.3f, 0.6f, MaxLevels, 0.2f, 1f);

        const int MaxLevels = 7;
        const int Prefilter = 0, Down = 1, Up = 2, Combine = 3;
        static readonly Color Deep = new Color(0.55f, 0.65f, 0.8f); // what the vignette tints toward

        Look look;
        Material lens;
        Camera eye;
        readonly RenderTexture[] levels = new RenderTexture[MaxLevels];
        // The battle's shock waves (Wave): three at most, each a ring going out over Ripple seconds.
        const float Ripple = 0.4f;
        readonly Vector4[] waves = new Vector4[3];
        readonly Vector2[] middles = new Vector2[3];
        readonly float[] born = { -1f, -1f, -1f };
        int nextWave;

        public static SeaLens Fit(Camera camera, Look look)
        {
            camera.allowHDR = true;
            var lens = camera.gameObject.AddComponent<SeaLens>();
            lens.look = look;
            lens.lens = SeaArt.Plain("Bloom");
            lens.lens.SetColor("_VignetteColor", Deep);
            lens.eye = camera;
            // The figures' rim and the shadows on the seabed go with the lens, on the same two cameras.
            SeaShade.Fit(camera);
            return lens;
        }

        // A ring bending the picture out from `world` (a weak spot or critical hit, ice bursting); none with 画面震动
        // off, as it moves the picture.
        public void Wave(Vector3 world)
        {
            if (GameRoot.Instance != null && !GameRoot.Instance.Settings.screenShake) return;
            var at = eye.WorldToViewportPoint(world);
            if (at.z < 0f) return;
            middles[nextWave] = at;
            born[nextWave] = Time.unscaledTime;
            nextWave = (nextWave + 1) % waves.Length;
        }

        void OnDestroy()
        {
            if (lens != null) Destroy(lens);
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var knee = look.Threshold * look.Knee;
            // 减弱闪光: every flash past white glows less wide.
            var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
            lens.SetVector("_Curve", new Vector4(look.Threshold - knee, knee * 2f, 0.25f / knee, look.Threshold));
            lens.SetFloat("_Intensity", look.Intensity * (calm ? 0.6f : 1f));
            // The zone's grade and the darker edges of its water (SeaAtmos.Current: the maze's, or the zone a battle was
            // met in); none at all outside a map.
            var air = SeaAtmos.Current;
            lens.SetVector("_Vignette", new Vector4(0.45f, 1.05f, look.Vignette + air.Vignette, 0f));
            lens.SetVector("_GradeGain", air.Graded ? new Vector4(air.Gain.x, air.Gain.y, air.Gain.z, 1f) : Vector4.zero);
            lens.SetFloat("_GradeSaturation", air.Saturation);
            lens.SetColor("_GradeShadow", air.Shadow);
            lens.SetFloat("_WhiteHot", look.WhiteHot);
            for (var k = 0; k < waves.Length; k++)
            {
                var age = (Time.unscaledTime - born[k]) / Ripple;
                var out3 = 1f - (1f - age) * (1f - age) * (1f - age);
                waves[k] = age < 1f ? new Vector4(middles[k].x, middles[k].y, 0.3f * out3, (1f - age) * (1f - age)) : Vector4.zero;
            }
            lens.SetVectorArray("_Waves", waves);
            int w = source.width / 2, h = source.height / 2;
            var count = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Mathf.Min(w, h), 2f)) - 2, 1, look.Levels);
            for (var i = 0; i < count; i++)
            {
                levels[i] = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.DefaultHDR);
                levels[i].filterMode = FilterMode.Bilinear;
                Graphics.Blit(i == 0 ? source : levels[i - 1], levels[i], lens, i == 0 ? Prefilter : Down);
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
            }
            // Back up the chain, each level adding the blurred one under it.
            var last = levels[count - 1];
            for (var i = count - 2; i >= 0; i--)
            {
                var sum = RenderTexture.GetTemporary(levels[i].width, levels[i].height, 0, RenderTextureFormat.DefaultHDR);
                sum.filterMode = FilterMode.Bilinear;
                lens.SetTexture("_BaseTex", levels[i]);
                Graphics.Blit(last, sum, lens, Up);
                if (last != levels[count - 1]) RenderTexture.ReleaseTemporary(last);
                last = sum;
            }
            lens.SetTexture("_BloomTex", last);
            Graphics.Blit(source, destination, lens, Combine);
            if (last != levels[count - 1]) RenderTexture.ReleaseTemporary(last);
            for (var i = 0; i < count; i++)
            {
                RenderTexture.ReleaseTemporary(levels[i]);
                levels[i] = null;
            }
        }
    }
}
