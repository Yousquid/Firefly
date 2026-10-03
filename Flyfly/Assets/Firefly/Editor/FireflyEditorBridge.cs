using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Firefly.Editor
{
    /// <summary>Local editor CLI bridge; requests live in ignored Library and never run on another machine.</summary>
    [InitializeOnLoad]
    public static class FireflyEditorBridge
    {
        [Serializable] private sealed class Request { public string action=""; public string id=""; }
        [Serializable] private sealed class Result { public string id; public string action; public bool success; public string message; }
        private const string RequestPath="Library/FireflyCommand.json";
        private const string ResultPath="Library/FireflyCommandResult.json";
        private static double nextCheck;
        static FireflyEditorBridge() { EditorApplication.update+=Update; Application.logMessageReceived+=Log; }
        private static void Log(string condition,string stack,LogType type)
        {
            if (type!=LogType.Error && type!=LogType.Exception && type!=LogType.Assert) return;
            try { File.AppendAllText("Library/FireflyRuntimeErrors.log",DateTime.UtcNow.ToString("O")+" "+condition+"\n"+stack+"\n"); } catch (IOException) { }
        }
        private static void Update()
        {
            if (EditorApplication.timeSinceStartup<nextCheck || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextCheck=EditorApplication.timeSinceStartup+0.5;
            if (!File.Exists(RequestPath)) return;
            Request request;
            try { request=JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath)); File.Delete(RequestPath); }
            catch (Exception e) { Debug.LogException(e); return; }
            var result=new Result { id=request.id,action=request.action };
            try
            {
                switch (request.action)
                {
                    case "build": FireflySceneBuilder.Build(); result.message=FireflySceneBuilder.ScenePath; break;
                    case "validate": FireflySmokeCheck.Begin(); result.message="Play Mode checks started"; break;
                    case "capture":
                        Directory.CreateDirectory("../Artifacts");
                        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Capture requires Play Mode.");
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("../Artifacts/NightMeadow.png"));
                        result.message="Artifacts/NightMeadow.png"; break;
                    case "play": EditorApplication.isPlaying=true; result.message="Entering Play Mode"; break;
                    case "stop": EditorApplication.isPlaying=false; result.message="Leaving Play Mode"; break;
                    case "quit":
                        if (!Application.isBatchMode) throw new InvalidOperationException("Quit is only available for the validation batch editor.");
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before quitting the validation editor.");
                        result.message="Closing validation batch editor";
                        EditorApplication.delayCall+=()=>EditorApplication.Exit(0); break;
                    case "inspect": result.message=Inspect(); break;
                    case "save":
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Save requires Edit Mode.");
                        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                        if (scene.path!=FireflySceneBuilder.ScenePath) throw new InvalidOperationException("Save only targets the generated NightMeadow scene.");
                        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                        AssetDatabase.SaveAssets(); result.message="NightMeadow saved"; break;
                    default: throw new ArgumentException("Unknown Firefly editor command.");
                }
                result.success=true;
            }
            catch (Exception e) { result.message=e.ToString(); Debug.LogException(e); }
            File.WriteAllText(ResultPath,JsonUtility.ToJson(result,true));
        }

        private static string Inspect()
        {
            var renderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var filters=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            long triangles=0; int missing=0; var shaders=new System.Collections.Generic.HashSet<Shader>();
            foreach (var filter in filters)
                if (filter.sharedMesh) triangles+=filter.sharedMesh.triangles.Length/3;
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                    if (!material || !material.shader) missing++; else shaders.Add(material.shader);
            var errors=new System.Collections.Generic.List<string>();
            foreach (var shader in shaders)
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        errors.Add(shader.name+": "+message.message);
            var report=new Inspection { renderers=renderers.Length,triangles=triangles,missingMaterials=missing,shaderErrors=errors.ToArray() };
            string json=JsonUtility.ToJson(report,true); File.WriteAllText("Library/FireflySceneInspection.json",json);
            return json;
        }
        [Serializable] private sealed class Inspection
        {
            public int renderers; public long triangles; public int missingMaterials; public string[] shaderErrors;
        }
    }
}
