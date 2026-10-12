#if UNITY_EDITOR
using System;
using System.Text.RegularExpressions;

namespace LiveLink.Editor.Connection
{
    public static class LiveLinkCompatibilityEvaluator
    {
        public static bool CanSynchronize(LiveLinkCompatibilityState state)
        {
            return state == LiveLinkCompatibilityState.Compatible;
        }
        public static LiveLinkCompatibilityState Evaluate(
            int protocolVersion,
            string playerFingerprint,
            string editorFingerprint)
        {
            if (protocolVersion !=
                LiveLinkCompatibilityManager.SupportedProtocolVersion)
            {
                return LiveLinkCompatibilityState.Incompatible;
            }

            if (string.IsNullOrWhiteSpace(playerFingerprint) ||
                !Regex.IsMatch(
                    playerFingerprint.Trim(),
                    "^[0-9a-fA-F]{64}$"))
            {
                return LiveLinkCompatibilityState.VerificationFailed;
            }

            if (!string.Equals(
                    playerFingerprint.Trim(),
                    editorFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return LiveLinkCompatibilityState.Mismatch;
            }

            return LiveLinkCompatibilityState.Compatible;
        }
    }
}
#endif
