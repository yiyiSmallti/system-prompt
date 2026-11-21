#if UNITY_EDITOR || SP_E2E
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace SystemPrompt.E2E
{
    // 卡顿探针：带 `-hitches <tsv 路径>` 启动测试版时，给播放器里所有计时的 Profiler 标记各挂一个 ProfilerRecorder，
    // 某一帧的墙上时间超过阈值时，把这一帧里用时最多的标记、GC 次数和当时的场合（剧情、潜空间、战斗、曲目）写一行。
    // 标记的计时只有开发版（BuildTools.BuildE2E 带 -development）才有；非开发版只能记到帧时间和 GC 次数。
    // 只编进编辑器和带 SP_E2E 的测试版，和端到端验证（-e2e）一起用，用来查运行时偶发的短暂卡顿是哪段代码。
    public sealed class HitchProbe : MonoBehaviour
    {
        const float ThresholdMs = 40f; // 60 帧下超过 2.4 帧才算一次卡顿
        const int Top = 24;

        readonly List<(string name, ProfilerRecorder rec)> recorders = new List<(string, ProfilerRecorder)>();
        readonly HashSet<string> known = new HashSet<string>();
        readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
        // 各场合的帧数、GC 分配字节数（GC Allocated In Frame，非开发版可能没有）、GC 次数和超阈值的帧数，退出时写在文件末尾。
        readonly Dictionary<string, (long frames, long bytes, int gcs, int hitches)> totals = new Dictionary<string, (long, long, int, int)>();
        ProfilerRecorder gcAlloc;
        StreamWriter tsv;
        int lastGc;
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-hitches");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("E2E Hitch Probe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<HitchProbe>();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[i + 1])));
            probe.tsv = new StreamWriter(args[i + 1], false, new UTF8Encoding(false)) { AutoFlush = true };
            probe.tsv.WriteLine("time\tframe\tms\tgc\tcontext\ttop markers (ms)");
        }

        void Start()
        {
            Scan();
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            lastGc = GC.CollectionCount(0);
            Debug.Log($"卡顿探针：{recorders.Count} 个计时标记，开发版={Debug.isDebugBuild}，GC 分配计数={gcAlloc.Valid}");
        }

        // 标记是第一次用到时才注册的（脚本里的、着色器编译的），隔一会儿补上新出现的。
        void Scan()
        {
            handles.Clear();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || !known.Add(d.Name)) continue;
                var rec = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
                if (!rec.IsRunning) rec.Start();
                recorders.Add((d.Name, rec));
            }
        }

        void Update()
        {
            var ms = Time.unscaledDeltaTime * 1000f;
            var gc = GC.CollectionCount(0);
            var where = Where();
            var hitch = ms > ThresholdMs && Time.frameCount > 5;
            totals.TryGetValue(where, out var t);
            totals[where] = (t.frames + 1, t.bytes + (gcAlloc.Valid ? gcAlloc.LastValue : 0), t.gcs + gc - lastGc, t.hitches + (hitch ? 1 : 0));
            if (hitch)
            {
                // 这一帧读到的 LastValue 是上一帧的，和 unscaledDeltaTime 量的是同一帧。
                var top = recorders.Select(r => (r.name, ms: r.rec.LastValue / 1e6))
                    .Where(r => r.ms >= 1.0).OrderByDescending(r => r.ms).Take(Top)
                    .Select(r => $"{r.name}={r.ms:F1}");
                tsv.WriteLine($"{Time.realtimeSinceStartup:F2}\t{Time.frameCount - 1}\t{ms:F0}\t{gc - lastGc}\t{Context()}\t{string.Join(" | ", top)}");
            }
            lastGc = gc;
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + 10f;
                Scan();
            }
        }

        static string Where()
        {
            var game = GameRoot.Instance;
            if (game == null) return "boot";
            return game.InBattle ? "battle" : game.InDungeon ? "dungeon" : game.InStory ? "story" : "title";
        }

        static string Context() => $"{Where()} {GameRoot.Instance?.Audio?.Music}";

        void OnApplicationQuit()
        {
            if (tsv == null) return;
            tsv.WriteLine("# 场合\t帧数\t超阈值帧\tGC 次数\t每帧 GC 分配 (KB)");
            foreach (var kv in totals)
                tsv.WriteLine($"# {kv.Key}\t{kv.Value.frames}\t{kv.Value.hitches}\t{kv.Value.gcs}\t{kv.Value.bytes / 1024.0 / Math.Max(1, kv.Value.frames):F1}");
            tsv.Flush();
        }

        void OnDestroy()
        {
            foreach (var r in recorders) r.rec.Dispose();
            gcAlloc.Dispose();
            tsv?.Dispose();
            tsv = null;
        }
    }
}
#endif
