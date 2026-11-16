# 噪声体模型

潜空间里的三种噪声体都由 `game/Assets/SystemPrompt/Scripts/Dungeon/SeaActors.cs` 的 `NoiseLook` 搭出来。只有转发型的身子是在 Blender 里做的：

| 噪声体 | 做法 |
| --- | --- |
| 标题党型 | 代码里排体素（`BlockCells`）：8 × 6 × 4 的弹窗，红色标题栏、右端白色关闭钮，眼睛在标题栏下面，再下面是两行高的黑嘴、两头各一颗白牙。散成方块的特效靠体素网格 uv2 里的方块中心和编号，所以不能换成整块网格。标题栏、关闭钮、牙和嘴里的固定颜色写在顶点色里（`BlockRole`，`Glitch.shader`）。 |
| 转发型 | 箭头刺猬。身子是这里的 `repost_body.py` 生成的 `RepostBody.fbx`，背上的刺是代码做的分叉箭头（`ForkedArrow`，放大 1.25 倍，方向在 `ArrowDirs`）。 |
| 冷知识型 | 代码里的一叠索引卡（`Glitch.shader` 的卡片模式）。 |

## 重新生成转发型的身子

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b -P assets/characters/noise/repost_body.py -- game/Assets/SystemPrompt/Resources/Models/Noise/RepostBody.fbx /tmp/repost_preview.png
```

脚本把几个椭球（身子、尖鼻子、鼻头、两只耳朵、四只脚）合并，用体素重建焊成一个整体，再用拉普拉斯平滑磨出接缝处的圆角，减到约 5 千个三角面后导出。正面朝 Blender 的 -Y，导出后是 Unity 的 +z；单位是米，身子中心在原点。第二个参数可选，会额外渲染一张前侧 3/4 加正侧面的预览图。

`Resources/Models/Noise/` 下的模型按静态网格导入，并设成可读（`Editor/ModelAssetImporter.cs`），因为 `NoiseLook.Shape` 要在运行时把子节点的变换烘进顶点，合成一个以米为单位的网格。故障着色器是按网格自身坐标切格子的，这样烘过之后，格子大小才和代码生成的身子一致。
