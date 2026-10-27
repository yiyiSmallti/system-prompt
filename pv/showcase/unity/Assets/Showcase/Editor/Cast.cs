using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Showcase
{
    // 项目里的六个角色模型，每个只载入一次，各镜头按需显示、摆位。身高同游戏的 SeaFigure。
    sealed class Cast
    {
        public sealed class Entry
        {
            public string Model, Name, Outfit, Note;
            public float Height;
            public Palette Palette;
            public Figure Figure;
        }

        public static readonly Entry[] Models =
        {
            new Entry { Model = "PlayerMale", Height = 1.72f, Name = "陪伴生（男）", Outfit = "家居服", Note = "序章被拉进潜空间时穿的", Palette = Palette.Night },
            new Entry { Model = "PlayerFemale", Height = 1.62f, Name = "陪伴生（女）", Outfit = "家居服", Note = "序章被拉进潜空间时穿的", Palette = Palette.Night },
            new Entry { Model = "PlayerMaleBattle", Height = 1.72f, Name = "陪伴生（男）", Outfit = "潜空间战斗服", Note = "白门镜子里的觉醒后形态", Palette = Palette.Terminal },
            new Entry { Model = "PlayerFemaleBattle", Height = 1.62f, Name = "陪伴生（女）", Outfit = "潜空间战斗服", Note = "白门镜子里的觉醒后形态", Palette = Palette.Terminal },
            new Entry { Model = "DeepSeekCasual", Height = 1.58f, Name = "DeepSeek", Outfit = "周末便服", Note = "灯塔觉醒前的卫衣裙", Palette = Palette.Sky },
            new Entry { Model = "DeepSeek", Height = 1.58f, Name = "DeepSeek", Outfit = "潜空间战斗服", Note = "思考强度 Max · 深渊之鲸", Palette = Palette.Sea },
        };

        readonly Dictionary<string, Entry> byModel = new Dictionary<string, Entry>();

        public Cast(Transform parent)
        {
            foreach (var e in Models)
            {
                e.Figure = new Figure(e.Model, e.Height, parent, tailBones: e.Model == "DeepSeek", darkGreys: e.Model == "PlayerMaleBattle");
                e.Figure.Show(false);
                byModel[e.Model] = e;
            }
        }

        public Entry this[string model] => byModel.TryGetValue(model, out var e) ? e : throw new ArgumentException($"没有模型 {model}");

        public IEnumerable<Figure> Figures => Models.Select(e => e.Figure);

        // 只显示这几个人物，其余藏起来；显示的放回原点、朝 +z。
        public void Only(params Figure[] shown)
        {
            foreach (var f in Figures)
            {
                f.Show(shown.Contains(f));
                f.Root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
        }

        public void Dispose()
        {
            foreach (var f in Figures) f.Dispose();
        }
    }
}
