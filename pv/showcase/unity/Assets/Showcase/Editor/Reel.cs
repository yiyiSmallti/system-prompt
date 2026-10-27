using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Showcase
{
    // 一镜：Frame(t) 把场景、人物和镜头摆到这一镜的第 t 秒，只看 t，不依赖上一帧，所以可以跳着取样。
    abstract class Shot
    {
        public readonly string Id;
        public readonly float Duration;
        // 交给 compose.py 叠的字幕（时间是镜内的秒）。
        public readonly List<Note> Notes = new List<Note>();

        protected Shot(string id, float duration)
        {
            Id = id;
            Duration = duration;
        }

        // 进镜时：只显示这一镜的人物，换主题色。
        public abstract void Begin(Stage stage);
        public abstract void Frame(float t, Camera camera);

        // 分屏：画面切成几格，每格一个镜头（Aim 摆好再渲染）。1 格就是 Frame 摆好的整个画面。
        public virtual int Cells => 1;
        public virtual Rect Cell(int i) => new Rect(0f, 0f, 1f, 1f);
        public virtual void Aim(int cell, float t, Camera camera) { }

        protected Note Note(string kind, float start, float end, params (string key, object value)[] fields)
        {
            var n = new Note { Kind = kind, Start = start, End = end };
            foreach (var (k, v) in fields) n.Fields[k] = v;
            Notes.Add(n);
            return n;
        }
    }

    sealed class Note
    {
        public string Kind;
        public float Start, End;
        public readonly Dictionary<string, object> Fields = new Dictionary<string, object>();
        // 跟着画面里某一点的字幕：第 AnchorAt 秒时 Anchor() 给出的世界坐标，换算成画面上的像素。
        public Func<Vector3> Anchor;
        public float AnchorAt;

        public Note At(float time, Func<Vector3> anchor)
        {
            AnchorAt = time;
            Anchor = anchor;
            return this;
        }
    }

    // 模型与动作展示片的渲染。场景全用代码搭，逐帧推进，先按两倍大小渲染再缩一半（加上 4 倍 MSAA），
    // 原始 RGB 帧直接灌给 ffmpeg 编成无损的中间视频，一镜一个文件；字幕和配乐由 compose.py 叠。用法见 render.sh：
    //   Unity -batchmode -quit -projectPath pv/showcase/unity -executeMethod Showcase.Reel.Run -out <目录> [-shots a,b] [-stills]
    public static class Reel
    {
        public const int Width = 1920, Height = 1080, Fps = 60;
        const int Super = 2;

        public static void Run()
        {
            try
            {
                Shoot();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

        static Camera Setup()
        {
            // 母作按 Gamma 色彩空间调的颜色，这里保持一致。
            if (PlayerSettings.colorSpace != ColorSpace.Gamma) PlayerSettings.colorSpace = ColorSpace.Gamma;
            EditorSettings.asyncShaderCompilation = false;
            ShaderUtil.allowAsyncCompilation = false;
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.lodBias = 4f;
            // 写实光照的软阴影：只投在人物和道具自己身上（地面是假阴影，Stage.Shadow），近处要细。
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.CloseFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 30f;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.renderingPath = RenderingPath.Forward;
            return camera;
        }

        static void Shoot()
        {
            var output = Arg("-out") ?? throw new ArgumentException("用 -out <目录> 指定输出目录");
            var only = Arg("-shots")?.Split(',');
            var stills = Flag("-stills");
            var ffmpeg = Arg("-ffmpeg") ?? "/opt/homebrew/bin/ffmpeg";
            Directory.CreateDirectory(output);
            var camera = Setup();
            var big = new RenderTexture(Width * Super, Height * Super, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, filterMode = FilterMode.Bilinear };
            var small = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            var read = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            camera.targetTexture = big;
            camera.aspect = Width / (float)Height;

            var stage = new Stage();
            var cast = new Cast(stage.Root);
            var shots = Shots.All(cast);
            var unknown = only?.Where(id => shots.All(s => s.Id != id)).ToArray();
            if (unknown != null && unknown.Length > 0) throw new ArgumentException("没有这几镜：" + string.Join(",", unknown));

            void Grab(Shot shot, float t)
            {
                if (shot.Cells == 1)
                {
                    stage.Light(camera);
                    camera.Render();
                }
                else
                    for (var c = 0; c < shot.Cells; c++)
                    {
                        var r = shot.Cell(c);
                        camera.rect = r;
                        camera.aspect = r.width * Width / (r.height * Height);
                        shot.Aim(c, t, camera);
                        stage.Light(camera);
                        camera.Render();
                    }
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = Width / (float)Height;
                Graphics.Blit(big, small);
                RenderTexture.active = small;
                read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                read.Apply(false);
                RenderTexture.active = null;
            }

            var buffer = new byte[Width * Height * 3];

            // 把一镜灌给 ffmpeg，编成无损 RGB；ReadPixels 的行从下往上，交给 ffmpeg 翻过来。ffmpeg 中途退出返回 false。
            bool Encode(Shot shot, int frames, string file)
            {
                var info = new ProcessStartInfo(ffmpeg,
                    $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgb24 -s {Width}x{Height} -r {Fps} -i - -vf vflip -c:v libx264rgb -qp 0 -preset ultrafast \"{file}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                };
                using (var encoder = Process.Start(info))
                {
                    var pipe = encoder.StandardInput.BaseStream;
                    try
                    {
                        for (var f = 0; f < frames; f++)
                        {
                            shot.Frame(f / (float)Fps, camera);
                            Grab(shot, f / (float)Fps);
                            read.GetRawTextureData<byte>().CopyTo(buffer);
                            pipe.Write(buffer, 0, buffer.Length);
                        }
                        pipe.Close();
                    }
                    catch (IOException e)
                    {
                        Debug.LogWarning($"{shot.Id}：写入 ffmpeg 失败：{e.Message}");
                        try { pipe.Dispose(); } catch (IOException) { }
                    }
                    encoder.WaitForExit();
                    return encoder.ExitCode == 0 && File.Exists(file);
                }
            }

            var timeline = new StringBuilder("{\n  \"fps\": " + Fps + ", \"width\": " + Width + ", \"height\": " + Height + ",\n  \"shots\": [\n");
            for (var s = 0; s < shots.Count; s++)
            {
                var shot = shots[s];
                var frames = Mathf.RoundToInt(shot.Duration * Fps);
                stage.HideProps();
                shot.Begin(stage);
                // 字幕的锚点：先跳到那一刻摆好，再把世界坐标换成画面像素。
                foreach (var note in shot.Notes.Where(n => n.Anchor != null))
                {
                    shot.Frame(note.AnchorAt, camera);
                    var v = camera.WorldToViewportPoint(note.Anchor());
                    note.Fields["x"] = v.x * Width;
                    note.Fields["y"] = (1f - v.y) * Height;
                }
                timeline.Append("    {\"id\": ").Append(Json.Str(shot.Id)).Append(", \"frames\": ").Append(frames).Append(", \"notes\": [");
                timeline.Append(string.Join(",", shot.Notes.Select(n => "\n      " + Json.Note(n))));
                timeline.Append(shot.Notes.Count > 0 ? "\n    ]}" : "]}").Append(s + 1 < shots.Count ? ",\n" : "\n");

                if (only != null && !only.Contains(shot.Id)) continue;
                var watch = Stopwatch.StartNew();
                if (stills)
                {
                    foreach (var at in new[] { 0f, 0.25f, 0.5f, 0.75f, 0.999f })
                    {
                        shot.Frame(at * shot.Duration, camera);
                        Grab(shot, at * shot.Duration);
                        File.WriteAllBytes(Path.Combine(output, $"{shot.Id}-{Mathf.RoundToInt(at * 100):000}.png"), read.EncodeToPNG());
                    }
                    Debug.Log($"{shot.Id}：预览图 → {output}");
                    continue;
                }
                var file = Path.Combine(output, shot.Id + ".mkv");
                // 外置盘上 ffmpeg 打开输出文件偶尔会被信号打断（Interrupted system call）而退出，管道随之断开。
                // 每镜的画面只由时间决定，编码失败就整镜重来。
                for (var attempt = 1; !Encode(shot, frames, file); attempt++)
                {
                    if (File.Exists(file)) File.Delete(file);
                    if (attempt == 3) throw new InvalidOperationException($"ffmpeg 编码 {shot.Id} 连续失败 {attempt} 次");
                    Debug.LogWarning($"{shot.Id}：ffmpeg 编码失败，第 {attempt + 1} 次重渲");
                }
                Debug.Log($"{shot.Id}：{frames} 帧，{watch.Elapsed.TotalSeconds:F0} 秒，进程内存 {Process.GetCurrentProcess().WorkingSet64 >> 20} MB → {file}");
            }
            timeline.Append("  ]\n}\n");
            if (!stills) File.WriteAllText(Path.Combine(output, "timeline.json"), timeline.ToString());
            cast.Dispose();
        }

        // 一段动作在一个模型上均匀取 n 帧，横排成一张图，用来看动作本身。
        //   Unity ... -executeMethod Showcase.Reel.Strip -model PlayerMale -motion Run -n 10 -out strip.png
        public static void Strip()
        {
            try
            {
                var camera = Setup();
                var stage = new Stage();
                stage.Use(Palette.Studio, Vector3.zero);
                var model = Arg("-model") ?? "PlayerMale";
                var motion = Arg("-motion") ?? "Run";
                var n = int.Parse(Arg("-n") ?? "10");
                var entry = Cast.Models.First(e => e.Model == model);
                var f = new Figure(model, entry.Height, stage.Root);
                const int w = 480, h = 640;
                var rt = new RenderTexture(w * 2, h * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var half = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
                var read = new Texture2D(w, h, TextureFormat.RGB24, false);
                var sheet = new Texture2D(w * n, h, TextureFormat.RGB24, false);
                camera.targetTexture = rt;
                camera.aspect = w / (float)h;
                camera.fieldOfView = 34f;
                camera.transform.position = new Vector3(3.2f, 1.0f, 3.2f);
                camera.transform.LookAt(new Vector3(0f, 0.8f, 0f));
                var length = f.Length(motion);
                for (var i = 0; i < n; i++)
                {
                    f.Pose((motion, length * i / n, 1f));
                    f.Commit();
                    camera.Render();
                    Graphics.Blit(rt, half);
                    RenderTexture.active = half;
                    read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                    RenderTexture.active = null;
                    sheet.SetPixels(i * w, 0, w, h, read.GetPixels());
                }
                File.WriteAllBytes(Arg("-out") ?? "strip.png", sheet.EncodeToPNG());
                f.Dispose();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // 每段动作的长度、是否循环、腰在一段里走了多远、身体最低点，以及手脚伸得最远的时刻（出招命中的时机）。
        //   Unity -batchmode -quit -projectPath pv/showcase/unity -executeMethod Showcase.Reel.Probe
        public static void Probe()
        {
            try
            {
                Setup();
                var stage = new Stage();
                var f = new Figure("PlayerFemaleBattle", 1.62f, stage.Root);
                var report = new StringBuilder("动作探测（PlayerFemaleBattle，1.62 米）\n");
                var names = Directory.GetFiles("Assets/SystemPrompt/Resources/Models/Motions", "*.fbx").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
                names.Insert(0, Figure.Own);
                var ends = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
                var low = new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head, HumanBodyBones.Hips };
                foreach (var name in names)
                {
                    var length = f.Length(name);
                    Vector3 start = default, end = default;
                    float minY = float.MaxValue, maxHip = float.MinValue, minHip = float.MaxValue, reach = 0f, reachAt = 0f;
                    var reachBone = "";
                    for (var t = 0f; t <= length + 1e-4f; t += 1f / 30f)
                    {
                        f.Pose((name, t, 1f));
                        var hips = f.Root.InverseTransformPoint(f.Hips);
                        if (t == 0f) start = hips;
                        end = hips;
                        maxHip = Mathf.Max(maxHip, hips.y);
                        minHip = Mathf.Min(minHip, hips.y);
                        foreach (var b in low) minY = Mathf.Min(minY, f.Root.InverseTransformPoint(f.Bone(b).position).y);
                        foreach (var b in ends)
                        {
                            var p = f.Root.InverseTransformPoint(f.Bone(b).position) - hips;
                            var d = new Vector2(p.x, p.z).magnitude;
                            if (d > reach) { reach = d; reachAt = t; reachBone = b.ToString(); }
                        }
                    }
                    var move = end - start;
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-18} {1,5:F2}s {2,-4} 腰 起({3:F2},{4:F2},{5:F2}) 移({6:F2},{7:F2}) 高 {8:F2}–{9:F2} 最低 {10:F2} 最远 {11} {12:F2}m@{13:F2}s",
                        name, length, f.Loops(name) ? "循环" : "一次", start.x, start.y, start.z, move.x, move.z, minHip, maxHip, minY, reachBone, reach, reachAt));
                }
                Debug.Log(report.ToString());
                f.Dispose();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }

    static class Json
    {
        public static string Str(string s)
        {
            var b = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c == '\n') b.Append("\\n");
                else if (c < ' ') b.AppendFormat("\\u{0:x4}", (int)c);
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        static string Value(object v) => v switch
        {
            null => "null",
            string s => Str(s),
            bool b => b ? "true" : "false",
            float f => f.ToString("0.###", CultureInfo.InvariantCulture),
            double d => d.ToString("0.###", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            IEnumerable<string> list => "[" + string.Join(", ", list.Select(Str)) + "]",
            _ => throw new ArgumentException($"字幕字段不认识的类型 {v.GetType()}"),
        };

        public static string Note(Note n)
        {
            var parts = new List<string> { "\"kind\": " + Str(n.Kind), "\"start\": " + Value(n.Start), "\"end\": " + Value(n.End) };
            parts.AddRange(n.Fields.Select(kv => Str(kv.Key) + ": " + Value(kv.Value)));
            return "{" + string.Join(", ", parts) + "}";
        }
    }
}
