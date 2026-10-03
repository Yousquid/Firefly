using UnityEngine;

namespace Firefly
{
    public sealed class FireflyGlow : MonoBehaviour
    {
        [SerializeField] private bool startLit = true;
        [SerializeField] private float transitionDuration = 0.3f;
        [SerializeField] private float lightIntensity = 0.3f;
        [SerializeField] private Color glowColor = new Color(0.79f, 1f, 0.24f, 1f);

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        [SerializeField] private Renderer abdomen;
        [SerializeField] private Renderer halo;
        [SerializeField] private Light glowLight;
        [SerializeField] private FlightInput input;
        private MaterialPropertyBlock abdomenProperties;
        private MaterialPropertyBlock haloProperties;
        private Color restingAbdomenColor = new Color(0.16f, 0.22f, 0.055f, 1f);

        public bool IsLit { get; private set; }
        public float Brightness { get; private set; }

        private void Awake()
        {
            Configure(abdomen, glowLight, halo);
        }

        public void Configure(Renderer abdomenRenderer, Light light, Renderer haloRenderer = null)
        {
            abdomen = abdomenRenderer;
            glowLight = light;
            if (glowLight != null) glowLight.range = 1.65f;
            halo = haloRenderer;
            abdomenProperties = new MaterialPropertyBlock();
            haloProperties = new MaterialPropertyBlock();
            if (abdomen != null && abdomen.sharedMaterial != null && abdomen.sharedMaterial.HasProperty(BaseColor))
                restingAbdomenColor = abdomen.sharedMaterial.GetColor(BaseColor);
            IsLit = startLit;
            Brightness = IsLit ? 1f : 0f;
            ApplyVisuals();
        }

        public void BindInput(FlightInput flightInput)
        {
            if (input != null)
                input.GlowPressed -= Toggle;
            input = flightInput;
            if (input != null && isActiveAndEnabled)
                input.GlowPressed += Toggle;
        }

        private void OnEnable()
        {
            if (abdomenProperties == null || haloProperties == null)
                Configure(abdomen, glowLight, halo);
            if (input != null)
                input.GlowPressed += Toggle;
        }

        private void OnDisable()
        {
            if (input != null)
                input.GlowPressed -= Toggle;
        }

        public void SetLit(bool lit, bool playHaptics = false)
        {
            if (IsLit == lit)
                return;
            IsLit = lit;
            if (playHaptics && input != null)
                input.PlayGlowHaptics(lit);
        }

        private void Toggle()
        {
            SetLit(!IsLit, true);
        }

        private void Update()
        {
            if (input != null && (input.IsPaused || !input.HasFocus))
                return;
            Brightness = Mathf.MoveTowards(Brightness, IsLit ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, transitionDuration));
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            float eased = Mathf.SmoothStep(0f, 1f, Brightness);
            if (abdomen != null)
            {
                abdomen.GetPropertyBlock(abdomenProperties);
                abdomenProperties.SetColor(EmissionColor, glowColor * (eased * 5.5f));
                abdomenProperties.SetColor(BaseColor, Color.Lerp(restingAbdomenColor, glowColor * 0.65f, eased));
                abdomen.SetPropertyBlock(abdomenProperties);
            }
            if (glowLight != null)
            {
                glowLight.color = glowColor;
                glowLight.intensity = lightIntensity * eased;
                glowLight.enabled = eased > 0.001f;
            }
            if (halo != null)
            {
                halo.GetPropertyBlock(haloProperties);
                haloProperties.SetColor(BaseColor, new Color(glowColor.r, glowColor.g, glowColor.b, eased * 0.15f));
                halo.SetPropertyBlock(haloProperties);
                halo.enabled = eased > 0.001f;
            }
        }
    }
}
