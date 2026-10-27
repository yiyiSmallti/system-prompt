using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The white door at the deep exit turned into a mirror (story: Prologue_Deep_Exit, <<mirror on|off>>).
    // A second camera stands where the area's camera would be reflected through the glass and draws the
    // sea from there, clipped at the glass; the glass shows that picture (Resources/Shaders/Mirror.shader).
    // While the double is up, the reflection is not the player's avatar but the same player in the
    // latent-space battle coat (5-battle) they have not awakened into yet: the avatar is on a layer the
    // reflection camera skips, the double on one only it draws.
    public sealed class SeaMirror : MonoBehaviour
    {
        const int DoubleLayer = 28;  // drawn only in the mirror
        const int AvatarLayer = 29;  // drawn everywhere but the mirror
        const float FadeSeconds = 0.9f;

        Camera view;           // the area's camera
        Camera reflection;
        RenderTexture picture;
        Material glass;
        Transform avatar;
        SeaFigure twin;
        Vector3 point, normal; // the glass, and the side it faces
        float show, flash;

        // The glass is width × height, standing on the ground at `centre` and facing `facing`.
        public static SeaMirror Create(Transform parent, Camera view, Vector3 centre, Vector3 facing, float width, float height, SeaAvatar avatar, string gender)
        {
            var go = new GameObject("Mirror");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(-facing));
            var mirror = go.AddComponent<SeaMirror>();
            mirror.Build(view, centre, facing, width, height, avatar, gender);
            return mirror;
        }

        void Build(Camera view, Vector3 centre, Vector3 facing, float width, float height, SeaAvatar avatarOf, string gender)
        {
            this.view = view;
            point = centre;
            normal = facing.normalized;
            avatar = avatarOf.Root;

            picture = new RenderTexture(Mathf.Max(256, Screen.width / 2), Mathf.Max(144, Screen.height / 2), 24) { name = "Mirror" };
            var camGo = new GameObject("Mirror Camera");
            camGo.transform.SetParent(transform, false);
            reflection = camGo.AddComponent<Camera>();
            reflection.CopyFrom(view);
            reflection.targetTexture = picture;
            reflection.depth = view.depth - 1f;
            reflection.cullingMask = ~(1 << AvatarLayer);
            view.cullingMask &= ~(1 << DoubleLayer);

            glass = SeaArt.Plain("Mirror");
            glass.mainTexture = picture;
            Parts.Shape("Glass", transform, SeaArt.Quad(width, height), glass, new Vector3(0f, height / 2f, 0f));

            twin = SeaFigure.Player(gender, true, transform);
            SetLayer(twin.Root, DoubleLayer);
            twin.Root.gameObject.SetActive(false);
            Apply();
        }

        void OnDestroy()
        {
            twin?.Dispose();
            if (picture != null) picture.Release();
            if (view != null) view.cullingMask |= 1 << DoubleLayer;
            if (avatar != null) SetLayer(avatar, 0);
        }

        // Fades the reflection in from the plain door, with the double in it.
        public async Awaitable Open()
        {
            SetLayer(avatar, AvatarLayer);
            twin.Root.gameObject.SetActive(true);
            await Anim.Tween(FadeSeconds, t =>
            {
                show = t;
                flash = Mathf.Sin(t * Mathf.PI) * 0.5f;
                Apply();
            });
        }

        // A flash, and the reflection is only the player again.
        public async Awaitable Settle()
        {
            await Anim.Tween(0.18f, t =>
            {
                flash = t;
                Apply();
            }, Ease.Linear);
            SetLayer(avatar, 0);
            twin.Root.gameObject.SetActive(false);
            await Anim.Tween(0.5f, t =>
            {
                flash = 1f - t;
                Apply();
            });
        }

        void LateUpdate()
        {
            if (view == null) return;
            twin.Root.SetPositionAndRotation(avatar.position, avatar.rotation);
            twin.Animate(Time.unscaledDeltaTime, 0f);

            // The area's camera reflected through the glass, looking back through it at the sea in front.
            var eye = view.transform;
            reflection.transform.SetPositionAndRotation(Reflect(eye.position), Quaternion.LookRotation(ReflectDirection(eye.forward), ReflectDirection(eye.up)));
            reflection.fieldOfView = view.fieldOfView;
            reflection.aspect = (float)picture.width / picture.height;
            reflection.ResetProjectionMatrix();
            // Near plane on the glass, so nothing behind the door (the door itself) gets in the way.
            var toCamera = reflection.worldToCameraMatrix;
            var p = toCamera.MultiplyPoint(point + normal * 0.02f);
            var n = toCamera.MultiplyVector(normal).normalized;
            reflection.projectionMatrix = reflection.CalculateObliqueMatrix(new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n)));
            glass.SetMatrix("_MirrorVP", reflection.projectionMatrix * reflection.worldToCameraMatrix);
        }

        void Apply()
        {
            glass.SetFloat("_Show", show);
            glass.SetFloat("_Flash", flash);
        }

        Vector3 Reflect(Vector3 p) => p - 2f * Vector3.Dot(p - point, normal) * normal;

        Vector3 ReflectDirection(Vector3 d) => d - 2f * Vector3.Dot(d, normal) * normal;

        static void SetLayer(Transform root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
