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
        const string NoiseRoot = ModelRoot + "Noise/"; // the noise bodies' shapes (Dungeon/SeaActors.cs NoiseLook): plain meshes
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
            if (assetPath.StartsWith(PropRoot) || assetPath.StartsWith(NoiseRoot))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                // NoiseLook copies the noise bodies' meshes into its own at run time, which needs them readable.
                if (assetPath.StartsWith(NoiseRoot)) importer.isReadable = true;
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
            if (!assetPath.StartsWith(ModelRoot) || assetPath.StartsWith(PropRoot) || assetPath.StartsWith(NoiseRoot)) return;
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
        public override uint GetVersion() => 17;

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
                MarkUnderside(skin);
                if (blender) continue;
                RoundChin(skin);
                Unhook(skin);
            }
        }

        // A tail's underside marked in the mesh's vertex colours: red 0 where a vertex is wholly underside, 1 anywhere
        // else, for Toon.shader's _Lift. The generated texture has the back of DeepSeek's battle tail bright and starry
        // and its underside a third as bright (no picture the model was made from shows that side), and hooked up
        // beside her the underside is what faces anyone in front of her. A vertex is the tail's by its bones' names
        // (Tail...), and underside by its normal against the tail's own back there: across the bone, in her middle
        // plane, on the side behind her where the tail hangs and above it where it lies. Models with no such bones
        // are left as they are.
        // The whole tail is marked as well, for the starlit tail of the PV (Toon.shader's _Starlit): green 0 where a
        // vertex is wholly the tail's (1 off it), and uv3 its place on the tail, x along it (0 at the root, 1 at the
        // fluke's end) and y round it (the normal's turn about the bone, 0 to 1), so the stars sit evenly on the tail
        // and stay put on it as it bends, whatever the texture's own islands are.
        static void MarkUnderside(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh;
            if (mesh == null) return;
            var bones = skin.bones;
            var tail = Enumerable.Range(0, bones.Length).Where(i => bones[i] != null && bones[i].name.Contains("Tail")).ToList();
            var hips = Array.FindIndex(bones, b => b != null && b.name.EndsWith("Hips"));
            var head = Array.FindIndex(bones, b => b != null && b.name.EndsWith("Head"));
            if (tail.Count < 2 || hips < 0 || head < 0) return;
            var binds = mesh.bindposes;
            Vector3 At(int bone) => binds[bone].inverse.MultiplyPoint(Vector3.zero);
            var up = (At(head) - At(hips)).normalized;
            var behind = Vector3.ProjectOnPlane(At(tail[tail.Count - 1]) - At(hips), up).normalized;
            var across = Vector3.Cross(up, behind);
            var back = new Dictionary<int, Vector3>();
            var axis = new Dictionary<int, Vector3>();
            var order = new Dictionary<int, float>();
            for (var i = 0; i < tail.Count; i++)
            {
                var along = i + 1 < tail.Count ? At(tail[i + 1]) - At(tail[i]) : At(tail[i]) - At(tail[i - 1]);
                back[tail[i]] = Vector3.Cross(along.normalized, across).normalized;
                axis[tail[i]] = along.normalized;
                order[tail[i]] = (float)i / (tail.Count - 1);
            }
            var normals = mesh.normals;
            var weights = mesh.boneWeights;
            var trim = Trim(mesh.vertices, weights, head, At(head), up, -behind, out var fins, out var bows);
            var colors = new Color32[mesh.vertexCount];
            var place = new Vector2[mesh.vertexCount];
            var marked = 0;
            for (var v = 0; v < colors.Length; v++)
            {
                var w = weights[v];
                float share = 0f, along = 0f, most = 0f;
                var its = Vector3.zero;
                var main = -1;
                void Add(int bone, float weight)
                {
                    if (weight <= 0f || !back.TryGetValue(bone, out var b)) return;
                    share += weight;
                    its += b * weight;
                    along += order[bone] * weight;
                    if (weight > most) { most = weight; main = bone; }
                }
                Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                var under = 0f;
                if (share > 0.5f && its.sqrMagnitude > 1e-6f)
                    under = Mathf.Clamp01((-Vector3.Dot(normals[v], its.normalized) - 0.1f) / 0.5f) * Mathf.Clamp01((share - 0.5f) / 0.3f);
                if (under > 0f) marked++;
                colors[v] = new Color32((byte)Mathf.RoundToInt(255f * (1f - under)), (byte)Mathf.RoundToInt(255f * (1f - Mathf.Clamp01(share))), (byte)Mathf.RoundToInt(255f * (1f - trim[v])), 255);
                if (main >= 0)
                {
                    var n = Vector3.ProjectOnPlane(normals[v], axis[main]);
                    var round = Mathf.Atan2(Vector3.Dot(n, Vector3.Cross(axis[main], back[main])), Vector3.Dot(n, back[main]));
                    place[v] = new Vector2(along / share, round / (2f * Mathf.PI) + 0.5f);
                }
            }
            mesh.colors32 = colors;
            mesh.SetUVs(2, place);
            Debug.Log($"{mesh.name}: 尾巴腹面标了 {marked} 个顶点，耳鳍 {fins} 个、蝴蝶结 {bows} 个");
        }

        // Her ear fins and the bows over them, for Toon.shader's _Trim (vertex blue: 1 − this): of what hangs on the
        // head bone, by where it is from the head joint in units of how high the head reaches above it (so neither
        // the file's scale nor its axes matter), across either way from the middle, up, and ahead. The fins stand
        // out flat at the sides at about a quarter of that height, wider than the hair there; the bows sit just
        // above them on the band, and the box takes in some dark hair, which the shader leaves out by its colour.
        // (Measured on the battle dress in Blender, the head's vertices plotted by the texture's colour.)
        static float[] Trim(Vector3[] vertices, BoneWeight[] weights, int head, Vector3 joint, Vector3 up, Vector3 ahead, out int fins, out int bows)
        {
            var trim = new float[vertices.Length];
            fins = bows = 0;
            var across = Vector3.Cross(up, ahead);
            float OnHead(BoneWeight w) => (w.boneIndex0 == head ? w.weight0 : 0f) + (w.boneIndex1 == head ? w.weight1 : 0f)
                + (w.boneIndex2 == head ? w.weight2 : 0f) + (w.boneIndex3 == head ? w.weight3 : 0f);
            var top = 0f;
            for (var v = 0; v < vertices.Length; v++)
                if (OnHead(weights[v]) >= 0.3f) top = Mathf.Max(top, Vector3.Dot(vertices[v] - joint, up));
            if (top <= 0f) return trim;
            for (var v = 0; v < vertices.Length; v++)
            {
                if (OnHead(weights[v]) < 0.3f) continue;
                var p = (vertices[v] - joint) / top;
                float side = Mathf.Abs(Vector3.Dot(p, across)), high = Vector3.Dot(p, up), front = Vector3.Dot(p, ahead);
                if (side > 0.34f && high > 0.2f && high < 0.42f)
                {
                    trim[v] = 1f;
                    fins++;
                }
                else if (side > 0.27f && side < 0.4f && high >= 0.42f && high < 0.56f && front > -0.08f && front < 0.15f)
                {
                    trim[v] = 0.5f;
                    bows++;
                }
            }
            return trim;
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
            importer.maxTextureSize = Array.Exists(Fine, model => assetPath.EndsWith("/" + model + "_BaseColor.png")) ? 4096 : 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        // Models whose base colour is kept at 4096: DeepSeek's battle dress was generated again with an 8K texture
        // for its lace hem and gold embroidery, which at 2048 smear back into what the first one had.
        static readonly string[] Fine = { "DeepSeek" };

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
        // DeepSeek's battle dress was generated with shade baked into its skin: the band of thigh between stocking
        // and skirt and the side of the face came out a flat brown (about #8A6F68, half the skin's texels), which
        // on the figure read as a tan line across the cheek and brown thighs under a pale face. The toon shader
        // shades the skin itself, so before the correction those texels are taken back to the skin as it is where
        // no shade fell (Unshade); only on the models named in BakedShade, whose brown is nothing but skin (on
        // another model the same brown could be leather).
        static readonly string[] BakedShade = { "DeepSeek" };

        // The lounge tops are VRoid's hoodie with its hood and strings rubbed out and the rest filled one grey. What
        // was left of the hoodie's own dark paint shows on the figures as flaws: a wedge at each corner of the collar
        // (the hood's root), a dark line down each sleeve's seam, a notch by each armhole, and the hem's ribbing, drawn
        // as a row of near-black teeth, which under a lifted thigh read as the shorts coming through the cloth. Every
        // texel much darker than the top's one colour is given that colour (Plain); its alpha, which cuts the cloth's
        // outline, is left as it is.
        static readonly string[] PlainTops =
        {
            "Assets/SystemPrompt/Characters/PlayerFemaleLounge/N00_005_01_Tops_01_CLOTH.png",
            "Assets/SystemPrompt/Characters/PlayerMaleLounge/N00_005_01_Tops_01_CLOTH.png",
        };

        static void Plain(Texture2D texture, string path)
        {
            var pixels = texture.GetPixels32();
            // The one colour: the commonest among the opaque texels, to 5 bits a channel.
            var seen = new Dictionary<int, int>();
            foreach (var p in pixels)
            {
                if (p.a < 250) continue;
                var key = (p.r >> 3) << 10 | (p.g >> 3) << 5 | p.b >> 3;
                seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            if (seen.Count == 0) return;
            var most = seen.OrderByDescending(kv => kv.Value).First().Key;
            long r = 0, g = 0, b = 0, count = 0;
            foreach (var p in pixels)
            {
                if (p.a < 250 || ((p.r >> 3) << 10 | (p.g >> 3) << 5 | p.b >> 3) != most) continue;
                r += p.r; g += p.g; b += p.b; count++;
            }
            var plain = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
            var level = 0.75f * (plain.r + plain.g + plain.b);
            var changed = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.r + p.g + p.b >= level) continue; // the clear texels too: the cut edge is filtered across them
                pixels[i] = new Color32(plain.r, plain.g, plain.b, p.a);
                changed++;
            }
            Knit(pixels, texture.width, texture.height);
            texture.SetPixels32(pixels);
            texture.Apply(true);
            Debug.Log($"{path}：{changed} 个深色像素填成上衣的颜色 #{ColorUtility.ToHtmlStringRGB(plain)}，再画成炭灰色的针织面料");
        }

        // The plain top painted as the design has it (assets/characters/protagonist-*-outfits.jpg, the lounge clothes):
        // charcoal, not the mid grey it was filled with; the weave as a fine grain and long soft folds down it; ribbing
        // (a darker band with fine upright lines, a seam where it is sewn on) along the hem, the cuffs and round the
        // neck. Places are those of VRoid's hoodie layout on a texture 2048 across, rows counted from the top: the body
        // across the lower half with its hem band from row 1956, a sleeve in each upper corner with its cuff from row
        // 880, the neck a notch in the body's top edge between columns 820 and 1230.
        static readonly Color32 Charcoal = new Color32(58, 59, 67, 255);

        static void Knit(Color32[] pixels, int width, int height)
        {
            var unit = width / 2048f;
            int At(int x, int row) => (height - 1 - row) * width + x;
            // How far each texel of the neck's surround is from the cloth's edge there, in texels (up to the collar's width).
            int collar = Mathf.RoundToInt(30 * unit), x0 = Mathf.RoundToInt(780 * unit), x1 = Mathf.RoundToInt(1270 * unit),
                r0 = Mathf.RoundToInt(900 * unit), r1 = Mathf.RoundToInt(1140 * unit);
            var edge = new int[(x1 - x0) * (r1 - r0)];
            for (var i = 0; i < edge.Length; i++) edge[i] = pixels[At(x0 + i % (x1 - x0), r0 + i / (x1 - x0))].a < 128 ? 0 : int.MaxValue;
            for (var step = 1; step <= collar + 5; step++)
                for (var i = 0; i < edge.Length; i++)
                {
                    if (edge[i] != int.MaxValue) continue;
                    int x = i % (x1 - x0), r = i / (x1 - x0);
                    if ((x > 0 && edge[i - 1] == step - 1) || (x + 1 < x1 - x0 && edge[i + 1] == step - 1)
                        || (r > 0 && edge[i - (x1 - x0)] == step - 1) || (r + 1 < r1 - r0 && edge[i + (x1 - x0)] == step - 1)) edge[i] = step;
                }
            int hem = Mathf.RoundToInt(1956 * unit), cuff = Mathf.RoundToInt(880 * unit), cuffEnd = Mathf.RoundToInt(1012 * unit),
                sleeveLeft = Mathf.RoundToInt(640 * unit), sleeveRight = Mathf.RoundToInt(1410 * unit), seam = Mathf.Max(2, Mathf.RoundToInt(5 * unit)),
                rib = Mathf.Max(3, Mathf.RoundToInt(7 * unit));
            for (var row = 0; row < height; row++)
                for (var x = 0; x < width; x++)
                {
                    var i = At(x, row);
                    // The weave: a grain texel by texel, and folds a hand wide running down the cloth.
                    var grain = Mathf.Repeat(Mathf.Sin(x * 12.9898f + row * 78.233f) * 43758.5453f, 1f) - 0.5f;
                    var fold = Mathf.Sin(x / (41f * unit) + 1.7f * Mathf.Sin(row / (330f * unit))) * Mathf.Sin(x / (97f * unit) + row / (610f * unit));
                    var shade = 1f + 0.05f * grain + 0.06f * fold;
                    var ribbed = false;
                    var sewn = false;
                    if (row >= hem) { ribbed = true; sewn = row < hem + seam; }
                    else if ((x < sleeveLeft || x > sleeveRight) && row >= cuff && row < cuffEnd) { ribbed = true; sewn = row < cuff + seam; }
                    else if (x >= x0 && x < x1 && row >= r0 && row < r1)
                    {
                        var d = edge[(row - r0) * (x1 - x0) + (x - x0)];
                        ribbed = d <= collar;
                        sewn = d > collar && d <= collar + seam;
                    }
                    if (ribbed) shade *= x / rib % 2 == 0 ? 0.8f : 0.9f;
                    if (sewn) shade *= 0.62f;
                    pixels[i] = new Color32((byte)Mathf.Clamp(Charcoal.r * shade, 0f, 255f), (byte)Mathf.Clamp(Charcoal.g * shade, 0f, 255f),
                        (byte)Mathf.Clamp(Charcoal.b * shade, 0f, 255f), pixels[i].a);
                }
        }

        // The girl's lounge shorts have white piping round each leg in the design; the texture (VRoid's shorts, black)
        // has none. A band of rows at each leg's hem, as fractions of the texture's height from the top, is painted
        // off-white where the cloth is.
        static readonly Dictionary<string, Vector2> Piped = new Dictionary<string, Vector2>
        {
            ["Assets/SystemPrompt/Characters/PlayerFemaleLounge/N00_001_01_Bottoms_01_CLOTH.png"] = new Vector2(0.316f, 0.3275f),
        };

        static void Pipe(Texture2D texture, Vector2 rows)
        {
            var pixels = texture.GetPixels32();
            int from = Mathf.RoundToInt(rows.x * texture.height), to = Mathf.RoundToInt(rows.y * texture.height);
            for (var row = from; row < to; row++)
                for (var x = 0; x < texture.width; x++)
                {
                    var i = (texture.height - 1 - row) * texture.width + x;
                    if (pixels[i].a >= 128) pixels[i] = new Color32(232, 234, 240, pixels[i].a);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true);
        }

        void OnPostprocessTexture(Texture2D texture)
        {
            if (Array.IndexOf(PlainTops, assetPath) >= 0)
            {
                Plain(texture, assetPath);
                return;
            }
            if (Piped.TryGetValue(assetPath, out var piping))
            {
                Pipe(texture, piping);
                return;
            }
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
            if (Array.Exists(BakedShade, model => assetPath.EndsWith("/" + model + "_BaseColor.png"))) Unshade(pixels, texture.width, texture.height);
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

        // Skin in baked shade (a dull red-brown: hue within 14 to 20 degrees of 6, saturation 0.16 to 0.38, value
        // 0.38 to 0.58, each with a soft edge) taken to the mean of the plainly light skin, a little of how dark it
        // was kept as form. By colour alone that also took the dark side of the gold (buttons, seams, buckles, the
        // garter's charm), which came out speckled pale, and stray texels of the neck: so only where the brown lies
        // in a patch of its own, MinPatch of the texture or more (ten patches hold three quarters of it: the thighs'
        // bands and the side of the face; the gold is in scraps of a few hundred texels at most). Round each patch
        // kept, three texels out, whatever is still skin-coloured goes with it, or the patch keeps a brown rim where
        // its edge was blended with what lies next to it.
        const float MinPatch = 0.00024f;

        static void Unshade(Color[] pixels, int width, int height)
        {
            Vector3 sum = default;
            var count = 0;
            var weight = new float[pixels.Length];
            var near = new float[pixels.Length]; // how far a texel by a kept patch is taken with it
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                Color.RGBToHSV(p, out var h, out var s, out var v);
                if (v > 0.68f && s >= 0.05f && s <= 0.3f && Mathf.Abs(Mathf.DeltaAngle(10f, h * 360f)) <= 25f)
                {
                    sum += new Vector3(p.r, p.g, p.b);
                    count++;
                }
                var warm = 1f - Step(14f, 20f, Mathf.Abs(Mathf.DeltaAngle(6f, h * 360f)));
                weight[i] = warm * Step(0.1f, 0.16f, s) * (1f - Step(0.38f, 0.46f, s)) * Step(0.28f, 0.38f, v) * (1f - Step(0.58f, 0.66f, v));
                near[i] = warm * Step(0.05f, 0.09f, s) * (1f - Step(0.46f, 0.52f, s)) * Step(0.28f, 0.38f, v);
            }
            if (count == 0) return;
            var light = sum / count;

            // The patches: texels over 0.2 joined to their neighbours, each counted by its texels over 0.5.
            var patch = new int[pixels.Length];
            var kept = new List<bool> { false };
            var open = new Stack<int>();
            var minimum = Mathf.Max(1, Mathf.RoundToInt(pixels.Length * MinPatch));
            for (var start = 0; start < pixels.Length; start++)
            {
                if (weight[start] <= 0.2f || patch[start] != 0) continue;
                var id = kept.Count;
                var solid = 0;
                patch[start] = id;
                open.Push(start);
                while (open.Count > 0)
                {
                    var i = open.Pop();
                    if (weight[i] > 0.5f) solid++;
                    int x = i % width, y = i / width;
                    for (var k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var n = ny * width + nx;
                        if (weight[n] <= 0.2f || patch[n] != 0) continue;
                        patch[n] = id;
                        open.Push(n);
                    }
                }
                kept.Add(solid >= minimum);
            }

            // A kept patch's texels, then three rings of texels round them.
            var inside = new bool[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) inside[i] = patch[i] != 0 && kept[patch[i]];
            var reach = (bool[])inside.Clone();
            for (var ring = 0; ring < 3; ring++)
            {
                var grown = (bool[])reach.Clone();
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * width + x;
                        if (reach[i]) continue;
                        grown[i] = (x > 0 && reach[i - 1]) || (x + 1 < width && reach[i + 1]) || (y > 0 && reach[i - width]) || (y + 1 < height && reach[i + width]);
                    }
                reach = grown;
            }

            int changed = 0, patches = 0;
            foreach (var k in kept) if (k) patches++;
            for (var i = 0; i < pixels.Length; i++)
            {
                if (!reach[i]) continue;
                var share = inside[i] ? weight[i] : near[i];
                if (share <= 0f) continue;
                Color.RGBToHSV(pixels[i], out _, out _, out var v);
                var form = inside[i] ? 1f - 0.08f * (1f - Mathf.InverseLerp(0.44f, 0.58f, v)) : 1f;
                pixels[i] = Color.Lerp(pixels[i], new Color(light.x * form, light.y * form, light.z * form, pixels[i].a), share);
                if (share > 0.5f) changed++;
            }
            Debug.Log($"皮肤上烘进去的阴影：{patches} 块、{changed} 个像素（{100f * changed / pixels.Length:F2}%）提回亮面的肤色 #{ColorUtility.ToHtmlStringRGB(new Color(light.x, light.y, light.z))}");
        }

        static float Step(float from, float to, float x)
        {
            var t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
