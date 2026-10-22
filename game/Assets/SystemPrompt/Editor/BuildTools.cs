using System;
using System.IO;
using System.Linq;
using UnityEditor;
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

        // The E2E build (game/Tools/e2e.sh) includes E2E/PrologueRun.cs and gets its own product name,
        // so its saves and settings never mix with the real game's.
        [MenuItem("System Prompt/构建端到端测试版")]
        public static void BuildE2E()
        {
            var output = Arg("-buildOutput") ?? "Builds/E2E/SystemPromptE2E.app";
            YarnStorySync.Sync(true);
            EnsureBootScene();
            var product = PlayerSettings.productName;
            var runInBackground = PlayerSettings.runInBackground;
            BuildReport report;
            try
            {
                PlayerSettings.productName = E2EProduct;
                PlayerSettings.runInBackground = true;
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { BootScene },
                    locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    extraScriptingDefines = new[] { "SP_E2E" },
                });
            }
            finally
            {
                PlayerSettings.productName = product;
                PlayerSettings.runInBackground = runInBackground;
                AssetDatabase.SaveAssets();
            }
            var ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log($"E2E 构建{(ok ? "完成" : "失败")}：{report.summary.result}，{report.summary.totalErrors} 个错误，输出 {output}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

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
