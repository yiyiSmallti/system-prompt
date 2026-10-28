using UnityEditor;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // Import settings for the 2D UI art and music, so nobody has to set them by hand per file.
    class UiAssetImporter : AssetPostprocessor
    {
        const string ArtRoot = "Assets/SystemPrompt/Resources/Art/";
        const string BgmRoot = "Assets/SystemPrompt/Resources/Audio/BGM/";
        const string TransitionRoot = ArtRoot + "Transitions/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true; // portraits are cut out; avoids dark fringes when scaled
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (!assetPath.StartsWith(TransitionRoot)) return;
            // The transition textures scroll and tile in Shaders/ScreenFx.shader. The circuit board isn't
            // seamless, so it mirrors; the noise holds data in its channels and is read raw.
            importer.alphaIsTransparency = false;
            importer.wrapMode = assetPath.Contains("circuit") ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;
            if (assetPath.Contains("noise"))
            {
                importer.sRGBTexture = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(BgmRoot)) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            importer.defaultSampleSettings = settings;
        }
    }
}
