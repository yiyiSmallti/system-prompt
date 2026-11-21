using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // Player builds. The game only needs an empty boot scene: GameBootstrap creates everything at runtime.
    public static class BuildTools
    {
        const string BootScene = "Assets/SystemPrompt/Scenes/Boot.unity";
        const string E2EProduct = "System Prompt E2E";
        const string ReleaseProduct = "System Prompt";

        // The E2E build (game/Tools/e2e.sh) includes E2E/PrologueRun.cs and gets its own product name,
        // so its saves and settings never mix with the real game's.
        [MenuItem("System Prompt/构建端到端测试版")]
        public static void BuildE2E()
        {
            var output = Arg("-buildOutput") ?? "Builds/E2E/SystemPromptE2E.app";
            // -development 构建开发版：E2E/HitchProbe.cs 要靠它读到 Profiler 标记的计时，正常的端到端验证不带它。
            var options = Array.IndexOf(Environment.GetCommandLineArgs(), "-development") >= 0 ? BuildOptions.Development : BuildOptions.None;
            var ok = Build(BuildTarget.StandaloneOSX, output, E2EProduct, true, new[] { "SP_E2E" }, options: options);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        // Release builds for distribution (game/Tools/release.sh). No SP_E2E define, so the E2E driver,
        // the visual lab and the tour recorder are compiled out; the window pauses when it loses focus.
        [MenuItem("System Prompt/构建正式版/macOS")]
        public static void BuildReleaseMac() => BuildRelease(BuildTarget.StandaloneOSX);

        [MenuItem("System Prompt/构建正式版/Windows")]
        public static void BuildReleaseWindows() => BuildRelease(BuildTarget.StandaloneWindows64);

        static void BuildRelease(BuildTarget target)
        {
            var ok = false;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                Debug.LogError($"正式版构建失败：没装 {target} 的构建模块。在 Unity Hub 里给 {Application.unityVersion} 加上"
                               + (target == BuildTarget.StandaloneOSX ? " Mac Build Support (Mono)" : " Windows Build Support (Mono)"));
            else
            {
                var output = Arg("-buildOutput") ?? DefaultReleaseOutput(target);
                ok = Build(target, output, ReleaseProduct, false, Array.Empty<string>(), ReleaseIdentity);
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string DefaultReleaseOutput(BuildTarget target) => target == BuildTarget.StandaloneOSX
            ? $"Builds/Release/macOS/{ReleaseProduct}.app"
            // A Windows player is a folder: the .exe, UnityPlayer.dll, <name>_Data and MonoBleedingEdge.
            : $"Builds/Release/Windows/{ReleaseProduct}.exe";

        // Company name and bundle id decide where saves and settings live (macOS: ~/Library/Application Support/<bundle id>,
        // Windows: %USERPROFILE%\AppData\LocalLow\<company>\<product>). Once players have saves they must not change.
        // They are set only for the duration of a release build: the project keeps Unity's derived id, so the E2E build
        // stays com.DefaultCompany.System-Prompt-E2E and e2e.sh, which wipes that folder, never touches release saves.
        static readonly (string Company, string BundleId) ReleaseIdentity = ("System Prompt", "com.systemprompt.game");

        static bool Build(BuildTarget target, string output, string product, bool runInBackground, string[] defines,
                          (string Company, string BundleId)? identity = null, BuildOptions options = BuildOptions.None)
        {
            YarnStorySync.Sync(true);
            EnsureBootScene();
            var savedProduct = PlayerSettings.productName;
            var savedRunInBackground = PlayerSettings.runInBackground;
            var savedCompany = PlayerSettings.companyName;
            var savedId = StoredStandaloneId();
            var savedOverride = OverrideStandaloneId;
            BuildReport report;
            try
            {
                PlayerSettings.productName = product;
                PlayerSettings.runInBackground = runInBackground;
                if (identity is { } id)
                {
                    PlayerSettings.companyName = id.Company;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, id.BundleId);
                    OverrideStandaloneId = true;
                }
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { BootScene },
                    locationPathName = output,
                    target = target,
                    extraScriptingDefines = defines,
                    options = options,
                });
            }
            finally
            {
                PlayerSettings.productName = savedProduct;
                PlayerSettings.runInBackground = savedRunInBackground;
                PlayerSettings.companyName = savedCompany;
                RestoreStandaloneId(savedId);
                OverrideStandaloneId = savedOverride;
                AssetDatabase.SaveAssets();
            }
            var s = report.summary;
            var ok = s.result == BuildResult.Succeeded;
            Debug.Log($"{product} {target} 构建{(ok ? "完成" : "失败")}：{s.result}，{s.totalErrors} 个错误，"
                      + $"{s.totalSize / 1048576f:F0} MB，用时 {s.totalTime:mm\\:ss}，输出 {output}");
            return ok;
        }

        // Player Settings' "Override Default Bundle Identifier" for macOS. Off, Unity derives com.<company>.<product> and
        // ignores the Standalone identifier; there is no public API for it, so it goes through the serialized settings.
        static bool OverrideStandaloneId
        {
            get => PlayerSettingsObject().FindProperty("overrideDefaultApplicationIdentifier").boolValue;
            set
            {
                var so = PlayerSettingsObject();
                so.FindProperty("overrideDefaultApplicationIdentifier").boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // The Standalone entry of the serialized applicationIdentifier map, or null when the project has none.
        // GetApplicationIdentifier returns the derived com.<company>.<product> for a missing entry, so restoring
        // through it would write that derived id into ProjectSettings after every release build.
        static string StoredStandaloneId()
        {
            var map = PlayerSettingsObject().FindProperty("applicationIdentifier");
            for (var i = 0; i < map.arraySize; i++)
            {
                var pair = map.GetArrayElementAtIndex(i);
                if (pair.FindPropertyRelative("first").stringValue == "Standalone")
                    return pair.FindPropertyRelative("second").stringValue;
            }
            return null;
        }

        static void RestoreStandaloneId(string id)
        {
            if (id != null)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, id);
                return;
            }
            var so = PlayerSettingsObject();
            var map = so.FindProperty("applicationIdentifier");
            for (var i = map.arraySize - 1; i >= 0; i--)
                if (map.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "Standalone")
                    map.DeleteArrayElementAtIndex(i);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static SerializedObject PlayerSettingsObject() =>
            new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));

        static void EnsureBootScene()
        {
            if (!File.Exists(BootScene))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BootScene));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, BootScene);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == BootScene))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
