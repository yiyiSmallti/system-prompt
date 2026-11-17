# 光鲸虚影

大招里 PV 那样的鲸鱼虚影（集成里游过战场、深海潜流里从 DeepSeek 身后升起再扎进漩涡）用的身子：一条座头鲸，修长的身体、喉部微鼓、三分之一体长的胸鳍、小背鳍、带缺口的尾叶。体长 1 米，正面朝 Blender 的 -Y（导出后是 Unity 的 +z），身体中心在原点，约 1.8 万个三角形，没有贴图。

```bash
blender -b -P whale_phantom.py -- ../../../game/Assets/SystemPrompt/Resources/Models/Props/WhalePhantom.fbx preview.png
```

身体是一串超椭圆截面沿 Catmull-Rom 曲线放样（直线插值会在身上折出棱），鳍是翼型截面放样，合并后体素重建、平滑、减面。`preview.png` 是三个视角：正侧面、俯视、从前下方看的 3/4。

游戏里用 `Resources/Shaders/Phantom.shader` 画（`SeaArt.Phantom`）：叠加、不写深度；按模型自身空间里的位置（`_Length`、`_Mid` 由 `BattleEffects.CallWhale` 从网格包围盒算出）画出白亮的轮廓、喉部褶纹、闪烁星点和从头到尾的光波，`_Shown` 从 0 到 1 让它从头部开始由火花显形。
