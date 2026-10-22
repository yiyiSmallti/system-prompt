using UnityEngine;

namespace SystemPrompt
{
    // The project has no scene of its own yet: pressing Play in any scene (even an empty one) boots the game.
    // Remove this once a boot scene with a GameRoot exists.
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (GameRoot.Instance != null) return;

            var camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                var go = new GameObject("UI Camera");
                camera = go.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 0;
                Object.DontDestroyOnLoad(go);
            }
            if (Object.FindFirstObjectByType<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();

            new GameObject("System Prompt").AddComponent<GameRoot>();
        }
    }
}
