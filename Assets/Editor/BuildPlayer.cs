using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Repro.Editor
{
    /// <summary>
    /// Builds an IL2CPP standalone player for the editor's platform into Build/.
    /// Menu: Repro > Build IL2CPP Player. Batch mode: Unity -batchmode -projectPath . -executeMethod Repro.Editor.BuildPlayer.Build
    /// </summary>
    public static class BuildPlayer
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Repro/Build IL2CPP Player")]
        public static void Build()
        {
            Configure();

            BuildTarget target;
            string file;
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    target = BuildTarget.StandaloneWindows64;
                    file = "MessagePackIl2CppRepro.exe";
                    break;
                case RuntimePlatform.OSXEditor:
                    target = BuildTarget.StandaloneOSX;
                    file = "MessagePackIl2CppRepro.app";
                    break;
                default:
                    target = BuildTarget.StandaloneLinux64;
                    file = "MessagePackIl2CppRepro";
                    break;
            }

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                locationPathName = Path.Combine("Build", target.ToString(), file),
            });
            Debug.Log($"Build {report.summary.result}: {report.summary.outputPath}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }

        // The settings the repro needs. They are saved in ProjectSettings, so File > Build And Run works too.
        private static void Configure()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1024;
            PlayerSettings.defaultScreenHeight = 400;
            PlayerSettings.runInBackground = true;
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single), ScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }
    }
}
