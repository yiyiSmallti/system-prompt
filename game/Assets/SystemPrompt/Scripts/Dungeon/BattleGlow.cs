using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The battle camera's glow (Shaders/Bloom.shader): the field is drawn in HDR so the effects' light can go
    // past white, and what does spills into a halo built down and back up a chain of halved copies, then the
    // whole rolls off into white. The scene stays under the threshold, so only the effects glow.
    [RequireComponent(typeof(Camera))]
    sealed class BattleGlow : MonoBehaviour
    {
        const float Threshold = 1.2f, Knee = 0.5f, Intensity = 0.6f;
        const int MaxLevels = 7;
        const int Prefilter = 0, Down = 1, Up = 2, Combine = 3;

        Material look;
        readonly RenderTexture[] levels = new RenderTexture[MaxLevels];

        void Awake()
        {
            GetComponent<Camera>().allowHDR = true;
            look = SeaArt.Plain("Bloom");
        }

        void OnDestroy()
        {
            if (look != null) Destroy(look);
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var knee = Threshold * Knee;
            look.SetVector("_Curve", new Vector4(Threshold - knee, knee * 2f, 0.25f / knee, Threshold));
            look.SetFloat("_Intensity", Intensity);
            int w = source.width / 2, h = source.height / 2;
            var count = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Mathf.Min(w, h), 2f)) - 2, 1, MaxLevels);
            for (var i = 0; i < count; i++)
            {
                levels[i] = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.DefaultHDR);
                levels[i].filterMode = FilterMode.Bilinear;
                Graphics.Blit(i == 0 ? source : levels[i - 1], levels[i], look, i == 0 ? Prefilter : Down);
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
            }
            // Back up the chain, each level adding the blurred one under it.
            var last = levels[count - 1];
            for (var i = count - 2; i >= 0; i--)
            {
                var sum = RenderTexture.GetTemporary(levels[i].width, levels[i].height, 0, RenderTextureFormat.DefaultHDR);
                sum.filterMode = FilterMode.Bilinear;
                look.SetTexture("_BaseTex", levels[i]);
                Graphics.Blit(last, sum, look, Up);
                if (last != levels[count - 1]) RenderTexture.ReleaseTemporary(last);
                last = sum;
            }
            look.SetTexture("_BloomTex", last);
            Graphics.Blit(source, destination, look, Combine);
            if (last != levels[count - 1]) RenderTexture.ReleaseTemporary(last);
            for (var i = 0; i < count; i++)
            {
                RenderTexture.ReleaseTemporary(levels[i]);
                levels[i] = null;
            }
        }
    }
}
