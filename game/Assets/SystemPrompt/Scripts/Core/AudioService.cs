using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt
{
    // 8-bit UI sound effects from pv/sfx and the title music. Clips live in Resources/Audio.
    public sealed class AudioService : MonoBehaviour
    {
        AudioSource music;
        AudioSource sfx;
        GameSettings settings;
        string currentMusic;
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
            music.volume = settings.musicVolume;
            sfx.volume = settings.sfxVolume;
        }

        public void Sfx(string name, float volume = 1f)
        {
            var clip = Load("Audio/SFX/" + name);
            if (clip != null) sfx.PlayOneShot(clip, volume);
        }

        public void PlayMusic(string name)
        {
            if (currentMusic == name && music.isPlaying) return;
            var clip = Load("Audio/BGM/" + name);
            if (clip == null) return;
            currentMusic = name;
            music.clip = clip;
            music.Play();
        }

        public void StopMusic()
        {
            currentMusic = null;
            music.Stop();
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
