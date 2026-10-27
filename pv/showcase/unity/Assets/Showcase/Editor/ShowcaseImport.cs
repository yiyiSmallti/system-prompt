using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Showcase
{
    // 展示片对母作导入设置（SystemPrompt/Editor/ModelAssetImporter.cs，从 game/ 原样同步）的两处改动，排在它之后：
    // 衍生作的出招、受击、倒地这些 Mixamo 动作只播一次（母作把 Models/ 下的动作都当成循环，loopPose 会把首尾的
    // 姿势拉齐，首尾不一样的动作会被拉变形；名单同衍生作的 MotionImport.cs）；贴图不压缩、开各向异性过滤，
    // 近景里看得清细节。
    class ShowcaseImport : AssetPostprocessor
    {
        const string Models = "Assets/SystemPrompt/Resources/Models/";

        public static readonly string[] OneShots =
        {
            "StepForward", "StepBackward", "LeadJab", "MmaKick", "RoundhouseKick", "Uppercut", "PunchCombo",
            "LightHit", "BigHit", "KnockedOut", "GettingUp", "Block", "FlipBackwards", "JumpUp", "Yawn", "Victory",
        };

        public override int GetPostprocessOrder() => 10;

        public override uint GetVersion() => 1;

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Models + "Motions/") || Array.IndexOf(OneShots, Path.GetFileNameWithoutExtension(assetPath)) < 0) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = false;
                clip.loopPose = false;
            }
            importer.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Models) || assetPath.EndsWith("_FaceDepth.png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.anisoLevel = 16;
            importer.filterMode = FilterMode.Trilinear;
        }
    }
}
