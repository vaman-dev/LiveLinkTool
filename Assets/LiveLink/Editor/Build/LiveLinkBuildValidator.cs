using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using LiveLink.Editor.Identity;

namespace LiveLink.Editor.Build
{
    public sealed class LiveLinkBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) == 0)
                return;

            ValidateBuildScenes();
        }

        [MenuItem("Tools/LiveLink/Validate Development Build Scenes")]
        public static void ValidateBuildScenes()
        {
            var paths = new List<string>();

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    paths.Add(scene.path);
            }

            IdentityValidationResult result = IdentityValidator.ValidateScenePaths(paths);
            string fingerprint = IdentityManifest.Compute(paths);

            if (result.IsValid)
            {
                Debug.Log("[LiveLink] Build identity validation passed. Manifest fingerprint: " + fingerprint);
                return;
            }

            result.Log();
            throw new BuildFailedException(
                "[LiveLink] Development build blocked because identity validation found " +
                result.ErrorCount + " error(s). Resolve the reported identities before building.");
        }
    }
}
