using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Firefly.Editor
{
    public static class FireflySceneBuilder
    {
        public const string ScenePath="Assets/Firefly/Scenes/NightMeadow.unity";
        private const string ArtPath="Assets/Firefly/Generated";
        [MenuItem("Firefly/Build Night Meadow")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before rebuilding.");
            // Preserve any unrelated unsaved work rather than silently replacing it.
            for (int i=0;i<SceneManager.sceneCount;i++)
            {
                Scene open=SceneManager.GetSceneAt(i);
                bool failedGeneratedScene=string.IsNullOrEmpty(open.path) && open.GetRootGameObjects().Length==1
                    && open.GetRootGameObjects()[0].name=="Night Meadow";
                if (open.isDirty && !failedGeneratedScene) throw new InvalidOperationException("Save your open scene before building the meadow.");
            }
            Directory.CreateDirectory("Assets/Firefly/Scenes"); Directory.CreateDirectory(ArtPath);
            AssetDatabase.Refresh();
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Night Meadow");
            var environmentGo=new GameObject("Grassland and forest edge"); environmentGo.transform.SetParent(root.transform,false);
            environmentGo.AddComponent<MeadowEnvironment>().Build();
            var sky=new Material(Shader.Find("Firefly/Night Sky")) { name="Night sky" };
            sky.SetColor("_Horizon",new Color(0.0015f,0.0035f,0.005f));
            sky.SetColor("_Zenith",new Color(0.0005f,0.0012f,0.0025f));
            RenderSettings.skybox=sky;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(0.008f,0.015f,0.022f);
            RenderSettings.ambientEquatorColor=new Color(0.004f,0.009f,0.009f);
            RenderSettings.ambientGroundColor=new Color(0.002f,0.004f,0.003f);
            RenderSettings.ambientIntensity=1;
            RenderSettings.reflectionIntensity=0.025f;
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogColor=new Color(0.001f,0.003f,0.0035f); RenderSettings.fogDensity=0.028f;
            var moon=new GameObject("Moonlight"); moon.transform.SetParent(root.transform,false);
            moon.transform.rotation=Quaternion.Euler(38,-38,0);
            var moonLight=moon.AddComponent<Light>(); moonLight.type=LightType.Directional;
            moonLight.color=new Color(0.5f,0.7f,0.86f); moonLight.intensity=0.018f;
            moonLight.shadows=LightShadows.Soft; moonLight.shadowStrength=0.7f; RenderSettings.sun=moonLight;
            var cameraGo=new GameObject("Main Camera"); cameraGo.transform.SetParent(root.transform,false); cameraGo.tag="MainCamera";
            var camera=cameraGo.AddComponent<Camera>(); cameraGo.AddComponent<AudioListener>();
            camera.fieldOfView=60; camera.nearClipPlane=0.08f; camera.farClipPlane=160;
            camera.clearFlags=CameraClearFlags.Skybox; camera.allowHDR=true;
            var cameraData=cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing=true; cameraData.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
            var playerGo=new GameObject("Player Firefly"); playerGo.transform.SetParent(root.transform,false);
            playerGo.transform.position=new Vector3(0,1.3f,-5);
            var input=playerGo.AddComponent<FlightInput>();
            var model=FireflyModel.Build(playerGo.transform);
            var glow=playerGo.AddComponent<FireflyGlow>(); glow.Configure(model.Abdomen,model.GlowLight,model.Halo); glow.BindInput(input);
            var pilot=playerGo.AddComponent<FireflyPilot>(); pilot.Configure(input,model.VisualRoot,cameraGo.transform);
            var follow=cameraGo.AddComponent<MeadowCamera>(); follow.Configure(playerGo.transform);
            var firefliesGo=new GameObject("Meadow fireflies"); firefliesGo.transform.SetParent(root.transform,false);
            var swarm=firefliesGo.AddComponent<FireflySwarm>(); swarm.Configure(playerGo.transform,glow); swarm.Build();
            root.AddComponent<FireflyExperience>().Configure(input,glow,swarm);
            root.AddComponent<FireflyHud>().Configure(input,glow,swarm);
            root.AddComponent<MeadowSoundscape>().Configure(swarm,playerGo.transform);
            var volumeGo=new GameObject("Night colour and glow"); volumeGo.transform.SetParent(root.transform,false);
            var volume=volumeGo.AddComponent<Volume>(); volume.isGlobal=true; volume.priority=10;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>(); profile.name="Night meadow atmosphere";
            var bloom=profile.Add<Bloom>(true); bloom.threshold.value=0.9f; bloom.intensity.value=0.5f; bloom.scatter.value=0.56f;
            var tone=profile.Add<Tonemapping>(true); tone.mode.value=TonemappingMode.ACES;
            var colors=profile.Add<ColorAdjustments>(true); colors.postExposure.value=-0.65f; colors.saturation.value=-8;
            var vignette=profile.Add<Vignette>(true); vignette.intensity.value=0.2f; vignette.smoothness.value=0.6f;
            PersistProfile(profile); volume.sharedProfile=profile;
            PersistAssets(root,sky);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.productName="Firefly - Night Meadow";
            PlayerSettings.defaultScreenWidth=1920; PlayerSettings.defaultScreenHeight=1080;
            PlayerSettings.resizableWindow=true;
            AssetDatabase.SaveAssets();
            Selection.activeGameObject=playerGo;
            SceneView.RepaintAll();
            Debug.Log("FIREFLY: Night meadow created. Open Game view and press Play.");
        }

        private static void PersistProfile(VolumeProfile profile)
        {
            string path=ArtPath+"/NightMeadowProfile.asset";
            AssetDatabase.CreateAsset(profile,path);
            foreach (var component in profile.components) { component.hideFlags=HideFlags.HideInInspector; AssetDatabase.AddObjectToAsset(component,profile); }
        }

        private static void PersistAssets(GameObject root,Material sky)
        {
            var saved=new HashSet<UnityEngine.Object>(); int index=0;
            Action<UnityEngine.Object,string> save=(asset,extension)=>
            {
                if (!asset || !saved.Add(asset) || AssetDatabase.Contains(asset)) return;
                string safe=System.Text.RegularExpressions.Regex.Replace(asset.name,@"[^A-Za-z0-9_-]","_");
                // These paths are owned by the generator; repeat builds replace their assets
                // rather than adding another complete meadow to the repository.
                AssetDatabase.CreateAsset(asset,$"{ArtPath}/{index++:D3}_{safe}.{extension}");
            };
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) save(filter.sharedMesh,"asset");
            var materials=new List<Material>{sky};
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) materials.AddRange(renderer.sharedMaterials);
            foreach (var material in materials)
            {
                if (!material) continue;
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture texture=material.GetTexture(property);
                    if (texture && texture is Texture2D && !AssetDatabase.Contains(texture) && texture!=Texture2D.whiteTexture && texture!=Texture2D.blackTexture && texture!=Texture2D.normalTexture)
                        save(texture,"asset");
                }
                save(material,"mat");
            }
        }
    }
}
