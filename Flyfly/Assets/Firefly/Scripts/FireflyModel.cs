using UnityEngine;
using UnityEngine.Rendering;

namespace Firefly
{
    /// <summary>Small readable insect assembled from shared materials and lightweight procedural parts.</summary>
    public sealed class FireflyModel : MonoBehaviour
    {
        private static Material bodyMaterial;
        private static Material headMaterial;
        private static Material abdomenMaterial;
        private static Material wingMaterial;
        private static Material haloMaterial;
        private Quaternion leftWingRest;
        private Quaternion rightWingRest;
        private float wingTime;
        private Camera viewCamera;

        [field: SerializeField] public Transform VisualRoot { get; private set; }
        [field: SerializeField] public Renderer Abdomen { get; private set; }
        [field: SerializeField] public Light GlowLight { get; private set; }
        [field: SerializeField] public Renderer Halo { get; private set; }
        [field: SerializeField] public Transform LeftWing { get; private set; }
        [field: SerializeField] public Transform RightWing { get; private set; }

        public static FireflyModel Build(Transform parent)
        {
            EnsureMaterials();
            GameObject root = new GameObject("Firefly visual");
            root.transform.SetParent(parent, false);
            FireflyModel model = root.AddComponent<FireflyModel>();
            model.VisualRoot = root.transform;
            model.Abdomen = Part(root.transform, "Luminous abdomen", new Vector3(0f, 0f, -0.19f), new Vector3(0.22f, 0.16f, 0.36f), abdomenMaterial);
            Part(root.transform, "Thorax", new Vector3(0f, 0.015f, 0.11f), new Vector3(0.18f, 0.13f, 0.27f), bodyMaterial);
            Part(root.transform, "Head", new Vector3(0f, 0.025f, 0.31f), new Vector3(0.145f, 0.12f, 0.135f), headMaterial);
            Part(root.transform, "Left eye", new Vector3(-0.065f, 0.038f, 0.354f), Vector3.one * 0.052f, bodyMaterial);
            Part(root.transform, "Right eye", new Vector3(0.065f, 0.038f, 0.354f), Vector3.one * 0.052f, bodyMaterial);
            Tendril(root.transform, "Left antenna", new Vector3(-0.047f, 0.055f, 0.36f), new Vector3(-0.1f, 0.083f, 0.46f));
            Tendril(root.transform, "Right antenna", new Vector3(0.047f, 0.055f, 0.36f), new Vector3(0.1f, 0.083f, 0.46f));

            model.LeftWing = Wing(root.transform, "Left wing", -1f);
            model.RightWing = Wing(root.transform, "Right wing", 1f);
            model.leftWingRest = model.LeftWing.localRotation;
            model.rightWingRest = model.RightWing.localRotation;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int leg = 0; leg < 3; leg++)
                {
                    float z = 0.18f - leg * 0.095f;
                    Tendril(root.transform, "Leg", new Vector3(side * 0.065f, -0.035f, z), new Vector3(side * 0.145f, -0.115f, z - 0.07f));
                }
            }

            GameObject lightObject = new GameObject("Abdomen light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, -0.04f, -0.26f);
            model.GlowLight = lightObject.AddComponent<Light>();
            model.GlowLight.type = LightType.Point;
            model.GlowLight.range = 1.65f;
            model.GlowLight.intensity = 0.3f;
            model.GlowLight.color = new Color(0.79f, 1f, 0.24f);
            model.GlowLight.shadows = LightShadows.None;
            model.GlowLight.renderMode = LightRenderMode.ForcePixel;

            GameObject haloObject = new GameObject("Soft glow halo");
            haloObject.transform.SetParent(root.transform, false);
            haloObject.transform.localPosition = new Vector3(0f, 0f, -0.27f);
            haloObject.transform.localScale = Vector3.one * 0.7f;
            haloObject.AddComponent<MeshFilter>().sharedMesh = BuildHaloMesh();
            MeshRenderer haloRenderer = haloObject.AddComponent<MeshRenderer>();
            haloRenderer.sharedMaterial = haloMaterial;
            haloRenderer.shadowCastingMode = ShadowCastingMode.Off;
            haloRenderer.receiveShadows = false;
            model.Halo = haloRenderer;
            return model;
        }

