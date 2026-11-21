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
            // 动态字体每加进一批新字形，TextCore 都会在下一次排版时回源字体查这些字形的 GPOS 字距对
            // （FontEngine.GetPairAdjustmentRecords）。Noto Sans SC 是 10 MB 的 CJK 字体，每查一次要 10–90 ms，
            // 剧情里每句话出现没见过的字都会卡一下（端到端跑一遍序章有 160 多帧）。这套字体的 kern 只给拉丁字母
            // 和假名配了字距对，汉字和中文标点一对也没有；关掉以后只少了拉丁字母的成对字距（如 Te、AV），
            // 字形本身照常按需加进图集。
            asset.getFontFeatures = false;
            return asset;
        }

        static StyleFontDefinition Def(FontAsset asset) => new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
    }
}
