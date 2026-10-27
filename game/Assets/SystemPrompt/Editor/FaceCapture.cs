using System;
using System.IO;
using SystemPrompt.Dungeon;
using UnityEditor;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // Takes each character's face straight from the front, in the face box SeaFigure lays expressions in:
    // the colour (Tools/FaceArt/<model>_capture.png, which Tools/face_expressions.py draws the expressions
    // over) and how deep the face is behind the box's front plane (Resources/Models/Characters/<model>_FaceDepth.png,
    // which the toon shader checks so the expressions stay on the face), and where the box's surfaces are on
    // the base colour (Tools/FaceArt/<model>_mesh.bin, which Tools/face_eyes.py carries the repainted eyes
    // back through). Rays are cast at the model posed as in the first frame of its idle, the pose the box is
    // fixed in.
    static class FaceCapture
    {
        const int Size = 512;

        [MenuItem("System Prompt/采集角色正脸")]
        public static void Run()
        {
            var art = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/FaceArt"));
            Directory.CreateDirectory(art);
            var holder = new GameObject("Face Capture");
            try
            {
                Capture(SeaFigure.Player("male", false, holder.transform), art);
                Capture(SeaFigure.Player("female", false, holder.transform), art);
                Capture(SeaFigure.Player("male", true, holder.transform), art);
                Capture(SeaFigure.Player("female", true, holder.transform), art);
                Capture(SeaFigure.DeepSeek(false, holder.transform), art);
                Capture(SeaFigure.DeepSeek(true, holder.transform), art);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
            AssetDatabase.Refresh();
        }

        static void Capture(SeaFigure figure, string art)
        {
            // A VRoid model's face changes by its own blend shapes, with nothing drawn over it.
            if (figure.ShapedFace)
            {
                figure.Dispose();
                return;
            }
            var model = figure.Root.name;
            var skin = figure.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            var posed = new Mesh();
            // Baked at the size it is drawn, around the renderer's position and turned as it is.
            skin.BakeMesh(posed);
            var target = new GameObject("Collider");
            target.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            var collider = target.AddComponent<MeshCollider>();
            collider.sharedMesh = posed;

            // The base colour as imported, read back through a render texture since it is compressed.
            var texture = (Texture2D)skin.sharedMaterial.mainTexture;
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var albedo = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            albedo.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            var box = figure.FaceToWorld;
            var depth = Vector3.Distance(box.MultiplyPoint(new Vector3(0.5f, 0.5f, 0f)), box.MultiplyPoint(new Vector3(0.5f, 0.5f, 1f)));
            var colour = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var depths = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            var hits = 0;
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var u = (x + 0.5f) / Size;
                    var v = (y + 0.5f) / Size;
                    var from = box.MultiplyPoint(new Vector3(u, v, 0f));
                    var ray = new Ray(from, box.MultiplyPoint(new Vector3(u, v, 1f)) - from);
                    if (collider.Raycast(ray, out var hit, depth))
                    {
                        hits++;
                        var c = albedo.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y);
                        c.a = 1f;
                        colour.SetPixel(x, y, c);
                        var w = hit.distance / depth;
                        depths.SetPixel(x, y, new Color(w, w, w));
                    }
                    else
                    {
                        colour.SetPixel(x, y, Color.clear);
                        depths.SetPixel(x, y, Color.white);
                    }
                }
            File.WriteAllBytes(Path.Combine(art, model + "_capture.png"), colour.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(Application.dataPath, $"SystemPrompt/Resources/Models/Characters/{model}_FaceDepth.png"), depths.EncodeToPNG());
            var triangles = WriteFaceMesh(posed, target.transform.localToWorldMatrix, box.inverse, Path.Combine(art, model + "_mesh.bin"));
            Debug.Log($"{model}: 正脸 {Size}×{Size}，{hits * 100 / (Size * Size)}% 打到模型，框深 {depth:F3} m，框里 {triangles} 个三角形");

            UnityEngine.Object.DestroyImmediate(albedo);
            UnityEngine.Object.DestroyImmediate(colour);
            UnityEngine.Object.DestroyImmediate(depths);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(posed);
            figure.Dispose();
        }

        // Where the face box's surfaces are on the base colour, for Tools/face_eyes.py, which paints the eyes
        // from the front and carries them back onto the texture: every triangle with a corner in or near the
        // box, as three corners of five little-endian floats each, the corner's uv on the base colour and then
        // (u, v, w) in the box.
        static int WriteFaceMesh(Mesh posed, Matrix4x4 toWorld, Matrix4x4 worldToBox, string path)
        {
            var vertices = posed.vertices;
            var uvs = posed.uv;
            var inBox = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) inBox[i] = worldToBox.MultiplyPoint(toWorld.MultiplyPoint3x4(vertices[i]));
            bool Near(Vector3 p) => p.x > -0.1f && p.x < 1.1f && p.y > -0.1f && p.y < 1.1f && p.z > -0.2f && p.z < 1.2f;
            var triangles = posed.triangles;
            var written = 0;
            using (var writer = new BinaryWriter(File.Create(path)))
                for (var t = 0; t < triangles.Length; t += 3)
                {
                    if (!Near(inBox[triangles[t]]) && !Near(inBox[triangles[t + 1]]) && !Near(inBox[triangles[t + 2]])) continue;
                    for (var k = 0; k < 3; k++)
                    {
                        var i = triangles[t + k];
                        writer.Write(uvs[i].x);
                        writer.Write(uvs[i].y);
                        writer.Write(inBox[i].x);
                        writer.Write(inBox[i].y);
                        writer.Write(inBox[i].z);
                    }
                    written++;
                }
            return written;
        }
    }
}