        private void Awake()
        {
            viewCamera = Camera.main;
            if (LeftWing != null)
                leftWingRest = LeftWing.localRotation;
            if (RightWing != null)
                rightWingRest = RightWing.localRotation;
        }

        private void LateUpdate()
        {
            wingTime += Time.deltaTime;
            float flap = Mathf.Sin(wingTime * 43f) * 24f;
            if (LeftWing != null)
                LeftWing.localRotation = leftWingRest * Quaternion.Euler(0f, 0f, flap);
            if (RightWing != null)
                RightWing.localRotation = rightWingRest * Quaternion.Euler(0f, 0f, -flap);
            if (viewCamera == null)
                viewCamera = Camera.main;
            if (Halo != null && viewCamera != null)
                Halo.transform.rotation = viewCamera.transform.rotation;
        }

        private static Transform Wing(Transform root, string name, float side)
        {
            GameObject hinge = new GameObject(name + " hinge");
            hinge.transform.SetParent(root, false);
            hinge.transform.localPosition = new Vector3(side * 0.055f, 0.075f, 0.12f);
            hinge.transform.localRotation = Quaternion.Euler(0f, side * -22f, side * 14f);
            GameObject membrane = new GameObject(name + " membrane");
            membrane.transform.SetParent(hinge.transform, false);
            membrane.AddComponent<MeshFilter>().sharedMesh = BuildWingMesh(side);
            MeshRenderer wingRenderer = membrane.AddComponent<MeshRenderer>();
            wingRenderer.sharedMaterial = wingMaterial;
            wingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            wingRenderer.receiveShadows = false;
            // Sparse fine veins give the translucent membrane an insect silhouette.
            WingVein(hinge.transform, new Vector3(side * 0.015f, 0.007f, 0f), new Vector3(side * 0.245f, 0.007f, -0.17f));
            WingVein(hinge.transform, new Vector3(side * 0.07f, 0.011f, -0.04f), new Vector3(side * 0.23f, 0.004f, 0.035f));
            WingVein(hinge.transform, new Vector3(side * 0.13f, 0.011f, -0.085f), new Vector3(side * 0.285f, 0.004f, -0.07f));
            return hinge.transform;
        }

