using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt
{
    // 8-bit UI sound effects from pv/sfx and the music (Tools/bgm_loops.py cuts the loops). Clips live in Resources/Audio.
    // A track is a loop, BGM/<name>; one with BGM/<name>_intro beside it plays that once first (the song up to
    // where its loop starts) and the loop takes over on the sample it ends on.
    // Voiced lines are Voice/<line id> (Tools/voice_lines.py makes them); the music dips a little under a voice.
    public sealed class AudioService : MonoBehaviour
    {
        const float FadeOut = 0.3f, FadeIn = 0.8f;
        const float VoiceDuck = 0.7f, DuckSpeed = 4f;

        AudioSource music;
        AudioSource intro;
        AudioSource sfx;
        AudioSource voice;
        GameSettings settings;
        string currentMusic;
        AudioClip nextMusic; // starts once the playing track has faded out
        AudioClip nextIntro; // its lead-in, if it has one
        float fade = 1f;
        float duck = 1f;
        float voiceLevel = 1f; // the line's own level (a whisper is quieter)
        AudioClip pendingVoice; // 已经要放、但声音数据还在后台解码的配音，解码完在 Update 里开始放
        string pendingSpeaker;
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
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            ApplyVolumes();
        }

        public void ApplyVolumes()
        {
            AudioListener.volume = settings.masterVolume;
            music.volume = intro.volume = settings.musicVolume * fade * duck;
            sfx.volume = settings.sfxVolume;
            voice.volume = settings.voiceVolume * voiceLevel;
        }

        // ------------------------------------------------------------------ voice

        // A voiced line starting (speaker, clip name); the end-to-end run counts them.
        public event Action<string, string> VoiceStarted;

        // The voice of a line by its Yarn id ("line:1a2b3c4d"), or null for a line without one (most aren't voiced).
        // 配音按载入时解压、在后台载入导入（Editor/UiAssetImporter.cs）：这里一拿到台词就开始在后台解码，
        // 不等 Play 时在主线程上同步解码（那样每句配音开头卡 10–70 ms）。
        public static AudioClip VoiceFor(string lineId)
        {
            if (string.IsNullOrEmpty(lineId) || !lineId.StartsWith("line:", StringComparison.Ordinal)) return null;
            var clip = Resources.Load<AudioClip>("Audio/Voice/" + lineId.Substring(5));
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            return clip;
        }

        public void PlayVoice(AudioClip clip, string speaker, float level = 1f)
        {
            if (clip == null) return;
            voice.Stop();
            voiceLevel = level;
            voice.volume = settings.voiceVolume * voiceLevel;
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if (clip.loadState == AudioDataLoadState.Loaded) StartVoice(clip, speaker);
            else
            {
                // 还在后台解码：先记下，解码完再放（通常就晚一两帧）；这段时间里 Voicing 仍算在念。
                pendingVoice = clip;
                pendingSpeaker = speaker;
            }
        }

        void StartVoice(AudioClip clip, string speaker)
        {
            pendingVoice = null;
            voice.clip = clip;
            voice.Play();
            VoiceStarted?.Invoke(speaker, clip.name);
        }

        // Whether this clip is still being spoken (or about to be, its data still decoding).
        public bool Voicing(AudioClip clip) => clip != null && (pendingVoice == clip || (voice.isPlaying && voice.clip == clip));

        // Cuts a line's voice off when the line is left. Only that clip: the next line's voice may already be on,
        // and a line without a voice (null) stops nothing.
        public void StopVoice(AudioClip clip)
        {
            if (clip == null) return;
            if (pendingVoice == clip) pendingVoice = null;
            if (voice.clip == clip) voice.Stop();
        }

        // Silences whatever is being said (the story is stopped).
        public void StopVoice()
        {
            pendingVoice = null;
            voice.Stop();
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
            if (pendingVoice != null)
            {
                var state = pendingVoice.loadState;
                if (state == AudioDataLoadState.Loaded) StartVoice(pendingVoice, pendingSpeaker);
                else if (state == AudioDataLoadState.Failed)
                {
                    Debug.LogError($"配音解码失败：{pendingVoice.name}");
                    pendingVoice = null;
                }
            }
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
            duck = Mathf.MoveTowards(duck, voice.isPlaying ? VoiceDuck : 1f, step * DuckSpeed);
            music.volume = intro.volume = settings.musicVolume * fade * duck;
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
