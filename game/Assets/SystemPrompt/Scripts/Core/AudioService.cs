using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt
{
    // 8-bit UI sound effects from pv/sfx and the music (Tools/bgm_loops.py cuts the loops). Clips live in Resources/Audio.
    // A track is a loop, BGM/<name>; one with BGM/<name>_intro beside it plays that once first (the song up to
    // where its loop starts) and the loop takes over on the sample it ends on.
    public sealed class AudioService : MonoBehaviour
    {
        const float FadeOut = 0.3f, FadeIn = 0.8f;

        AudioSource music;
        AudioSource intro;
        AudioSource sfx;
        GameSettings settings;
        string currentMusic;
        AudioClip nextMusic; // starts once the playing track has faded out
        AudioClip nextIntro; // its lead-in, if it has one
        float fade = 1f;
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public void Init(GameSettings s)
        {
            settings = s;
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            intro = gameObject.AddComponent<AudioSource>();
            intro.playOnAwake = false;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            ApplyVolumes();
        }

        public void ApplyVolumes()
        {
            AudioListener.volume = settings.masterVolume;
            music.volume = intro.volume = settings.musicVolume * fade;
            sfx.volume = settings.sfxVolume;
        }

        // The track that is playing or about to, by name; null when there is none.
        public string Music => currentMusic;

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
            nextIntro = Resources.Load<AudioClip>("Audio/BGM/" + name + "_intro");
        }

        public void StopMusic()
        {
            currentMusic = null;
            nextMusic = null;
            nextIntro = null;
            music.Stop();
            intro.Stop();
        }

        void Update()
        {
            if (music == null) return;
            var step = Time.unscaledDeltaTime;
            if (nextMusic != null && (music.isPlaying || intro.isPlaying) && fade > 0)
                fade = Mathf.Max(0, fade - step / FadeOut);
            else if (nextMusic != null)
            {
                music.Stop();
                intro.Stop();
                music.clip = nextMusic;
                if (nextIntro != null)
                {
                    // A lead-in opens on its own first note (a fanfare, a hit), so it comes in at full level.
                    var at = AudioSettings.dspTime + 0.1;
                    intro.clip = nextIntro;
                    intro.PlayScheduled(at);
                    music.PlayScheduled(at + (double)nextIntro.samples / nextIntro.frequency);
                    fade = 1;
                }
                else
                {
                    fade = 0;
                    music.Play();
                }
                nextMusic = null;
                nextIntro = null;
            }
            else if (fade < 1)
                fade = Mathf.Min(1, fade + step / FadeIn);
            music.volume = intro.volume = settings.musicVolume * fade;
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