        private static Mesh BuildWingMesh(float side)
        {
            // A slightly cupped, tapered membrane; the spread remains about 0.7 units.
            Vector2[] outline =
            {
                new Vector2(0f, 0f), new Vector2(0.05f, 0.065f),
                new Vector2(0.13f, 0.09f), new Vector2(0.225f, 0.042f),
                new Vector2(0.285f, -0.035f), new Vector2(0.295f, -0.09f),
                new Vector2(0.255f, -0.175f), new Vector2(0.185f, -0.218f),
                new Vector2(0.12f, -0.19f), new Vector2(0.055f, -0.105f),
                new Vector2(0.016f, -0.04f)
            };
            Vector3[] vertices = new Vector3[outline.Length + 1];
            Vector2[] uv = new Vector2[vertices.Length];
            vertices[0] = new Vector3(side * 0.13f, 0.012f, -0.06f);
            uv[0] = new Vector2(0.44f, 0.52f);
            int[] triangles = new int[outline.Length * 3];
            for (int i = 0; i < outline.Length; i++)
            {
                vertices[i + 1] = new Vector3(side * outline[i].x, 0f, outline[i].y);
                uv[i + 1] = new Vector2(outline[i].x / 0.3f, (outline[i].y + 0.22f) / 0.31f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = side > 0f ? i + 1 : (i + 1) % outline.Length + 1;
                triangles[i * 3 + 2] = side > 0f ? (i + 1) % outline.Length + 1 : i + 1;
            }
            Mesh mesh = new Mesh { name = side < 0f ? "Firefly left membrane" : "Firefly right membrane" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void WingVein(Transform root, Vector3 from, Vector3 to)
        {
            GameObject vein = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vein.name = "Wing vein";
            vein.transform.SetParent(root, false);
            vein.transform.localPosition = (from + to) * 0.5f;
            vein.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
            vein.transform.localScale = new Vector3(0.0035f, (to - from).magnitude * 0.5f, 0.0035f);
            Collider collider = vein.GetComponent<Collider>();
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
            Renderer renderer = vein.GetComponent<Renderer>();
            renderer.sharedMaterial = bodyMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Renderer Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            // Unity's primitive sphere diameter is one; values below are deliberately insect-sized.
            part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>();
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return renderer;
        }

        private static void Tendril(Transform root, string name, Vector3 from, Vector3 to)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            part.name = name;
            part.transform.SetParent(root, false);
            part.transform.localPosition = (from + to) * 0.5f;
            part.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
            part.transform.localScale = new Vector3(0.012f, (to - from).magnitude * 0.5f, 0.012f);
            Collider collider = part.GetComponent<Collider>();
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = bodyMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void EnsureMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (bodyMaterial == null)
                bodyMaterial = LitMaterial(lit, "Firefly bronze shell", new Color(0.34f, 0.29f, 0.21f), 0.4f, 0.1f);
            if (headMaterial == null)
                headMaterial = LitMaterial(lit, "Firefly dark head", new Color(0.18f, 0.22f, 0.23f), 0.5f, 0.05f);
            if (abdomenMaterial == null)
            {
                abdomenMaterial = LitMaterial(lit, "Firefly luminous abdomen", new Color(0.16f, 0.22f, 0.055f), 0.48f, 0f);
                abdomenMaterial.EnableKeyword("_EMISSION");
                abdomenMaterial.SetColor("_EmissionColor", Color.black);
                abdomenMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (wingMaterial == null)
            {
                Shader moonlitWing = Shader.Find("Firefly/MoonlitWing");
                wingMaterial = LitMaterial(moonlitWing != null ? moonlitWing : lit, "Firefly translucent wings", new Color(0.65f, 0.73f, 0.82f, 0.58f), 0.65f, 0.05f);
                SetTransparent(wingMaterial, false);
                if (wingMaterial.HasProperty("_Cull"))
                    wingMaterial.SetFloat("_Cull", (float)CullMode.Off);
            }
            if (haloMaterial == null)
            {
                Shader haloShader = Shader.Find("Firefly/Glow");
                if (haloShader == null)
                    haloShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                haloMaterial = new Material(haloShader) { name = "Firefly soft halo" };
                haloMaterial.SetColor("_BaseColor", new Color(0.79f, 1f, 0.24f, 0.15f));
                SetTransparent(haloMaterial, true);
                if (haloMaterial.HasProperty("_Cull"))
                    haloMaterial.SetFloat("_Cull", (float)CullMode.Off);
            }
        }

        private static Material LitMaterial(Shader shader, string name, Color color, float smoothness, float metallic)
        {
            Material material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            return material;
        }

        private static void SetTransparent(Material material, bool additive)
        {
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Mesh BuildHaloMesh()
        {
            const int segments = 40;
            Vector3[] vertices = new Vector3[segments + 1];
            Color[] colors = new Color[segments + 1];
            Vector2[] uv = new Vector2[segments + 1];
            int[] triangles = new int[segments * 3];
            colors[0] = Color.white;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.5f;
                colors[i + 1] = new Color(1f, 1f, 1f, 0f);
                uv[i + 1] = new Vector2(vertices[i + 1].x + 0.5f, vertices[i + 1].y + 0.5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            Mesh mesh = new Mesh { name = "Firefly radial halo" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
