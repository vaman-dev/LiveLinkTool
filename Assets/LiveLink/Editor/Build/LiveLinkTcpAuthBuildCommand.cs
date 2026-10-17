#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LiveLink.Editor.Build
{
    public static class LiveLinkTcpAuthBuildCommand
    {
        private const string OutputPath =
            "Builds/Development/LiveLink_v0.2.5_TCPAuth.exe";

        [MenuItem("Tools/LiveLink/Build TCP Auth Development Player")]
        public static void BuildTcpAuth()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var outputPath = Path.Combine(projectRoot, OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            try
            {
                // Explicitly bake before BuildPipeline.BuildPlayer. This keeps
                // command-line builds independent of callback discovery order.
                LiveLinkBuildValidator.ValidateBuildScenes();

                var scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                    .Select(scene => scene.path)
                    .ToArray();

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        "[LiveLink] TCP-auth Development Player build failed.");
                }

                Debug.Log("[LiveLink] TCP-auth Development Player built: " +
                    outputPath);
            }
            finally
            {
                LiveLinkManifestBaker.Cleanup();
            }
        }
    }
}
#endif
