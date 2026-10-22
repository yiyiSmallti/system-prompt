using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace SystemPrompt.EditorTools
{
    // The Cubism SDK isn't in the repository (its Core may not be redistributed): everyone imports it from
    // live2d.com into Assets/Live2D. This keeps the SP_LIVE2D define in step with that folder, so the
    // Live2D portrait code compiles only when the SDK is there and everything else falls back to the PNGs.
    // It lives in its own assembly so it still runs when the game scripts fail to compile because the SDK
    // was removed while the define was on.
    [InitializeOnLoad]
    static class Live2DDefine
    {
        const string Define = "SP_LIVE2D";
        const string SdkAssembly = "Assets/Live2D/Cubism/Live2D.Cubism.asmdef";

        static Live2DDefine()
        {
            var target = NamedBuildTarget.Standalone;
            var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0).ToList();
            var want = File.Exists(SdkAssembly);
            if (defines.Contains(Define) == want) return;
            if (want) defines.Add(Define);
            else defines.Remove(Define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        }
    }
}
