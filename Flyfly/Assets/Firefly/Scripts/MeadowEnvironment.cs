using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Firefly
{
    /// <summary>A deterministic, asset-free art pass for the playable moonlit clearing.</summary>
    [DisallowMultipleComponent]
    public sealed class MeadowEnvironment : MonoBehaviour
    {
        [SerializeField] private int seed = 48193;
        [SerializeField, HideInInspector] private Transform generatedRoot;
        [SerializeField, HideInInspector] private Material soilMaterial;
        [SerializeField, HideInInspector] private Material foliageMaterial;
        [SerializeField, HideInInspector] private Material canopyMaterial;
        [SerializeField, HideInInspector] private Material barkMaterial;
        [SerializeField, HideInInspector] private Material stoneMaterial;
        [SerializeField, HideInInspector] private Material mistMaterial;

        private readonly List<UnityEngine.Object> ownedResources = new List<UnityEngine.Object>();
        private System.Random random;

        public void Build()
        {
            ClearGenerated();
            random = new System.Random(seed);
            generatedRoot = new GameObject("Generated Meadow").transform;
            generatedRoot.SetParent(transform, false);
            CreateMaterials();
            CreateGround();
            CreateGrassAndFerns();
            CreateForest();
            CreateLandmarks();
            CreateOuterForest();
            CreateMist();
        }

        public static float GroundHeight(float x, float z)
        {
            float broad = Mathf.PerlinNoise(x * 0.075f + 27.3f, z * 0.069f + 12.8f);
            float detail = Mathf.PerlinNoise(x * 0.29f + 81.6f, z * 0.26f + 74.2f);
            return (broad - 0.5f) * 0.34f + (detail - 0.5f) * 0.09f - 0.04f;
        }

        private void ClearGenerated()
        {
            if (generatedRoot != null)
            {
                generatedRoot.gameObject.SetActive(false);
                DestroyOwned(generatedRoot.gameObject);
            }
            for (int i = 0; i < ownedResources.Count; i++)
            {
                UnityEngine.Object resource = ownedResources[i];
                if (resource == null) continue;
                // Editor scene generation may have promoted these objects to real assets.
                #if UNITY_EDITOR
                if (UnityEditor.AssetDatabase.Contains(resource)) continue;
                #endif
                DestroyOwned(resource);
            }
            ownedResources.Clear();
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            ownedResources.Add(value);
            return value;
        }

        private float Range(float min, float max) => min + (max - min) * (float)random.NextDouble();
        private bool Chance(float probability) => random.NextDouble() < probability;
        private Vector3 OnGround(float x, float z) => new Vector3(x, GroundHeight(x, z), z);

        private Material LitMaterial(string name, Color color, float smoothness)
        {
            Material material = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name });
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
            return material;
        }

        private void CreateMaterials()
        {
            Texture2D soil = CreateOrganicTexture("Damp meadow soil", 512, false);
            Texture2D bark = CreateOrganicTexture("Ridged forest bark", 256, true);
            Texture2D mist = CreateNoiseTexture();
            soilMaterial = LitMaterial("Damp earth and low moss", new Color(0.41f, 0.49f, 0.4f), 0.22f);
            soilMaterial.SetTexture("_BaseMap", soil);
            soilMaterial.SetTextureScale("_BaseMap", new Vector2(9f, 8f));
            barkMaterial = LitMaterial("Blue grey weathered bark", new Color(0.28f, 0.31f, 0.28f), 0.14f);
            barkMaterial.SetTexture("_BaseMap", bark);
            stoneMaterial = LitMaterial("Rain darkened stones", new Color(0.16f, 0.21f, 0.22f), 0.27f);
            foliageMaterial = Own(new Material(Shader.Find("Firefly/Meadow Foliage")) { name = "Moonlit meadow leaves" });
            foliageMaterial.SetColor("_BaseColor", Color.white);
            foliageMaterial.SetFloat("_WindStrength", 0.055f);
            foliageMaterial.SetFloat("_WindSpeed", 0.74f);
            foliageMaterial.SetFloat("_Smoothness", 0.3f);
            foliageMaterial.SetFloat("_Translucency", 0.28f);
            canopyMaterial = Own(new Material(foliageMaterial) { name = "Forest canopy leaves" });
            canopyMaterial.SetFloat("_WindStrength", 0.032f);
            canopyMaterial.SetFloat("_Smoothness", 0.15f);
            mistMaterial = Own(new Material(Shader.Find("Firefly/Meadow Mist")) { name = "Quiet ground mist" });
            mistMaterial.SetTexture("_NoiseTex", mist);
            mistMaterial.SetColor("_BaseColor", new Color(0.14f, 0.22f, 0.24f, 0.055f));
        }

        private Texture2D CreateOrganicTexture(string name, int size, bool bark)
        {
            Texture2D texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat });
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;
                float n = PeriodicNoise(u, v, bark ? 8f : 11f, bark ? 2f : 11f);
                float fine = PeriodicNoise(u, v, bark ? 36f : 70f, bark ? 6f : 70f);
                float grit = Range(-0.07f, 0.07f);
                Color low = bark ? new Color(0.11f, 0.12f, 0.105f) : new Color(0.2f, 0.255f, 0.16f);
                Color high = bark ? new Color(0.46f, 0.44f, 0.35f) : new Color(0.46f, 0.51f, 0.345f);
                float value = Mathf.Clamp01(n * 0.63f + fine * 0.32f + grit);
                pixels[y * size + x] = Color.Lerp(low, high, value);
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        // Blend four shifted noise samples to make the tiling texture continuous.
        private static float PeriodicNoise(float u, float v, float sx, float sy)
        {
            float a = Mathf.PerlinNoise(u * sx + 91.3f, v * sy + 13.4f);
            float b = Mathf.PerlinNoise((u - 1f) * sx + 91.3f, v * sy + 13.4f);
            float c = Mathf.PerlinNoise(u * sx + 91.3f, (v - 1f) * sy + 13.4f);
            float d = Mathf.PerlinNoise((u - 1f) * sx + 91.3f, (v - 1f) * sy + 13.4f);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private Texture2D CreateNoiseTexture()
        {
            const int size = 128;
            Texture2D texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Tiling wisps", wrapMode = TextureWrapMode.Repeat });
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float noise = PeriodicNoise(u, v, 7f, 7f) * 0.7f + PeriodicNoise(u, v, 17f, 17f) * 0.3f;
                pixels[y * size + x] = new Color(noise, noise, noise, 1f);
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private void CreateGround()
        {
            MeadowGeometry mesh = new MeadowGeometry();
            const int columns = 80, rows = 72;
            for (int z = 0; z <= rows; z++)
            for (int x = 0; x <= columns; x++)
            {
                float px = -20f + x * 0.5f, pz = -16f + z * 0.5f;
                float dx = (GroundHeight(px - 0.05f, pz) - GroundHeight(px + 0.05f, pz)) / 0.1f;
                float dz = (GroundHeight(px, pz - 0.05f) - GroundHeight(px, pz + 0.05f)) / 0.1f;
                mesh.Vertex(OnGround(px, pz), new Vector3(dx, 1f, dz).normalized, new Vector2((float)x / columns, (float)z / rows), Color.white);
            }
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < columns; x++)
            {
                int a = z * (columns + 1) + x;
                mesh.Triangle(a, a + columns + 1, a + 1);
                mesh.Triangle(a + 1, a + columns + 1, a + columns + 2);
            }
            GameObject ground = RenderMesh("Uneven damp ground", mesh, soilMaterial);
            ground.AddComponent<MeshCollider>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;

            // A coarse surrounding floor keeps the distant orbit camera from revealing
            // the rectangular playable terrain cut-off. It adds no grass or collisions.
            MeadowGeometry outerGround = new MeadowGeometry();
            const int outerColumns = 44;
            for (int z = 0; z <= outerColumns; z++)
            for (int x = 0; x <= outerColumns; x++)
            {
                float px = -88f + x * 4f, pz = -88f + z * 4f;
                float dx = (GroundHeight(px - 0.05f, pz) - GroundHeight(px + 0.05f, pz)) / 0.1f;
                float dz = (GroundHeight(px, pz - 0.05f) - GroundHeight(px, pz + 0.05f)) / 0.1f;
                // UVs preserve the central soil's world scale and tile phase.
                outerGround.Vertex(OnGround(px, pz), new Vector3(dx, 1f, dz).normalized,
                    new Vector2((px + 20f) / 40f, (pz + 16f) / 36f), Color.white);
            }
            for (int z = 0; z < outerColumns; z++)
            for (int x = 0; x < outerColumns; x++)
            {
                float px = -88f + x * 4f, pz = -88f + z * 4f;
                if (px >= -20f && px < 20f && pz >= -16f && pz < 20f) continue;
                int a = z * (outerColumns + 1) + x;
                outerGround.Triangle(a, a + outerColumns + 1, a + 1);
                outerGround.Triangle(a + 1, a + outerColumns + 1, a + outerColumns + 2);
            }
            RenderMesh("Low detail surrounding forest floor", outerGround, soilMaterial)
                .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void CreateGrassAndFerns()
        {
            Transform plants = new GameObject("Meadow vegetation chunks").transform;
            plants.SetParent(generatedRoot, false);
            for (int cz = 0; cz < 5; cz++)
            for (int cx = 0; cx < 5; cx++)
            {
                MeadowGeometry geometry = new MeadowGeometry();
                float xmin = -20f + cx * 8f, zmin = -16f + cz * 7.2f;
                for (int tuft = 0; tuft < 340; tuft++)
                {
                    float x = Range(xmin, xmin + 8f), z = Range(zmin, zmin + 7.2f);
                    float patch = Mathf.PerlinNoise(x * 0.28f + 18.2f, z * 0.25f + 44.1f);
                    float clearing = Mathf.Exp(-((x + 1f) * (x + 1f) / 68f + (z + 2f) * (z + 2f) / 75f));
                    if (Chance(0.12f + clearing * 0.3f + (1f - patch) * 0.11f)) continue;
                    Vector3 origin = OnGround(x, z);
                    int blades = random.Next(3, 6);
                    bool tall = patch > 0.64f && Chance(0.25f);
                    float height = tall ? Range(0.55f, 0.88f) : Range(0.14f, 0.4f);
                    Color tip = Color.Lerp(new Color(0.16f, 0.25f, 0.12f), new Color(0.29f, 0.36f, 0.2f), Range(0f, 1f));
                    for (int blade = 0; blade < blades; blade++)
                    {
                        float angle = Range(0f, Mathf.PI * 2f);
                        Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                        Vector3 stem = Vector3.up * height * Range(0.72f, 1.17f) + outward * height * Range(0.22f, 0.53f);
                        Vector3 basePosition = origin + outward * Range(0.015f, 0.085f);
                        geometry.PointedLeaf(basePosition, stem, Vector3.Cross(outward, Vector3.up), Range(0.014f, 0.03f), tip, true);
                    }
                    if (Chance(0.012f) && clearing < 0.65f) AddFern(geometry, origin, Range(0.34f, 0.68f));
                    if (Chance(0.19f)) AddGroundCover(geometry, origin, Range(0.16f, 0.29f));
                }
                GameObject chunk = RenderMesh($"Grass and fern {cx}-{cz}", geometry, foliageMaterial, plants);
                // Grass casts little useful shadow at this scale; canopy and landmarks do.
                chunk.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private void AddFern(MeadowGeometry mesh, Vector3 origin, float scale)
        {
            Color color = Color.Lerp(new Color(0.11f, 0.21f, 0.13f), new Color(0.21f, 0.31f, 0.16f), Range(0f, 1f));
            int fronds = random.Next(6, 10);
            for (int f = 0; f < fronds; f++)
            {
                float angle = f * Mathf.PI * 2f / fronds + Range(-0.15f, 0.15f);
                Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 side = Vector3.Cross(outward, Vector3.up);
                float reach = scale * Range(0.75f, 1.13f);
                Vector3 previous = origin;
                for (int section = 1; section <= 6; section++)
                {
                    float t = section / 6f;
                    Vector3 current = origin + outward * reach * t + Vector3.up * scale * (0.12f + Mathf.Sin(t * Mathf.PI * 0.72f) * 0.75f);
                    mesh.Tube(previous, current, 0.009f * (1f - t * 0.7f), 0.006f * (1f - t * 0.5f), 3, color, false);
                    float leafSize = scale * (0.24f * (1f - t) + 0.035f);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 direction = side * sign * leafSize + outward * leafSize * 0.48f + Vector3.up * leafSize * 0.13f;
                        mesh.PointedLeaf(current, direction, outward, leafSize * 0.21f, color, false);
                    }
                    previous = current;
                }
            }
        }

        private void AddBroadPlant(MeadowGeometry mesh, Vector3 origin, float scale)
        {
            Color color = new Color(0.13f, 0.25f, 0.17f);
            for (int leaf = 0; leaf < 5; leaf++)
            {
                float angle = leaf * Mathf.PI * 0.4f + Range(-0.3f, 0.3f);
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0.35f, Mathf.Cos(angle)).normalized;
                mesh.PointedLeaf(origin + Vector3.up * 0.04f, direction * scale, Vector3.Cross(direction, Vector3.up), scale * 0.21f, color, false);
            }
        }

        private void AddGroundCover(MeadowGeometry mesh, Vector3 origin, float scale)
        {
            Color color = Color.Lerp(new Color(0.15f, 0.255f, 0.15f), new Color(0.27f, 0.36f, 0.18f), Range(0f, 1f));
            int stalks = random.Next(2, 4);
            for (int stalk = 0; stalk < stalks; stalk++)
            {
                float spreadAngle = Range(0f, Mathf.PI * 2f);
                float spread = scale * Range(0.2f, 0.8f);
                Vector3 basePosition = origin + new Vector3(Mathf.Sin(spreadAngle), 0f, Mathf.Cos(spreadAngle)) * spread;
                basePosition.y = GroundHeight(basePosition.x, basePosition.z) + Range(0.075f, 0.16f);
                float rotation = Range(0f, Mathf.PI * 2f);
                for (int leaf = 0; leaf < 3; leaf++)
                {
                    float angle = rotation + leaf * Mathf.PI * 2f / 3f + Range(-0.11f, 0.11f);
                    Vector3 outward = new Vector3(Mathf.Sin(angle), Range(-0.09f, 0.19f), Mathf.Cos(angle)).normalized;
                    float length = scale * Range(0.72f, 1.12f);
                    Color tint = color * Range(0.88f, 1.09f);
                    tint.a = 1f;
                    mesh.RoundedLeaf(basePosition, outward * length, Vector3.Cross(outward, Vector3.up), length * Range(0.36f, 0.44f), tint);
                }
            }
        }

        private void CreateForest()
        {
            Transform forest = new GameObject("Layered forest edge").transform;
            forest.SetParent(generatedRoot, false);
            for (int group = 0; group < 5; group++)
            {
                MeadowGeometry wood = new MeadowGeometry();
                MeadowGeometry leaves = new MeadowGeometry();
                int count = group < 3 ? 14 : 10;
                for (int tree = 0; tree < count; tree++)
                {
                    float x, z;
                    if (group < 3)
                    {
                        x = -19f + tree * 38f / (count - 1) + Range(-0.9f, 0.9f);
                        z = 13.3f + group * 2.85f + Range(-0.75f, 0.75f);
                    }
                    else
                    {
                        x = (group == 3 ? -1f : 1f) * Range(18.8f, 20f);
                        z = -12.2f + tree * 28f / (count - 1) + Range(-0.55f, 0.55f);
                    }
                    float height = Range(5.9f, 9.4f) + (group == 2 ? 1.2f : 0f);
                    AddTree(wood, leaves, OnGround(x, z), height, group == 2 ? 0.4f : group == 1 ? 0.18f : 0f);
                    if (group == 0 || group >= 3)
                    {
                        GameObject collider = new GameObject($"Tree trunk collision {group}-{tree}");
                        collider.transform.SetParent(forest, false);
                        collider.transform.localPosition = OnGround(x, z) + Vector3.up * height * 0.36f;
                        CapsuleCollider capsule = collider.AddComponent<CapsuleCollider>();
                        capsule.radius = height * 0.04f;
                        capsule.height = height * 0.75f;
                    }
                }
                RenderMesh($"Trunks and branches {group}", wood, barkMaterial, forest);
                RenderMesh($"Canopy leaf clusters {group}", leaves, canopyMaterial, forest);
            }
            MeadowGeometry understory = new MeadowGeometry();
            for (int patch = 0; patch < 90; patch++)
            {
                float x = Range(-20f, 20f), z = Range(11.5f, 19.8f);
                Vector3 origin = OnGround(x, z);
                AddFern(understory, origin, Range(0.6f, 1.05f));
                if (Chance(0.4f)) AddShrub(understory, origin, Range(0.8f, 1.9f));
            }
            RenderMesh("Forest understory ferns and shrubs", understory, foliageMaterial, forest);
        }

        private void AddTree(MeadowGeometry wood, MeadowGeometry leaves, Vector3 origin, float height, float depthTint)
        {
            float radius = height * Range(0.029f, 0.048f);
            Vector3 lean = new Vector3(Range(-0.75f, 0.75f), 0f, Range(-0.4f, 0.4f));
            Color bark = Color.Lerp(new Color(0.42f, 0.44f, 0.37f), new Color(0.65f, 0.61f, 0.5f), Range(0f, 1f));
            Vector3 previous = origin;
            for (int segment = 1; segment <= 8; segment++)
            {
                float t = segment / 8f;
                Vector3 current = origin + Vector3.up * height * t + lean * t * t;
                float bottom = radius * Mathf.Lerp(1.2f, 0.09f, (segment - 1f) / 8f);
                float top = radius * Mathf.Lerp(1.2f, 0.09f, t);
                wood.Tube(previous, current, bottom, top, 7, bark, false);
                previous = current;
            }
            for (int root = 0; root < 5; root++)
            {
                float angle = root * Mathf.PI * 0.4f + Range(0f, 0.4f);
                Vector3 end = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius * 3.6f;
                end.y = GroundHeight(end.x, end.z);
                wood.Tube(origin + Vector3.up * radius * 0.5f, end, radius * 0.42f, 0.025f, 5, bark, false);
            }
            int branches = random.Next(11, 16);
            for (int branch = 0; branch < branches; branch++)
            {
                float t = Range(0.4f, 0.88f);
                float angle = branch * 2.399963f + Range(-0.28f, 0.28f);
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 start = origin + Vector3.up * height * t + lean * t * t;
                float reach = height * Range(0.18f, 0.31f) * (1.1f - t * 0.32f);
                Vector3 elbow = start + radial * reach * 0.53f + Vector3.up * height * 0.055f;
                Vector3 end = start + radial * reach + Vector3.up * height * Range(0.07f, 0.15f);
                wood.Tube(start, elbow, radius * (1f - t) * 0.62f, radius * 0.12f, 5, bark, false);
                wood.Tube(elbow, end, radius * 0.12f, radius * 0.028f, 5, bark, false);
                AddLeafCluster(leaves, end, Range(0.64f, 1.02f), 22, depthTint);
                if (branch % 2 == 0)
                {
                    Vector3 twig = elbow + Quaternion.Euler(0f, 39f, 0f) * radial * reach * 0.45f + Vector3.up * 0.55f;
                    wood.Tube(elbow, twig, radius * 0.075f, 0.014f, 4, bark, false);
                    AddLeafCluster(leaves, twig, 0.65f, 16, depthTint);
                }
            }
            AddLeafCluster(leaves, previous, 0.76f, 28, depthTint);
        }

        private void AddLeafCluster(MeadowGeometry mesh, Vector3 center, float radius, int count, float depthTint)
        {
            for (int leaf = 0; leaf < count; leaf++)
            {
                Vector3 origin = center + new Vector3(Range(-radius, radius), Range(-radius * 0.3f, radius * 0.38f), Range(-radius, radius));
                float angle = Range(0f, Mathf.PI * 2f);
                Vector3 direction = new Vector3(Mathf.Sin(angle), Range(-0.4f, 0.7f), Mathf.Cos(angle)).normalized;
                Color color = Color.Lerp(new Color(0.085f, 0.145f, 0.105f), new Color(0.18f, 0.25f, 0.15f), Range(0f, 1f));
                color = Color.Lerp(color, new Color(0.13f, 0.21f, 0.19f), depthTint);
                mesh.PointedLeaf(origin, direction * Range(0.4f, 0.75f), Vector3.Cross(direction, Vector3.up), Range(0.09f, 0.17f), color, false);
            }
        }

        private void AddShrub(MeadowGeometry mesh, Vector3 origin, float height)
        {
            for (int stem = 0; stem < 5; stem++)
            {
                float angle = stem * 1.2566f;
                Vector3 direction = new Vector3(Mathf.Sin(angle) * 0.28f, 1f, Mathf.Cos(angle) * 0.28f);
                for (int pair = 1; pair <= 5; pair++)
                {
                    Vector3 center = origin + direction * height * pair / 6f;
                    Vector3 outward = Quaternion.Euler(0f, pair * 97f, 0f) * Vector3.forward;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 leafDirection = (outward * sign + Vector3.up * 0.3f).normalized;
                        mesh.PointedLeaf(center, leafDirection * height * 0.28f, Vector3.Cross(leafDirection, Vector3.up), height * 0.07f, new Color(0.115f, 0.21f, 0.13f), false);
                    }
                }
            }
        }

        private void CreateOuterForest()
        {
            Transform backdrop = new GameObject("Distant forest around the clearing").transform;
            backdrop.SetParent(generatedRoot, false);
            for (int edge = 0; edge < 4; edge++)
            {
                MeadowGeometry wood = new MeadowGeometry();
                MeadowGeometry leaves = new MeadowGeometry();
                int count = edge == 0 ? 14 : 12;
                for (int tree = 0; tree < count; tree++)
                {
                    float along = -43f + tree * 86f / (count - 1) + Range(-1.8f, 1.8f);
                    float outside = Range(39f, 46f);
                    float x = edge < 2 ? along : (edge == 2 ? -outside : outside);
                    float z = edge < 2 ? (edge == 0 ? -outside : outside) : along;
                    AddOuterTree(wood, leaves, OnGround(x, z), Range(8.5f, 13.3f));
                }
                MeshRenderer trunk = RenderMesh($"Outer forest trunks {edge}", wood, barkMaterial, backdrop).GetComponent<MeshRenderer>();
                MeshRenderer canopy = RenderMesh($"Outer forest silhouettes {edge}", leaves, canopyMaterial, backdrop).GetComponent<MeshRenderer>();
                // Beyond the playable meadow: outlines need little geometric detail
                // and do not contribute useful real-time shadows or physics.
                trunk.shadowCastingMode = ShadowCastingMode.Off;
                canopy.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private void AddOuterTree(MeadowGeometry wood, MeadowGeometry leaves, Vector3 origin, float height)
        {
            float radius = height * Range(0.026f, 0.043f);
            Vector3 lean = new Vector3(Range(-0.9f, 0.9f), 0f, Range(-0.5f, 0.5f));
            Vector3 previous = origin;
            for (int segment = 1; segment <= 6; segment++)
            {
                float t = segment / 6f;
                Vector3 next = origin + Vector3.up * height * t + lean * t * t;
                wood.Tube(previous, next,
                    radius * Mathf.Lerp(1f, 0.07f, (segment - 1f) / 6f),
                    radius * Mathf.Lerp(1f, 0.07f, t), 5, Color.white, false);
                previous = next;
            }
            int branches = random.Next(5, 8);
            for (int branch = 0; branch < branches; branch++)
            {
                float t = Range(0.46f, 0.86f);
                float angle = branch * 2.399963f + Range(-0.2f, 0.2f);
                Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 start = origin + Vector3.up * height * t + lean * t * t;
                Vector3 elbow = start + outward * height * 0.12f + Vector3.up * height * 0.05f;
                Vector3 tip = start + outward * height * Range(0.19f, 0.28f) + Vector3.up * height * 0.11f;
                wood.Tube(start, elbow, radius * 0.21f, radius * 0.09f, 4, Color.white, false);
                wood.Tube(elbow, tip, radius * 0.09f, 0.018f, 4, Color.white, false);
                AddOuterLeafCluster(leaves, tip, 10);
            }
            AddOuterLeafCluster(leaves, previous, 12);
        }

        private void AddOuterLeafCluster(MeadowGeometry mesh, Vector3 center, int count)
        {
            for (int leaf = 0; leaf < count; leaf++)
            {
                Vector3 origin = center + new Vector3(Range(-1.35f, 1.35f), Range(-0.35f, 0.5f), Range(-1.35f, 1.35f));
                float angle = Range(0f, Mathf.PI * 2f);
                Vector3 direction = new Vector3(Mathf.Sin(angle), Range(-0.2f, 0.4f), Mathf.Cos(angle)).normalized;
                Color color = Color.Lerp(new Color(0.07f, 0.13f, 0.11f), new Color(0.13f, 0.21f, 0.16f), Range(0f, 1f));
                mesh.PointedLeaf(origin, direction * Range(0.85f, 1.45f), Vector3.Cross(direction, Vector3.up), Range(0.25f, 0.43f), color, false);
            }
        }

        private void CreateLandmarks()
        {
            Transform landmarks = new GameObject("Quiet meadow landmarks").transform;
            landmarks.SetParent(generatedRoot, false);
            AddStone(landmarks, -7.1f, -0.8f, new Vector3(1.3f, 0.62f, 0.95f));
            AddStone(landmarks, -6.4f, -1.4f, new Vector3(0.64f, 0.3f, 0.52f));
            AddStone(landmarks, 9.7f, 6.8f, new Vector3(1.7f, 0.85f, 1.1f));
            AddStone(landmarks, 10.8f, 7.2f, new Vector3(0.9f, 0.43f, 0.7f));
            AddStone(landmarks, -12.8f, 9.1f, new Vector3(0.85f, 0.48f, 0.72f));
            Vector3 start = OnGround(5.9f, 10.2f) + Vector3.up * 0.24f;
            Vector3 end = OnGround(10.5f, 8.8f) + Vector3.up * 0.28f;
            MeadowGeometry wood = new MeadowGeometry();
            wood.Tube(start, end, 0.34f, 0.28f, 11, Color.white, true);
            wood.Tube(Vector3.Lerp(start, end, 0.67f), Vector3.Lerp(start, end, 0.67f) + new Vector3(0.12f, 0.65f, 0.2f), 0.09f, 0.034f, 6, Color.white, true);
            GameObject log = RenderMesh("Fallen branch", wood, barkMaterial, landmarks);
            GameObject collision = new GameObject("Fallen branch collision");
            collision.transform.SetParent(log.transform, false);
            collision.transform.localPosition = (start + end) * 0.5f;
            collision.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
            CapsuleCollider capsule = collision.AddComponent<CapsuleCollider>();
            capsule.radius = 0.34f;
            capsule.height = (end - start).magnitude + 0.34f;
            MeadowGeometry moss = new MeadowGeometry();
            for (int tuft = 0; tuft < 42; tuft++)
            {
                Vector3 origin = Vector3.Lerp(start, end, Range(0.04f, 0.96f)) + new Vector3(Range(-0.12f, 0.12f), 0.29f, Range(-0.13f, 0.13f));
                AddBroadPlant(moss, origin, Range(0.07f, 0.13f));
            }
            RenderMesh("Moss on fallen wood", moss, foliageMaterial, landmarks);
        }

        private void AddStone(Transform parent, float x, float z, Vector3 scale)
        {
            MeadowGeometry geometry = new MeadowGeometry();
            Vector3 origin = OnGround(x, z) - Vector3.up * 0.1f;
            const int sides = 9;
            for (int ring = 0; ring < 3; ring++)
            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                float radial = (ring == 1 ? 1f : 0.57f) * Range(0.84f, 1.15f);
                float y = ring == 0 ? 0f : ring == 1 ? 0.38f : 0.82f;
                Vector3 offset = new Vector3(Mathf.Sin(angle) * radial, y + Range(-0.05f, 0.05f), Mathf.Cos(angle) * radial);
                geometry.Vertex(origin + Vector3.Scale(offset, scale), offset.normalized, new Vector2(side / 9f, ring / 2f), Color.white);
            }
            int top = geometry.Vertex(origin + Vector3.up * scale.y, Vector3.up, new Vector2(0.5f, 0.5f), Color.white);
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                for (int ring = 0; ring < 2; ring++)
                {
                    int a = ring * sides + side, b = ring * sides + next;
                    geometry.Triangle(a, b, a + sides);
                    geometry.Triangle(b, b + sides, a + sides);
                }
                geometry.Triangle(18 + side, 18 + next, top);
            }
            GameObject stone = RenderMesh("Weathered stone", geometry, stoneMaterial, parent);
            stone.AddComponent<MeshCollider>().sharedMesh = stone.GetComponent<MeshFilter>().sharedMesh;
            MeadowGeometry plants = new MeadowGeometry();
            for (int i = 0; i < 8; i++) AddBroadPlant(plants, origin + new Vector3(Range(-scale.x * 0.4f, scale.x * 0.4f), scale.y * 0.89f, Range(-scale.z * 0.4f, scale.z * 0.4f)), Range(0.055f, 0.13f));
            RenderMesh("Sparse rock moss", plants, foliageMaterial, parent);
        }

        private void CreateMist()
        {
            Transform mist = new GameObject("Drifting low mist").transform;
            mist.SetParent(generatedRoot, false);
            for (int i = 0; i < 7; i++)
            {
                Vector3 center = new Vector3(Range(-15f, 15f), Range(0.42f, 0.78f), Range(-8f, 15f));
                Vector3 right = Quaternion.Euler(0f, Range(-35f, 35f), 0f) * Vector3.right * Range(7f, 11f);
                Vector3 across = Vector3.Cross(right.normalized, Vector3.up) * Range(2f, 3.9f) + Vector3.up * Range(0.12f, 0.3f);
                MeadowGeometry geometry = new MeadowGeometry();
                geometry.Vertex(center - right - across, Vector3.up, new Vector2(0f, 0f), Color.white);
                geometry.Vertex(center + right - across, Vector3.up, new Vector2(1f, 0f), Color.white);
                geometry.Vertex(center - right + across, Vector3.up, new Vector2(0f, 1f), Color.white);
                geometry.Vertex(center + right + across, Vector3.up, new Vector2(1f, 1f), Color.white);
                geometry.Triangle(0, 2, 1);
                geometry.Triangle(1, 2, 3);
                MeshRenderer renderer = RenderMesh($"Low mist wisp {i}", geometry, mistMaterial, mist).GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private GameObject RenderMesh(string name, MeadowGeometry geometry, Material material, Transform parent = null)
        {
            GameObject value = new GameObject(name);
            value.transform.SetParent(parent != null ? parent : generatedRoot, false);
            Mesh mesh = Own(geometry.ToMesh(name));
            value.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = value.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return value;
        }
    }

    /// <summary>Combined procedural leaf and branch mesh builder. Color alpha is the wind mask.</summary>
    internal sealed class MeadowGeometry
    {
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        public int Vertex(Vector3 position, Vector3 normal, Vector2 uv, Color color)
        {
            int index = positions.Count;
            positions.Add(position);
            normals.Add(normal);
            uvs.Add(uv);
            colors.Add(color);
            return index;
        }

        public void Triangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        public void PointedLeaf(Vector3 origin, Vector3 stem, Vector3 widthDirection, float halfWidth, Color tipColor, bool grass)
        {
            Vector3 width = widthDirection.normalized;
            if (width.sqrMagnitude < 0.1f) width = Vector3.right;
            Vector3 normal = Vector3.Cross(width, stem).normalized;
            if (!grass) normal = (normal + Vector3.up * 0.46f).normalized;
            Color baseColor = Color.Lerp(tipColor, new Color(0.055f, 0.115f, 0.065f), grass ? 0.7f : 0.23f);
            int start = positions.Count;
            for (int ring = 0; ring < 3; ring++)
            {
                float t = ring == 0 ? 0f : ring == 1 ? 0.4f : 0.76f;
                float widthScale = grass ? (1f - t * 0.72f) : (ring == 0 ? 0.15f : ring == 1 ? 1f : 0.64f);
                Vector3 bend = grass ? new Vector3(stem.x, 0f, stem.z) * (t * t - t) * 0.62f : normal * stem.magnitude * Mathf.Sin(t * Mathf.PI) * 0.06f;
                Vector3 center = origin + stem * t + bend;
                Color color = Color.Lerp(baseColor, tipColor, t);
                color.a = grass ? t * t : 0.45f + t * 0.4f;
                Vertex(center - width * halfWidth * widthScale, normal, new Vector2(0f, t), color);
                Vertex(center + width * halfWidth * widthScale, normal, new Vector2(1f, t), color);
            }
            tipColor.a = 1f;
            Vertex(origin + stem, normal, new Vector2(0.5f, 1f), tipColor);
            for (int ring = 0; ring < 2; ring++)
            {
                int a = start + ring * 2;
                Triangle(a, a + 1, a + 2);
                Triangle(a + 1, a + 3, a + 2);
            }
            Triangle(start + 4, start + 5, start + 6);
        }

        public void RoundedLeaf(Vector3 origin, Vector3 stem, Vector3 widthDirection, float halfWidth, Color color)
        {
            Vector3 width = widthDirection.normalized;
            Vector3 normal = Vector3.Cross(width, stem).normalized;
            Vector3 center = origin + stem * 0.51f + normal * stem.magnitude * 0.08f;
            color.a = 0.16f;
            int first = Vertex(center, normal, new Vector2(0.5f, 0.5f), color);
            // Nine vertices per rounded leaflet, with a raised central vein.
            // Winding follows the declared normal on both tilted and horizontal leaves.
            LeafOutline(origin, stem, width, normal, 0f, 0f, halfWidth, color);
            LeafOutline(origin, stem, width, normal, 0.23f, 0.86f, halfWidth, color);
            LeafOutline(origin, stem, width, normal, 0.62f, 1f, halfWidth, color);
            LeafOutline(origin, stem, width, normal, 0.9f, 0.57f, halfWidth, color);
            LeafOutline(origin, stem, width, normal, 0.99f, 0f, halfWidth, color);
            LeafOutline(origin, stem, width, normal, 0.9f, -0.57f, halfWidth, color);
            LeafOutline(origin, stem, width, normal, 0.62f, -1f, halfWidth, color);
            LeafOutline(origin, stem, width, normal, 0.23f, -0.86f, halfWidth, color);
            for (int edge = 0; edge < 8; edge++) Triangle(first, first + edge + 1, first + (edge + 1) % 8 + 1);
        }

        private void LeafOutline(Vector3 origin, Vector3 stem, Vector3 width, Vector3 normal, float along, float across, float halfWidth, Color color)
        {
            color.a = 0.1f + along * 0.15f;
            color *= Mathf.Lerp(0.8f, 1f, along);
            Vertex(origin + stem * along + width * across * halfWidth, normal, new Vector2(across * 0.5f + 0.5f, along), color);
        }

        public void Tube(Vector3 start, Vector3 end, float startRadius, float endRadius, int sides, Color color, bool caps)
        {
            Vector3 axis = (end - start).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 across = Vector3.Cross(axis, side).normalized;
            int first = positions.Count;
            color.a = 0f;
            for (int ring = 0; ring < 2; ring++)
            for (int point = 0; point <= sides; point++)
            {
                float angle = point * Mathf.PI * 2f / sides;
                Vector3 normal = side * Mathf.Cos(angle) + across * Mathf.Sin(angle);
                Vertex((ring == 0 ? start : end) + normal * (ring == 0 ? startRadius : endRadius), normal, new Vector2(point / (float)sides, ring == 0 ? 0f : (end - start).magnitude * 0.45f), color);
            }
            for (int point = 0; point < sides; point++)
            {
                int a = first + point, b = a + sides + 1;
                Triangle(a, a + 1, b);
                Triangle(a + 1, b + 1, b);
            }
            if (!caps) return;
            int lower = Vertex(start, -axis, Vector2.one * 0.5f, color);
            int upper = Vertex(end, axis, Vector2.one * 0.5f, color);
            for (int point = 0; point < sides; point++)
            {
                Triangle(lower, first + point + 1, first + point);
                Triangle(upper, first + sides + 1 + point, first + sides + 2 + point);
            }
        }

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh { name = name, indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
