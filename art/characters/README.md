# 角色三渲二模型

已确认的 ChatGPT、Claude 春秋校服模型，保留原模型和贴图，增加 Eevee 分层阴影、脸部减阴影与细描边。每套均有可直接打开的 Blender 工程及四张实际渲染图。

| 角色 | 工程 | 预览与检查记录 |
| --- | --- | --- |
| ChatGPT | [toon.blend](chatgpt/1-uniform/toon/toon.blend) | [斜侧](chatgpt/1-uniform/toon/quarter.png) · [正面](chatgpt/1-uniform/toon/front.png) · [背面](chatgpt/1-uniform/toon/back.png) · [正脸](chatgpt/1-uniform/toon/face-front.png) · [报告](chatgpt/1-uniform/toon/toon-report.json) |
| Claude | [toon.blend](claude/1-uniform/toon/toon.blend) | [斜侧](claude/1-uniform/toon/quarter.png) · [正面](claude/1-uniform/toon/front.png) · [背面](claude/1-uniform/toon/back.png) · [正脸](claude/1-uniform/toon/face-front.png) · [报告](claude/1-uniform/toon/toon-report.json) |

模型及预览通过 Git LFS 管理。在安装 Git LFS 后，于仓库运行 `git lfs pull --include="art/characters/**"`，再用 Blender 4.3 或更新版本打开工程，保持 **Eevee** 渲染器，按 **F12** 查看。三个原始纹理均已内嵌，原 PBR 材质作为备份保留，无需下载原 Tripo 输出即可打开当前模型。

## 调整效果

- 默认相机为斜侧 `Inspection_TOON_quarter`；场景还保留正面、背面和脸部相机。
- 切换 `TOON_Outlines` 集合可开关描边；旋转 `TOON_Key` 主光可改变分层阴影。
- 描边和脸部遮罩跟随原网格的平移、旋转。两模型尚未绑定骨骼，这不是动画变形验证。

每套 `manifest.json` 记录文件大小和 SHA-256；`toon-report.json` 记录贴图打包、重开工程及原网格不变检查；`visual-review.json` 记录四视角可见问题。提交文件与已确认的最终模型一致，未改动角色几何、UV 或原 atlas。

## 制作与限制

[build_toon.py](toon/build_toon.py) 是制作附录，需通过 `--input` 指定原 PBR Blender 工程；本目录没有重复收录该输入。查看、编辑和渲染当前 `toon.blend` 不需要运行脚本。

两个模型仍为高面数模型，尚未绑定、重拓扑或完成游戏优化。原 atlas 中的烘焙明暗及已有模型细节问题仍保留；脸部遮罩是保守的颜色与位置判断。Eevee 的 Shader to RGB 节点不会随普通 GLB/FBX 导出完整移植到 Unity，游戏端需要对应着色器。

角色原创者为 [ZipZipPipe（胖次狐）](https://space.bilibili.com/4168597)，设定图来源 [lmy414/ai-chan-sheets](https://github.com/lmy414/ai-chan-sheets)。衍生素材沿用 **CC BY-NC-SA 4.0**，仅限非商业用途；署名、许可及修改说明见 [LICENSE.md](toon/LICENSE.md)。
