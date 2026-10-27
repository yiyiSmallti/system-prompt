using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt
{
    // 8-bit UI sound effects from pv/sfx and the music (Tools/bgm_loops.py cuts the loops). Clips live in Resources/Audio.
    public sealed class AudioService : MonoBehaviour
    {
        const float FadeOut = 0.3f, FadeIn = 0.8f;

        AudioSource music;
        AudioSource sfx;
        GameSettings settings;
        string currentMusic;
        AudioClip nextMusic; // starts once the playing track has faded out
        float fade = 1f;
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public void Init(GameSettings s)
        {
            settings = s;
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            ApplyVolumes();
        }

        public void ApplyVolumes()
        {
            AudioListener.volume = settings.masterVolume;
            music.volume = settings.musicVolume * fade;
            sfx.volume = settings.sfxVolume;
        }

        public void Sfx(string name, float volume = 1f)
        {
            var clip = Load("Audio/SFX/" + name);
            if (clip != null) sfx.PlayOneShot(clip, volume);
        }

        public void PlayMusic(string name)
        {
            if (currentMusic == name) return; // even paused: a scene change within one track goes on with it
            var clip = Load("Audio/BGM/" + name);
            if (clip == null) return;
            currentMusic = name;
            nextMusic = clip;
        }

        public void StopMusic()
        {
            currentMusic = null;
            nextMusic = null;
            music.Stop();
        }

        void Update()
        {
            if (music == null) return;
            var step = Time.unscaledDeltaTime;
            if (nextMusic != null && music.isPlaying && fade > 0)
                fade = Mathf.Max(0, fade - step / FadeOut);
            else if (nextMusic != null)
            {
                music.clip = nextMusic;
                nextMusic = null;
                fade = 0;
                music.Play();
            }
            else if (fade < 1)
                fade = Mathf.Min(1, fade + step / FadeIn);
            music.volume = settings.musicVolume * fade;
        }

        AudioClip Load(string path)
        {
            if (cache.TryGetValue(path, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(path);
            if (clip == null) Debug.LogError($"找不到音频 Resources/{path}");
            cache[path] = clip;
            return clip;
        }
    }
}
