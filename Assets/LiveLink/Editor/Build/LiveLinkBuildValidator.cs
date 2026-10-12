using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using LiveLink.Editor.Identity;
using LiveLink.Editor.Connection;

namespace LiveLink.Editor.Build
{
    public sealed class LiveLinkBuildValidator :
        IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) == 0)
            {
                // Never allow a development-only manifest to leak into a
                // normal release build if a previous build was interrupted.
                LiveLinkManifestBaker.Cleanup();
                return;
            }

            ValidateBuildScenes();
        }

        public void OnPostprocessBuild(
            BuildReport report)
        {
            ValidatePlayerConnectionAddress(report);
            LiveLinkManifestBaker.Cleanup();
        }

        private static void ValidatePlayerConnectionAddress(
            BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) == 0)
                return;

            bool hasDetectedAddress =
                LiveLinkNetworkDetector.TryGetPreferredAddress(
                    out string detectedAddress,
                    out string detectionDiagnostic);

            bool hasBakedAddress =
                LiveLinkNetworkDetector.TryReadBootConfigAddress(
                    report.summary.outputPath,
                    out string bakedAddress,
                    out string bootDiagnostic);

            if (!hasDetectedAddress || !hasBakedAddress)
            {
                Debug.LogWarning(
                    "[LiveLink] Could not verify the Development Player " +
                    "connection address. " + detectionDiagnostic + " " +
                    bootDiagnostic);
                return;
            }

            if (!string.Equals(
                    detectedAddress,
                    bakedAddress,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    "[LiveLink] Development Player connection address " +
                    "differs from the current detected address. Baked: " +
                    bakedAddress + "; current: " + detectedAddress + ". " +
                    "Rebuild after changing networks.");
                return;
            }

            Debug.Log(
                "[LiveLink] Development Player connection address verified: " +
                bakedAddress);
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
                if (!LiveLinkManifestBaker.Bake(fingerprint))
                {
                    throw new BuildFailedException(
                        "[LiveLink] Development build blocked because " +
                        "the identity manifest could not be baked.");
                }

                Debug.Log(
                    "[LiveLink] Build identity validation passed. " +
                    "Manifest fingerprint: " + fingerprint);
                return;
            }

            result.Log();
            throw new BuildFailedException(
                "[LiveLink] Development build blocked because identity validation found " +
                result.ErrorCount + " error(s). Resolve the reported identities before building.");
        }
    }
}
