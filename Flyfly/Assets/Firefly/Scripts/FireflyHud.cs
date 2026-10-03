using UnityEngine;
using UnityEngine.UI;

namespace Firefly
{
    /// <summary>Small, device-aware hints. Start/Menu resumes without requiring UI navigation.</summary>
    public sealed class FireflyHud : MonoBehaviour
    {
        [SerializeField] private FlightInput input;
        [SerializeField] private FireflyGlow glow;
        [SerializeField] private FireflySwarm swarm;
        private Text title, subtitle, hints, lantern, answer, pauseTitle, pauseHint;
        private CanvasGroup introGroup, hintGroup, pauseGroup;
        private Font font;
        private bool chinese;
        private float started, hintUntil, answerUntil;
        private bool lastDevice;
        private GameObject canvasRoot;
        public void Configure(FlightInput controls,FireflyGlow light,FireflySwarm fireflies)
        {
            input=controls; glow=light; swarm=fireflies;
        }
        private void Awake()
        {
            string[] available=Font.GetOSInstalledFontNames();
            chinese=System.Array.Exists(available,name=>name=="Microsoft YaHei" || name=="Microsoft YaHei UI" || name=="Noto Sans CJK SC");
            font=chinese ? Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft YaHei UI","Noto Sans CJK SC"},24)
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasRoot=new GameObject("Quiet interface",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            canvasRoot.transform.SetParent(transform,false);
            var canvas=canvasRoot.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=20;
            var scaler=canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=0.5f;
            var intro=Panel("Introduction",canvasRoot.transform); introGroup=intro.AddComponent<CanvasGroup>();
            title=Label(intro.transform,chinese?"夜 萤":"F I R E F L Y",40,new Vector2(70,-65),new Vector2(540,60),TextAnchor.UpperLeft);
            subtitle=Label(intro.transform,chinese?"在森林醒来之前，留下你的一点光。":"A little light, before the forest wakes.",18,new Vector2(73,-130),new Vector2(750,45),TextAnchor.UpperLeft);
            subtitle.color=new Color(0.65f,0.79f,0.74f,0.75f);
            var hintPanel=Panel("Controls",canvasRoot.transform); hintGroup=hintPanel.AddComponent<CanvasGroup>();
            hints=Label(hintPanel.transform,"",18,new Vector2(0,40),new Vector2(1800,70),TextAnchor.MiddleCenter,true);
            lantern=Label(canvasRoot.transform,"",17,new Vector2(-65,-62),new Vector2(420,42),TextAnchor.UpperRight,false,true);
            answer=Label(canvasRoot.transform,"",21,new Vector2(0,115),new Vector2(850,45),TextAnchor.MiddleCenter,true);
            var pause=Panel("Pause",canvasRoot.transform); pauseGroup=pause.AddComponent<CanvasGroup>();
            var shade=pause.AddComponent<Image>(); shade.color=new Color(0.008f,0.018f,0.023f,0.83f); shade.raycastTarget=false;
            pauseTitle=Label(pause.transform,chinese?"静 候":"P A U S E D",40,new Vector2(0,35),new Vector2(700,80),TextAnchor.MiddleCenter);
            Center(pauseTitle.rectTransform);
            pauseHint=Label(pause.transform,"",22,new Vector2(0,-60),new Vector2(1100,70),TextAnchor.MiddleCenter); Center(pauseHint.rectTransform);
            pauseGroup.alpha=0;
            started=Time.unscaledTime; hintUntil=started+14;
            lastDevice=input && input.LastInputWasGamepad;
            if (swarm) swarm.ClearingAnswered+=OnAnswer;
        }
        private static GameObject Panel(string name,Transform parent)
        {
            var go=new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform; rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero; return go;
        }
        private Text Label(Transform parent,string text,int size,Vector2 offset,Vector2 dimensions,TextAnchor alignment,bool bottom=false,bool right=false)
        {
            var go=new GameObject("Text",typeof(RectTransform),typeof(Text)); go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;
            Vector2 anchor=bottom?new Vector2(0.5f,0):right?Vector2.one:new Vector2(0,1);
            rect.anchorMin=rect.anchorMax=rect.pivot=anchor; rect.anchoredPosition=offset; rect.sizeDelta=dimensions;
            var label=go.GetComponent<Text>(); label.font=font; label.fontSize=size; label.text=text;
            label.alignment=alignment; label.color=new Color(0.85f,0.91f,0.78f,0.9f); label.raycastTarget=false;
            label.horizontalOverflow=HorizontalWrapMode.Overflow; return label;
        }
        private static void Center(RectTransform rect) { rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0.5f,0.5f); }
        private void OnAnswer(int clearing) { answerUntil=Time.unscaledTime+3.4f; }
        private void Update()
        {
            if (!input || !glow) return;
            bool gamepad=input.LastInputWasGamepad;
            if (lastDevice!=gamepad) { lastDevice=gamepad; hintUntil=Time.unscaledTime+7; }
            introGroup.alpha=1-Mathf.SmoothStep(0,1,(Time.unscaledTime-started-6)/5);
            hintGroup.alpha=Mathf.Lerp(hintGroup.alpha,Time.unscaledTime<hintUntil?0.85f:0.15f,Time.unscaledDeltaTime*3);
            hints.text=chinese ? (gamepad?"左摇杆  飞行     RT / LT  升降     右摇杆  视角     A / ×  荧光     RB / R1  加速     Menu  暂停":"WASD  飞行     空格 / Ctrl  升降     鼠标右键拖动  视角     F  荧光     Shift  加速     Esc  暂停")
                : (gamepad?"LEFT STICK  Fly     RT / LT  Rise / Descend     RIGHT STICK  Look     A / CROSS  Glow     RB / R1  Faster     MENU  Pause":"WASD  Fly     SPACE / CTRL  Rise / Descend     RMB + MOUSE  Look     F  Glow     SHIFT  Faster     ESC  Pause");
            lantern.text=chinese ? (glow.IsLit?"●  荧光已点亮":"○  荧光已关闭") : (glow.IsLit?"GLOW  ON":"GLOW  OFF");
            lantern.color=Color.Lerp(new Color(0.53f,0.64f,0.64f,0.7f),new Color(0.82f,0.97f,0.46f,0.9f),glow.Brightness);
            answer.text=chinese?"林间的微光，回应了你。":"The meadow answers your light.";
            answer.color=new Color(0.81f,0.94f,0.55f,Mathf.Clamp01((answerUntil-Time.unscaledTime)*0.9f));
            pauseGroup.alpha=input.IsPaused?1:0;
            pauseHint.text=chinese ? (gamepad?"按 Menu / Options 继续飞行":"按 Esc 继续飞行") : (gamepad?"Press MENU / OPTIONS to return":"Press ESC to return");
        }
        private void OnDestroy() { if (swarm) swarm.ClearingAnswered-=OnAnswer; if (chinese && font) Destroy(font); }
    }
}
