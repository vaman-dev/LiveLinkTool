#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LiveLink.Editor.Build
{
    public static class LiveLinkV03MaterialBuildCommand
    {
        private const string OutputPath =
            "Builds/Development/LiveLink_v0.3_Material.exe";

        [MenuItem("Tools/LiveLink/Build v0.3 Material Development Player")]
        public static void BuildMaterialPlayer()
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            string outputPath = Path.Combine(projectRoot, OutputPath);
            string developmentDirectory =
                Path.GetDirectoryName(outputPath);

            try
            {
                CleanDevelopmentDirectory(developmentDirectory);
                Directory.CreateDirectory(developmentDirectory);
                LiveLinkBuildValidator.ValidateBuildScenes();

                string[] scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.enabled &&
                        !string.IsNullOrEmpty(scene.path))
                    .Select(scene => scene.path)
                    .ToArray();

                BuildReport report = BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = outputPath,
                        target = BuildTarget.StandaloneWindows64,
                        options = BuildOptions.Development |
                            BuildOptions.AllowDebugging
                    });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        "[LiveLink] v0.3 Material Development Player build failed.");
                }

                Debug.Log("[LiveLink] v0.3 Material Development Player built: " +
                    outputPath);
            }
            finally
            {
                LiveLinkManifestBaker.Cleanup();
            }
        }

        private static void CleanDevelopmentDirectory(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (string file in Directory.GetFiles(directory))
                File.Delete(file);

            foreach (string childDirectory in Directory.GetDirectories(directory))
                Directory.Delete(childDirectory, true);
        }
    }
}
#endif
