using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // Import settings for the Tripo character models and motions (Mixamo rigs, exported as FBX): humanoid
    // avatars so one set of motions plays on every character, looping in-place clips (the battle's cut to the
    // part used, some played once), and no imported materials, since SeaFigure puts the toon shader on them
    // with the base colour texture. Also the areas' scenery modelled in Blender (Resources/Scenes).
    class ModelAssetImporter : AssetPostprocessor
    {
        const string ModelRoot = "Assets/SystemPrompt/Resources/Models/";
        const string PropRoot = ModelRoot + "Props/"; // still things (Dungeon/SeaModels.cs): plain meshes
        const string SceneRoot = "Assets/SystemPrompt/Resources/Scenes/"; // Dungeon/SeaScene.cs

        void OnPreprocessModel()
        {
            if (assetPath.StartsWith(SceneRoot))
            {
                // Plain meshes. Their materials come in only for their names, which SeaScene turns into the sea's
                // cel shading, and the meshes stay readable so it can batch them.
                var scene = (ModelImporter)assetImporter;
                scene.animationType = ModelImporterAnimationType.None;
                scene.importAnimation = false;
                scene.importCameras = false;
                scene.importLights = false;
                scene.importBlendShapes = false;
                scene.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                scene.isReadable = true;
                scene.indexFormat = ModelImporterIndexFormat.Auto;
                return;
            }
            if (!assetPath.StartsWith(ModelRoot)) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            // 32-bit indices where needed: in 16 bits DeepSeek's battle dress (83k vertices) came in cut into two
            // meshes, and the face capture and the chin only see one.
            importer.indexFormat = ModelImporterIndexFormat.Auto;
            if (assetPath.StartsWith(PropRoot))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                return;
            }
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
        }

        // The battle's motions from Tripo (on the battle coat, one per file, Dungeon/BattleStage.cs): the part of each
        // take that is used, in seconds (to 0: up to the end), and whether it loops. The cast's lead-in and settling
        // back are cut: it starts from the gathering just before the palm goes out (at 2.85 s), so she is casting half
        // a second in (BattleStage.CastLands). Its travel across the floor is dropped, not baked into the pose, so it
        // plays where the figure stands: taken from the body's centre of mass, since Tripo's rig carries it on the
        // hips and leaves the root node where it was.
        static readonly Dictionary<string, (float from, float to, bool loop)> Battle = new Dictionary<string, (float, float, bool)>
        {
            ["Cast"] = (1.95f, 4.4f, false),
        };

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(ModelRoot) || assetPath.StartsWith(PropRoot)) return;
            var importer = (ModelImporter)assetImporter;
            var battle = Battle.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(assetPath), out var cut) && assetPath.StartsWith(ModelRoot + "Motions/");
            var rate = importer.importedTakeInfos.Length > 0 ? importer.importedTakeInfos[0].sampleRate : 24f;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = !battle || cut.loop;
                clip.loopPose = clip.loopTime;
                // The motions are in place: keep the body's sway and bob in the pose and the root still.
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = !battle;
                clip.keepOriginalPositionXZ = !battle;
                if (!battle) continue;
                clip.firstFrame = Mathf.Max(clip.firstFrame, cut.from * rate);
                if (cut.to > 0f) clip.lastFrame = Mathf.Min(clip.lastFrame, cut.to * rate);
            }
            importer.clipAnimations = clips;
        }

        // Bump when the import below changes, so the models and textures already in the library are redone.
        public override uint GetVersion() => 9;

        // Tripo's rig has no bones for a tail, a bag or hair down the back, and gives their vertices to the limb
        // nearest in the T-pose: casual DeepSeek's whale tail went to her feet and right thigh, the hair down her
        // back to her shoulders, so both tore as soon as the legs or arms moved. The part of a limb's weight on a
        // vertex well away from the whole limb goes to the torso bone the limb hangs from (Hips for a leg, Spine2
        // for an arm), fading in over a band wide enough that the seam bends instead of tearing. Distances are in
        // metres on a 1.6 m figure. A skirt's hem is near a leg; the side of the chest under an arm is about 0.2 m
        // from it and needs its share of the arm to fold when the arm comes down, so arms let go only further out.
        static readonly (string limb, string torso, float near, float far)[] Limbs =
        {
            ("LeftUpLeg", "Hips", 0.16f, 0.32f), ("RightUpLeg", "Hips", 0.16f, 0.32f),
            ("LeftShoulder", "Spine2", 0.3f, 0.5f), ("RightShoulder", "Spine2", 0.3f, 0.5f),
        };

        // Models that should be one piece. Tripo gave casual DeepSeek two whale tails: the one hanging from under
        // her hoodie with its own fluke at the ankles, and a bigger fluke on a short stalk standing loose behind
        // her feet, joined to nothing, so from the side the tail looked cut in two. Anything not joined to the
        // body is dropped.
        static readonly string[] OnePiece = { "DeepSeekCasual" };

        // Models rigged in Blender rather than Tripo (assets/characters/highpoly/blender/process.py): DeepSeek's
        // battle dress has bones down its tail and weights already fixed part by part, which Unhook would undo,
        // and its head bone sits at the top of the neck, too near the chin for RoundChin to find it (the chin of
        // that multi-view model is not pointed anyway).
        static readonly string[] Blender = { "DeepSeek" };

        // Tripo sharpens every chin to a point, sharper than the turnarounds. The face below the mouth is gathered
        // up, most in the middle, so the point rises (ChinLift of the way from the mouth to the chin), and the jaw
        // eases out toward the chin (up to ChinWiden wider), so it comes out a little rounder.
        const float ChinLift = 0.45f, ChinWiden = 0.15f;

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ModelRoot + "Characters/")) return;
            var model = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            var onePiece = OnePiece.Contains(model);
            var blender = Blender.Contains(model);
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (onePiece) KeepBody(skin.sharedMesh);
                if (blender) continue;
                RoundChin(skin);
                Unhook(skin);
            }
        }

        // Keeps the triangles of the largest connected part. Vertices split along uv and normal seams are joined
        // by position first, so the seams don't cut the body into parts.
        static void KeepBody(Mesh mesh)
        {
            if (mesh == null) return;
            var vertices = mesh.vertices;
            var part = Enumerable.Range(0, vertices.Length).ToArray();
            int Find(int i) { while (part[i] != i) i = part[i] = part[part[i]]; return i; }
            void Join(int a, int b) => part[Find(a)] = Find(b);
            var cell = mesh.bounds.size.magnitude * 1e-5f;
            var seen = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] / cell);
                if (seen.TryGetValue(key, out var other)) Join(i, other); else seen[key] = i;
            }
            var triangles = mesh.triangles;
            for (var t = 0; t < triangles.Length; t += 3)
            {
                Join(triangles[t], triangles[t + 1]);
                Join(triangles[t + 1], triangles[t + 2]);
            }
            var body = triangles.Where((_, i) => i % 3 == 0).GroupBy(Find).OrderByDescending(g => g.Count()).First().Key;
            var dropped = 0;
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var kept = new List<int>();
                var subTriangles = mesh.GetTriangles(sub);
                for (var t = 0; t < subTriangles.Length; t += 3)
                {
                    if (Find(subTriangles[t]) != body) { dropped++; continue; }
                    kept.Add(subTriangles[t]);
                    kept.Add(subTriangles[t + 1]);
                    kept.Add(subTriangles[t + 2]);
                }
                mesh.SetTriangles(kept, sub);
            }
            if (dropped > 0) Debug.Log($"{mesh.name}: 去掉了 {dropped} 个没连在身上的三角形");
        }

        static void Unhook(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            Transform Bone(string name) => bones.FirstOrDefault(b => b != null && (b.name == name || b.name.EndsWith(":" + name)));
            var hips = Bone("Hips");
            var head = Bone("Head");
            if (mesh == null || hips == null || head == null) return;
            // Where a bone was when the mesh was bound to it, in the mesh's space (the file's rest pose can differ).
            var bindposes = mesh.bindposes;
            Vector3 At(Transform t) => bindposes[Array.IndexOf(bones, t)].inverse.MultiplyPoint3x4(Vector3.zero);
            var vertices = mesh.vertices;
            var up = (At(head) - At(hips)).normalized;
            var heights = vertices.Select(v => Vector3.Dot(v, up)).ToArray();
            var scale = 1.6f / (heights.Max() - heights.Min());

            var weights = mesh.boneWeights;
            var moved = 0;
            foreach (var (limbName, torsoName, near, far) in Limbs)
            {
                var limb = Bone(limbName);
                var torso = Array.IndexOf(bones, Bone(torsoName));
                if (limb == null || torso < 0) continue;
                // The limb as segments from each of its skinned bones to each skinned child, out to the fingers and toes.
                var skinned = limb.GetComponentsInChildren<Transform>().Where(t => Array.IndexOf(bones, t) >= 0).ToList();
                var segments = skinned.SelectMany(t =>
                {
                    var children = t.Cast<Transform>().Where(skinned.Contains).ToList();
                    return children.Count == 0 ? new[] { (At(t), At(t)) } : children.Select(c => (At(t), At(c))).ToArray();
                }).ToArray();
                var chain = new HashSet<int>(skinned.Select(t => Array.IndexOf(bones, t)));
                for (var v = 0; v < weights.Length; v++)
                {
                    var w = weights[v];
                    var influences = new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) };
                    if (!influences.Any(x => x.Item2 > 0f && chain.Contains(x.Item1))) continue;
                    var away = Mathf.Sqrt(segments.Min(s => DistanceSquared(vertices[v], s.Item1, s.Item2))) * scale;
                    var release = Step(near, far, away);
                    if (release <= 0f) continue;
                    var total = new Dictionary<int, float>();
                    foreach (var (bone, weight) in influences)
                    {
                        if (weight <= 0f) continue;
                        var keep = chain.Contains(bone) ? weight * (1f - release) : weight;
                        total[bone] = total.GetValueOrDefault(bone) + keep;
                        total[torso] = total.GetValueOrDefault(torso) + weight - keep;
                    }
                    weights[v] = Pack(total);
                    moved++;
                }
            }
            if (moved == 0) return;
            mesh.boneWeights = weights;
            Debug.Log($"{mesh.name}: {moved} 个顶点离四肢太远，权重转给了躯干");
        }

        // In a frame on the head bone, in metres on a 1.6 m figure: x across, y up, z ahead. The chin is where the
        // middle of the face, followed down from the head bone, steps back onto the neck; the mouth is taken to be
        // halfway between the head bone and the chin.
        static void RoundChin(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            Transform Bone(string name) => bones.FirstOrDefault(b => b != null && (b.name == name || b.name.EndsWith(":" + name)));
            Transform hips = Bone("Hips"), head = Bone("Head"), leftLeg = Bone("LeftUpLeg"), rightLeg = Bone("RightUpLeg");
            if (mesh == null || hips == null || head == null || leftLeg == null || rightLeg == null) return;
            var bindposes = mesh.bindposes;
            Vector3 At(Transform t) => bindposes[Array.IndexOf(bones, t)].inverse.MultiplyPoint3x4(Vector3.zero);
            var vertices = mesh.vertices;
            var origin = At(head);
            var up = (origin - At(hips)).normalized;
            var ahead = Vector3.Cross(up, At(leftLeg) - At(rightLeg)).normalized;
            var across = Vector3.Cross(up, ahead);
            var heights = vertices.Select(v => Vector3.Dot(v, up)).ToArray();
            var scale = 1.6f / (heights.Max() - heights.Min());
            var local = vertices.Select(v => { var d = (v - origin) * scale; return new Vector3(Vector3.Dot(d, across), Vector3.Dot(d, up), Vector3.Dot(d, ahead)); }).ToArray();

            // The front of the mesh cut down the middle (x = 0), every half millimetre down from the head bone.
            const float Row = 0.0005f;
            var front = Enumerable.Repeat(float.NegativeInfinity, 240).ToArray();
            var triangles = mesh.triangles;
            var cut = new List<Vector3>(3);
            for (var t = 0; t < triangles.Length; t += 3)
            {
                cut.Clear();
                for (var e = 0; e < 3; e++)
                {
                    Vector3 p = local[triangles[t + e]], q = local[triangles[t + (e + 1) % 3]];
                    if (p.x < 0f != q.x < 0f) cut.Add(Vector3.Lerp(p, q, p.x / (p.x - q.x)));
                }
                if (cut.Count != 2) continue;
                var (a, b) = cut[0].y > cut[1].y ? (cut[0], cut[1]) : (cut[1], cut[0]);
                for (var row = Mathf.Max(0, Mathf.CeilToInt(-a.y / Row - 0.5f)); row < front.Length && (row + 0.5f) * -Row >= b.y; row++)
                {
                    var y = (row + 0.5f) * -Row;
                    var z = a.y > b.y ? Mathf.Lerp(b.z, a.z, (y - b.y) / (a.y - b.y)) : Mathf.Max(a.z, b.z);
                    front[row] = Mathf.Max(front[row], z);
                }
            }
            var chin = -1;
            for (var row = 0; row + 30 < front.Length && chin < 0; row++)
                if (front[row] - front[row + 6] > 0.015f && !float.IsInfinity(front[row]) && !float.IsInfinity(front[row + 6])) chin = row;
            if (chin < 40) // the chin less than 2 cm under the head bone, or not found: something else is in the way
            {
                Debug.LogWarning($"{mesh.name}: 没找到下巴，下巴没修");
                return;
            }
            float chinY = (chin + 0.5f) * -Row, chinZ = front[chin], neckZ = front.Skip(chin + 6).Take(24).Max();
            float length = -chinY, mouth = chinY * 0.5f;

            var moved = 0;
            for (var v = 0; v < vertices.Length; v++)
            {
                var p = local[v];
                // Only the face in front of the neck, and nothing more than a few millimetres under the chin.
                var face = Step(neckZ, neckZ + 0.4f * (chinZ - neckZ), p.z) * (1f - Step(0f, 0.008f, chinY - p.y));
                if (face <= 0f) continue;
                var lift = ChinLift * (mouth - Mathf.Clamp(p.y, chinY, mouth)) * Mathf.Exp(-Mathf.Pow(p.x / (0.45f * length), 2f));
                var widen = ChinWiden * Step(0.2f, 0.85f, -p.y / length);
                if (lift <= 0f && widen <= 0f) continue;
                vertices[v] += (up * lift + across * (p.x * widen)) * face / scale;
                moved++;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            Debug.Log($"{mesh.name}: 下巴在头骨下 {length * 1000f:F0} mm，修圆时动了 {moved} 个顶点");
        }

        static float DistanceSquared(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }

        // The four heaviest, scaled back to a sum of one.
        static BoneWeight Pack(Dictionary<int, float> total)
        {
            var top = total.Where(x => x.Value > 0f).OrderByDescending(x => x.Value).Take(4).ToArray();
            var sum = top.Sum(x => x.Value);
            var w = new BoneWeight();
            if (top.Length > 0) { w.boneIndex0 = top[0].Key; w.weight0 = top[0].Value / sum; }
            if (top.Length > 1) { w.boneIndex1 = top[1].Key; w.weight1 = top[1].Value / sum; }
            if (top.Length > 2) { w.boneIndex2 = top[2].Key; w.weight2 = top[2].Value / sum; }
            if (top.Length > 3) { w.boneIndex3 = top[3].Key; w.weight3 = top[3].Value / sum; }
            return w;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ModelRoot)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            if (assetPath.EndsWith("_FaceDepth.png"))
            {
                // A depth per texel (Editor/FaceCapture.cs, the same in all three channels), read as it is.
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaSource = TextureImporterAlphaSource.None;
                return;
            }
            if (assetPath.EndsWith("_Eyes.png"))
            {
                // The eyes painted over the base colour (Tools/face_eyes.py), read from the file by the base
                // colour's own import below; the texture itself is never drawn, so it is kept tiny.
                importer.maxTextureSize = 32;
                importer.mipmapEnabled = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                return;
            }
            if (assetPath.EndsWith("_Faces.png"))
            {
                // The expressions (Tools/face_expressions.py), drawn with their own alpha.
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            importer.sRGBTexture = true;
            if (assetPath.StartsWith(PropRoot))
            {
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            // Any alpha source, so the texture keeps an alpha channel: OnPostprocessTexture writes the skin mask there.
            importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        // Tripo's delighting flattens the bright end of the base colour. Against the turnarounds the darks
        // match (#343439 in both), but white comes out at 76% instead of 96%, and skin a grey pink (#B6A9A8,
        // hue 5°) instead of the drawn peach (#EBD6CD, hue 18°). The bright end of whites, greys and skin is
        // lifted back, skin-coloured texels turn warmer, and alpha keeps how much of each texel is skin, for
        // the toon shader's skin shade. Coloured texels keep their value: lifted too, DeepSeek's navy hair came
        // out a pale sky blue. Tripo also turns blues about 12° toward cyan at every brightness (her hair's
        // darks 223° against the drawn 236°, both outfits) and greys their bright end (saturation 0.32 where
        // the drawn light ends are 0.5); blues turn back, less so toward the bright end, where the drawn ends
        // are sky blue anyway, and the bright end takes its saturation back.
        // The player models' eyes came out wrong (hair drawn across them, one with no pupil), and are painted
        // again by Tools/face_eyes.py as <model>_Eyes.png, the base colour's size and transparent but where it
        // repaints: laid over the corrected colour, which it was painted against, with the painted texels'
        // skin share judged again from their colour.
        void OnPostprocessTexture(Texture2D texture)
        {
            if (!assetPath.StartsWith(ModelRoot + "Characters/") || !assetPath.EndsWith("_BaseColor.png")) return;
            var eyesPath = assetPath.Replace("_BaseColor.png", "_Eyes.png");
            context.DependsOnSourceAsset(eyesPath);
            Color32[] eyes = null;
            if (System.IO.File.Exists(eyesPath))
            {
                var painted = new Texture2D(2, 2);
                painted.LoadImage(System.IO.File.ReadAllBytes(eyesPath));
                if (painted.width == texture.width && painted.height == texture.height) eyes = painted.GetPixels32();
                else Debug.LogWarning($"{eyesPath} 是 {painted.width}×{painted.height}，基础色是 {texture.width}×{texture.height}，眼睛没画上");
                UnityEngine.Object.DestroyImmediate(painted);
            }
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out var h, out var s, out var v);
                var hue = h * 360f;
                var skin = (1f - Step(25f, 40f, Mathf.Abs(Mathf.DeltaAngle(10f, hue))))
                    * Step(0.03f, 0.05f, s) * (1f - Step(0.3f, 0.4f, s)) * Step(0.45f, 0.6f, v);
                hue += skin * Mathf.DeltaAngle(hue, 16f);
                var grey = 1f - Step(0.15f, 0.3f, s);
                var lifted = v * (1f + 0.27f * Mathf.InverseLerp(0.3f, 0.76f, v) * Mathf.Max(grey, skin));
                var blue = (1f - Step(20f, 35f, Mathf.Abs(Mathf.DeltaAngle(215f, hue)))) * (1f - grey);
                var bright = Step(0.55f, 0.75f, v);
                hue += 12f * blue * (1f - bright);
                var saturation = s * (1f + 0.6f * skin) * (1f + 0.45f * blue * bright);
                var c = Color.HSVToRGB(Mathf.Repeat(hue, 360f) / 360f, Mathf.Clamp01(saturation), Mathf.Min(1f, lifted));
                c.a = skin;
                if (eyes != null && eyes[i].a > 0)
                {
                    Color e = eyes[i];
                    Color.RGBToHSV(e, out h, out s, out v);
                    // Skin as it is after the correction: the hue turned to 16°, more saturated, lifted.
                    e.a = (1f - Step(25f, 40f, Mathf.Abs(Mathf.DeltaAngle(16f, h * 360f)))) * Step(0.04f, 0.07f, s) * (1f - Step(0.45f, 0.6f, s)) * Step(0.55f, 0.7f, v);
                    c = Color.Lerp(c, e, eyes[i].a / 255f);
                }
                pixels[i] = c;
            }
            texture.SetPixels(pixels);
            texture.Apply(true); // the mips from the corrected top level
        }

        static float Step(float from, float to, float x)
        {
            var t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
