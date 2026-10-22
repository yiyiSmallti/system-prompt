using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // story/ stays the single home of the Yarn scripts (see README), but Unity only imports files
    // under Assets/. Mirror them into a git-ignored folder next to Prologue.yarnproject.
    [InitializeOnLoad]
    static class YarnStorySync
    {
        const string TargetDir = "Assets/SystemPrompt/Resources/Story/Synced";

        static YarnStorySync()
        {
            EditorApplication.delayCall += () => Sync(false);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Sync(false);
            };
        }

        [MenuItem("System Prompt/同步剧本")]
        static void SyncFromMenu() => Sync(true);

        // Also called by BuildTools before a build: in batchmode the delayCall above may never run.
        internal static void Sync(bool verbose)
        {
            var sourceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../story"));
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogError($"找不到剧本目录：{sourceDir}");
                return;
            }

            Directory.CreateDirectory(TargetDir);
            var changed = false;
            var wanted = new HashSet<string>();
            foreach (var src in Directory.GetFiles(sourceDir, "*.yarn"))
            {
                var name = Path.GetFileName(src);
                wanted.Add(name);
                var dst = Path.Combine(TargetDir, name);
                var bytes = File.ReadAllBytes(src);
                if (File.Exists(dst) && SameBytes(File.ReadAllBytes(dst), bytes)) continue;
                File.WriteAllBytes(dst, bytes);
                changed = true;
            }

            foreach (var dst in Directory.GetFiles(TargetDir, "*.yarn"))
            {
                if (wanted.Contains(Path.GetFileName(dst))) continue;
                AssetDatabase.DeleteAsset(dst.Replace('\\', '/'));
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.Refresh();
                // A .yarnproject only lists the scripts it saw at its last import; make it pick up new ones.
                foreach (var project in Directory.GetFiles(Path.GetDirectoryName(TargetDir), "*.yarnproject"))
                    AssetDatabase.ImportAsset(project.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            }
            if (verbose) Debug.Log(changed ? "剧本已从 story/ 同步。" : "剧本没有变化。");
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
