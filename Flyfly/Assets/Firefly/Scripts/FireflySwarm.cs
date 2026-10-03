using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Firefly
{
    /// <summary>One billboard mesh, with a bounded pool of real lights on nearby insects.</summary>
    public sealed class FireflySwarm : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private FireflyGlow playerGlow;
        [SerializeField, Range(32, 256)] private int count = 112;
        [SerializeField] private int seed = 714;
        [SerializeField, Range(8, 32)] private int lightBudget = 24;
        [SerializeField] private float responseDuration = 10f;
        [SerializeField] private float responseLightIntensity = 1.8f;
        [SerializeField] private Light[] localLights;
        [SerializeField] private Vector3[] clearings =
        {
            new Vector3(-8f, 1.1f, -1f), new Vector3(6f, 1.4f, 8f), new Vector3(10f, 1.1f, -5f)
        };
        private struct Mote { public Vector3 home; public float phase, speed, radius, size; public int group; }
        private Mote[] motes;
        private Vector3[] vertices;
        private Color[] colors;
        private Vector3[] positions;
        private float[] illumination;
        private float[] responseEnvelopes;
        private int[] selectedMotes;
        private float[] selectionScores;
        private Mesh mesh;
        private Camera view;
        private float clock;
        private float[] responseUntil;
        private float[] cooldownUntil;
        private bool[] discovered;
        private bool ownsMesh;
        public event Action<int> ClearingAnswered;
        public int DiscoveredCount { get; private set; }
        public bool IsResponding { get; private set; }
        public int ActivePointLightCount { get; private set; }
        public int RespondingPointLightCount { get; private set; }
        public float ResponseLightEnergy { get; private set; }
        public float AmbientLightEnergy { get; private set; }

        public void Configure(Transform target, FireflyGlow glow)
        {
            player = target; playerGlow = glow;
        }

        public void Build()
        {
            var filter = GetComponent<MeshFilter>();
            if (!filter) filter=gameObject.AddComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (!renderer) renderer=gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Firefly/Glow")) { name = "Meadow firefly glow" };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Initialize();
            EnsureLightPool();
            mesh = new Mesh { name = "Meadow firefly billboards" };
            filter.sharedMesh = mesh;
            SetupMesh();
            UpdateMesh(Vector3.right, Vector3.up);
        }

        private void Awake()
        {
            Initialize();
            EnsureLightPool();
            var filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) Build();
            else
            {
                mesh = Instantiate(filter.sharedMesh);
                mesh.name = "Live meadow fireflies";
                filter.sharedMesh = mesh;
                SetupMesh();
            }
            ownsMesh = true;
            mesh.MarkDynamic();
            view = Camera.main;
        }

        private void OnEnable()
        {
            // Unity can reload scripts during Play Mode without invoking Awake again.
            // Recreate the non-serialized simulation buffers before the next update.
            if (!Application.isPlaying) return;
            if (motes==null) Initialize();
            EnsureLightPool();
            if (!mesh)
            {
                var filter=GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh) { mesh=filter.sharedMesh; ownsMesh=true; mesh.MarkDynamic(); }
            }
            if (!view) view=Camera.main;
        }

        private void Initialize()
        {
            var random = new System.Random(seed);
            clock=0; DiscoveredCount=0; IsResponding=false;
            motes = new Mote[count]; vertices = new Vector3[count * 4]; colors = new Color[count * 4];
            positions = new Vector3[count]; illumination = new float[count]; responseEnvelopes = new float[count];
            selectedMotes = new int[lightBudget]; selectionScores = new float[lightBudget];
            responseUntil = new float[clearings.Length]; cooldownUntil = new float[clearings.Length];
            discovered = new bool[clearings.Length];
            for (int i = 0; i < count; i++)
            {
                int group = i < clearings.Length * 13 ? i / 13 : -1;
                Vector3 home = group >= 0 ? clearings[group] + new Vector3(R(random,-2,2), R(random,-0.4f,1), R(random,-2,2))
                    : new Vector3(R(random,-18,18), R(random,0.45f,3), R(random,-12,17));
                motes[i] = new Mote { home=home, group=group, phase=R(random,0,Mathf.PI*2),
                    speed=R(random,0.22f,0.55f), radius=R(random,0.2f,0.7f), size=R(random,0.035f,0.075f) };
            }
        }

        private static float R(System.Random random,float min,float max) => Mathf.Lerp(min,max,(float)random.NextDouble());

        private void SetupMesh()
        {
            var uv = new Vector2[count * 4]; var indices = new int[count * 6];
            for (int i=0;i<count;i++)
            {
                int v=i*4, t=i*6;
                uv[v]=Vector2.zero; uv[v+1]=Vector2.right; uv[v+2]=Vector2.one; uv[v+3]=Vector2.up;
                indices[t]=v; indices[t+1]=v+2; indices[t+2]=v+1;
                indices[t+3]=v; indices[t+4]=v+3; indices[t+5]=v+2;
            }
            mesh.vertices=vertices; mesh.uv=uv; mesh.colors=colors; mesh.triangles=indices;
            mesh.bounds=new Bounds(new Vector3(0,5,3),new Vector3(46,20,42));
        }

        private void EnsureLightPool()
        {
            if (localLights != null && localLights.Length == lightBudget && Array.TrueForAll(localLights, light => light)) return;
            // Recover serialized scene lights after a domain reload before creating any more.
            var existing = GetComponentsInChildren<Light>(true);
            localLights = new Light[lightBudget];
            for (int i = 0; i < lightBudget; i++)
            {
                Light light = i < existing.Length ? existing[i] : null;
                if (!light)
                {
                    var go = new GameObject("Local firefly light " + (i + 1).ToString("D2"));
                    go.transform.SetParent(transform, false);
                    light = go.AddComponent<Light>();
                }
                light.type = LightType.Point;
                light.color = new Color(0.73f, 1f, 0.22f);
                light.range = 1.55f;
                light.intensity = 0;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.enabled = false;
                localLights[i] = light;
            }
            for (int i = lightBudget; i < existing.Length; i++) existing[i].enabled = false;
        }

        private void Update()
        {
            if (Time.deltaTime <= 0) return;
            clock += Time.deltaTime;
            IsResponding = false;
            for (int g=0;g<clearings.Length;g++)
            {
                Vector3 delta=player ? player.position-clearings[g] : Vector3.one*100;
                if (playerGlow && playerGlow.IsLit && playerGlow.Brightness > 0.65f && delta.sqrMagnitude < 14f && clock >= cooldownUntil[g])
                {
                    responseUntil[g]=clock+responseDuration; cooldownUntil[g]=clock+responseDuration+3f;
                    if (!discovered[g]) { discovered[g]=true; DiscoveredCount++; }
                    ClearingAnswered?.Invoke(g);
                }
                IsResponding |= clock < responseUntil[g];
            }
            if (!view) view=Camera.main;
            if (view) UpdateMesh(view.transform.right,view.transform.up);
            else UpdateMesh(Vector3.right,Vector3.up);
            UpdateLocalLights();
        }

        private void UpdateMesh(Vector3 right,Vector3 up)
        {
            for (int i=0;i<count;i++)
            {
                Mote m=motes[i]; float t=clock*m.speed+m.phase;
                Vector3 p=m.home+new Vector3(Mathf.Sin(t)*m.radius,Mathf.Sin(t*1.7f)*0.17f,Mathf.Cos(t*0.8f)*m.radius);
                bool answering=m.group >= 0 && clock < responseUntil[m.group];
                float pulse=Mathf.Pow(Mathf.Max(0,Mathf.Sin(clock*1.25f+m.phase)),5);
                float brightness=0.07f+pulse*2.4f;
                float envelope=0;
                if (answering)
                {
                    float remaining=responseUntil[m.group]-clock;
                    float rise=Mathf.SmoothStep(0,1,(responseDuration-remaining)/1.2f);
                    float fade=Mathf.SmoothStep(0,1,remaining/2.5f);
                    // A broad shared pulse keeps the whole group bright together, so their
                    // physical lights add up instead of illuminating unrelated single dots.
                    envelope=rise*fade*(0.85f+0.15f*Mathf.Sin(clock*2.1f));
                    if (player) p=Vector3.Lerp(p,player.position+new Vector3(Mathf.Sin(t*2)*1.4f,0.1f+Mathf.Cos(t)*0.35f,Mathf.Cos(t*2)*1.4f),envelope*0.65f);
                    brightness=Mathf.Max(brightness,envelope*4f);
                }
                positions[i]=p;
                responseEnvelopes[i]=envelope;
                illumination[i]=0.02f+pulse*0.28f+envelope*responseLightIntensity;
                Vector3 r=right*m.size, u=up*m.size; int v=i*4;
                vertices[v]=p-r-u; vertices[v+1]=p+r-u; vertices[v+2]=p+r+u; vertices[v+3]=p-r+u;
                Color c=new Color(0.8f*brightness,brightness,0.16f*brightness,1);
                colors[v]=colors[v+1]=colors[v+2]=colors[v+3]=c;
            }
            mesh.vertices=vertices; mesh.colors=colors;
        }

        private void UpdateLocalLights()
        {
            Vector3 focus=player ? player.position : Vector3.zero;
            for (int slot=0;slot<lightBudget;slot++) { selectedMotes[slot]=-1; selectionScores[slot]=float.NegativeInfinity; }
            for (int i=0;i<count;i++)
            {
                float distanceSquared=(positions[i]-focus).sqrMagnitude;
                if (distanceSquared>16f*16f) continue;
                float score=(responseEnvelopes[i]>0.01f ? 1000f : 0f)-distanceSquared;
                for (int slot=0;slot<lightBudget;slot++)
                {
                    if (score<=selectionScores[slot]) continue;
                    for (int next=lightBudget-1;next>slot;next--)
                    { selectedMotes[next]=selectedMotes[next-1]; selectionScores[next]=selectionScores[next-1]; }
                    selectedMotes[slot]=i; selectionScores[slot]=score; break;
                }
            }
            ActivePointLightCount=0; RespondingPointLightCount=0; ResponseLightEnergy=0; AmbientLightEnergy=0;
            for (int slot=0;slot<lightBudget;slot++)
            {
                Light light=localLights[slot]; int i=selectedMotes[slot];
                if (i<0) { light.enabled=false; continue; }
                light.transform.position=transform.TransformPoint(positions[i]);
                light.intensity=illumination[i];
                light.range=Mathf.Lerp(1.55f,3.5f,responseEnvelopes[i]);
                light.enabled=light.intensity>0.001f;
                if (!light.enabled) continue;
                ActivePointLightCount++;
                if (responseEnvelopes[i]>0.1f) { RespondingPointLightCount++; ResponseLightEnergy+=light.intensity; }
                else AmbientLightEnergy+=light.intensity;
            }
        }

        private void OnDisable()
        {
            if (localLights!=null) foreach (Light light in localLights) if (light) light.enabled=false;
            ActivePointLightCount=0; RespondingPointLightCount=0; ResponseLightEnergy=0; AmbientLightEnergy=0;
        }

        private void OnDestroy() { if (ownsMesh && mesh) Destroy(mesh); }
    }
}
