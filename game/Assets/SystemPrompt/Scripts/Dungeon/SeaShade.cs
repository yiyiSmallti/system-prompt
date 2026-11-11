using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SystemPrompt.Dungeon
{
    // The figures and noise bodies drawn again on their own, by their SEADEPTH pass (Toon.shader, Glitch.shader), in a
    // command buffer the sea camera runs before its opaque pass (run at once from OnPreRender, it broke into the
    // camera's frame and cost a battle about 0.4 ms more): into a map from above, blurred into the shadows the seabed
    // takes (ShadeSoft.shader, SeaCommon.cginc SeaShadow), the shape of a kneel or a leap rather than a round spot,
    // tight under the feet and wider under what is higher; and the figures
    // alone into a depth from the camera, from which Toon.shader lights a figure's outline against what is behind it as
    // a clean rim of even width (DepthRim), none on the folds of a hoodie, inside the hair or across the face. Fitted
    // with the lens (SeaLens.Fit) to the maze's camera and the battle field's, so a lab view rendered from either has
    // them too; the mirror's camera keeps the old rim (_SeaFigureRim is 1 only while a fitted camera draws) and a layer
    // a camera leaves out (the mirror's double) is left out of both. A figure or noise body joins as it is made (Add)
    // and leaves when its root is destroyed. The camera's depth is a temporary texture, given back once the camera has
    // drawn; the map is one texture per camera, freed with it.
    [RequireComponent(typeof(Camera))]
    sealed class SeaShade : MonoBehaviour
    {
        const int MapSize = 1024;
        const float Far = 1e4f;        // what both read where nothing was drawn
        const float Strength = 0.45f;  // how much of the seabed's light a shadow takes
        // The shadows' light: steeper than the sea's, so they fall short, mostly under the feet.
        static readonly Vector3 Toward = new Vector3(0.25f, 1f, 0.2f).normalized;

        sealed class Body
        {
            public Transform Root;
            public bool Figure;            // also in the camera's depth, for the rim
            public int Count = -1;         // the root's hierarchyCount when its renderers were gathered
            public readonly List<Renderer> Parts = new List<Renderer>();
            public readonly List<int> Subs = new List<int>(); // each one's submesh count
        }

        static readonly List<Body> bodies = new List<Body>();
        static readonly List<Renderer> gathered = new List<Renderer>();
        static readonly List<Material> materials = new List<Material>();
        static readonly Dictionary<Shader, int> passes = new Dictionary<Shader, int>();

        static readonly int Across = Shader.PropertyToID("_SeaShadeAcross");

        Camera eye;
        CommandBuffer cb;
        RenderTexture map, soft, depth;
        Material soften;

        // A figure (SeaFigure: in the shadows and the rim) or a noise body (NoiseLook: in the shadows), whose renderers,
        // whatever is put on it later included, are drawn.
        public static void Add(Transform root, bool figure) => bodies.Add(new Body { Root = root, Figure = figure });

        public static SeaShade Fit(Camera camera)
        {
            var shade = camera.gameObject.AddComponent<SeaShade>();
            shade.eye = camera;
            shade.cb = new CommandBuffer { name = "Sea figures" };
            camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, shade.cb);
            return shade;
        }

        void OnDestroy()
        {
            if (eye != null && cb != null) eye.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, cb);
            cb?.Release();
            if (map != null) Destroy(map);
            if (soft != null) Destroy(soft);
            if (soften != null) Destroy(soften);
            if (depth != null) RenderTexture.ReleaseTemporary(depth);
            depth = null;
            Shader.SetGlobalVector("_SeaShade", Vector4.zero);
            Shader.SetGlobalFloat("_SeaFigureRim", 0f);
        }

        void OnPreRender()
        {
            // Nothing with the lab's switch off (SeaAtmos.Off): the sea as it was.
            if (!Gather() || SeaAtmos.Off)
            {
                cb.Clear();
                Shader.SetGlobalVector("_SeaShade", Vector4.zero);
                return;
            }
            if (map == null)
            {
                map = new RenderTexture(MapSize, MapSize, 16, RenderTextureFormat.RHalf) { name = "Sea shadows", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                map.Create();
                soft = new RenderTexture(MapSize, MapSize, 0, RenderTextureFormat.ARGB32) { name = "Sea shadows, soft", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                soft.Create();
                soften = new Material(Resources.Load<Shader>("Shaders/ShadeSoft"));
            }
            depth = RenderTexture.GetTemporary(eye.pixelWidth, eye.pixelHeight, 16, RenderTextureFormat.RHalf);
            depth.filterMode = FilterMode.Point;

            // The map: 14 m square round the player in the maze, 20 m round the field's middle in a battle (no scenery
            // thins out there: _SeaFade 0), its middle moved a texel at a time so the shadows don't crawl as it follows.
            var glow = Shader.GetGlobalVector("_SeaGlowPos");
            var half = Shader.GetGlobalVector("_SeaFade").x > 0f ? 7f : 10f;
            var look = Quaternion.LookRotation(-Toward, Vector3.forward);
            var texel = half * 2f / MapSize;
            var centre = Quaternion.Inverse(look) * new Vector3(glow.x, 0f, glow.z);
            centre.x = Mathf.Round(centre.x / texel) * texel;
            centre.y = Mathf.Round(centre.y / texel) * texel;
            var from = look * centre + Toward * 40f;
            // World to the light's view, looking down -z as a camera's does.
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(from, look, Vector3.one).inverse;

            cb.Clear();
            cb.SetRenderTarget(map);
            cb.ClearRenderTarget(true, true, new Color(Far, 0f, 0f, 0f));
            cb.SetViewProjectionMatrices(view, Matrix4x4.Ortho(-half, half, -half, half, 0.1f, 80f));
            Draw(false);
            // The seabed's own depth along the light, from its plane (y = 0) in the map's view.
            var back = view.inverse;
            cb.SetGlobalVector("_ShadeFloor", new Vector4(back.m10 / back.m12, back.m11 / back.m12, back.m13 / back.m12, half * 2f));
            cb.GetTemporaryRT(Across, MapSize, MapSize, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            cb.Blit(map, Across, soften, 0);
            cb.Blit(Across, soft, soften, 1);
            cb.ReleaseTemporaryRT(Across);
            cb.SetRenderTarget(depth);
            cb.ClearRenderTarget(true, true, new Color(Far, 0f, 0f, 0f));
            cb.SetViewProjectionMatrices(eye.worldToCameraMatrix, eye.projectionMatrix);
            Draw(true);
            // Back to the camera's own picture, as it was.
            cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cb.SetViewProjectionMatrices(eye.worldToCameraMatrix, eye.projectionMatrix);

            Shader.SetGlobalTexture("_SeaShadeSoft", soft);
            Shader.SetGlobalMatrix("_SeaShadeView", view);
            Shader.SetGlobalVector("_SeaShade", new Vector4(0.5f / half, 1f / MapSize, 0f, Strength));
            Shader.SetGlobalTexture("_SeaFigureDepth", depth);
            Shader.SetGlobalFloat("_SeaFigureRim", 1f);
        }

        void OnPostRender()
        {
            Shader.SetGlobalFloat("_SeaFigureRim", 0f);
            if (depth != null) RenderTexture.ReleaseTemporary(depth);
            depth = null;
        }

        // Each body's renderers again whenever something was put on it or taken off (a phone, a spoon, a burst), the
        // gone ones dropped; whether any is left to draw.
        static bool Gather()
        {
            var any = false;
            for (var i = bodies.Count - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b.Root == null)
                {
                    bodies.RemoveAt(i);
                    continue;
                }
                any |= b.Root.gameObject.activeInHierarchy;
                if (b.Root.hierarchyCount == b.Count) continue;
                b.Count = b.Root.hierarchyCount;
                b.Root.GetComponentsInChildren(true, gathered);
                b.Parts.Clear();
                b.Subs.Clear();
                foreach (var r in gathered)
                {
                    var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
                    if (mesh == null) continue;
                    b.Parts.Add(r);
                    b.Subs.Add(mesh.subMeshCount);
                }
                gathered.Clear();
            }
            return any;
        }

        void Draw(bool figuresOnly)
        {
            var mask = eye.cullingMask;
            foreach (var b in bodies)
            {
                if ((figuresOnly && !b.Figure) || !b.Root.gameObject.activeInHierarchy) continue;
                for (var k = 0; k < b.Parts.Count; k++)
                {
                    var r = b.Parts[k];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || (mask & (1 << r.gameObject.layer)) == 0) continue;
                    r.GetSharedMaterials(materials);
                    for (var i = 0; i < materials.Count; i++)
                    {
                        var m = materials[i];
                        if (m == null) continue;
                        var shader = m.shader;
                        if (!passes.TryGetValue(shader, out var pass)) passes[shader] = pass = m.FindPass("SEADEPTH");
                        if (pass >= 0) cb.DrawRenderer(r, m, Mathf.Min(i, b.Subs[k] - 1), pass);
                    }
                }
            }
            materials.Clear();
        }
    }
}
