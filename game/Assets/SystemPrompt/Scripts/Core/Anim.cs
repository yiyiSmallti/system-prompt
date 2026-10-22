using System;
using UnityEngine;

namespace SystemPrompt
{
    public static class Ease
    {
        public static float Linear(float t) => t;
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InCubic(float t) => t * t * t;
        public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

        // Overshoot used for slammed text and pop-ups: 0 -> ~1.1 -> 1.
        public static float OutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }

    // Frame-driven tweens on unscaled time, so UI keeps animating while the game is paused.
    public static class Anim
    {
        public static async Awaitable Tween(float seconds, Action<float> step, Func<float, float> ease = null, Func<bool> cancel = null)
        {
            ease ??= Ease.OutCubic;
            var t = 0f;
            while (t < seconds)
            {
                if (cancel != null && cancel()) break;
                step(ease(Mathf.Clamp01(t / seconds)));
                await Awaitable.NextFrameAsync();
                t += Time.unscaledDeltaTime;
            }
            step(1f);
        }

        public static async Awaitable Wait(float seconds, Func<bool> cancel = null)
        {
            var t = 0f;
            while (t < seconds)
            {
                if (cancel != null && cancel()) return;
                await Awaitable.NextFrameAsync();
                t += Time.unscaledDeltaTime;
            }
        }

        public static async Awaitable Until(Func<bool> condition)
        {
            while (!condition()) await Awaitable.NextFrameAsync();
        }

        // Fire-and-forget that still reports exceptions instead of losing them inside the Awaitable.
        public static async void Forget(this Awaitable awaitable)
        {
            try { await awaitable; }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
