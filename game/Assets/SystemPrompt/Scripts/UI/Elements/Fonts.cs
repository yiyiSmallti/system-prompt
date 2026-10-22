using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    public enum FontKind { Regular, Bold, Black, Mono, MonoBold }

    // Font assets are built at runtime from the TTFs in Resources/Fonts. JetBrains Mono has no CJK glyphs,
    // so the mono assets fall back to Noto Sans SC, like the PV renderer does.
    public static class Fonts
    {
        static readonly Dictionary<FontKind, StyleFontDefinition> definitions = new Dictionary<FontKind, StyleFontDefinition>();

        public static void Init()
        {
            if (definitions.Count > 0) return;
            var regular = Create("NotoSansSC-Regular");
            var bold = Create("NotoSansSC-Bold");
            var black = Create("NotoSansSC-Black");
            var mono = Create("JetBrainsMono-Regular");
            var monoBold = Create("JetBrainsMono-Bold");
            mono.fallbackFontAssetTable = new List<FontAsset> { regular };
            monoBold.fallbackFontAssetTable = new List<FontAsset> { bold };
            black.fallbackFontAssetTable = new List<FontAsset> { bold };

            definitions[FontKind.Regular] = Def(regular);
            definitions[FontKind.Bold] = Def(bold);
            definitions[FontKind.Black] = Def(black);
            definitions[FontKind.Mono] = Def(mono);
            definitions[FontKind.MonoBold] = Def(monoBold);
        }

        public static void Apply(VisualElement e, FontKind kind) => e.style.unityFontDefinition = definitions[kind];

        static FontAsset Create(string file)
        {
            var font = Resources.Load<Font>("Fonts/" + file);
            if (font == null) throw new InvalidOperationException($"找不到字体 Resources/Fonts/{file}");
            var asset = FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048);
            if (asset == null) throw new InvalidOperationException($"字体 {file} 无法生成字体资源，检查导入设置是否为 Dynamic");
            asset.name = file;
            return asset;
        }

        static StyleFontDefinition Def(FontAsset asset) => new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
    }
}
