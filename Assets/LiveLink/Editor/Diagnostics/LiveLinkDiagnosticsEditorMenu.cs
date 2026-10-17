#if UNITY_EDITOR
using System;
using LiveLink.Diagnostics;
using LiveLink.Editor.Connection;
using UnityEditor;
using UnityEngine;
using UnityEditor.Networking.PlayerConnection;

namespace LiveLink.Editor.Diagnostics
{
    [InitializeOnLoad]
    internal static class LiveLinkDiagnosticsEditorMenu
    {
        static LiveLinkDiagnosticsEditorMenu()
        {
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            foreach (string report in LiveLinkDiagnostics.DrainReports())
                Debug.Log("[LiveLink Diagnostic]" + Environment.NewLine + report);
        }

        [MenuItem("Tools/LiveLink/Print Connection Diagnostic")]
        private static void PrintConnectionDiagnostic()
        {
            LiveLinkDiscoveryService.EnsureStarted();
            string address;
            string addressDiagnostic;
            bool addressFound = LiveLinkNetworkDetector.TryGetPreferredAddress(
                out address, out addressDiagnostic);
            Debug.Log("========== LIVELINK CONNECTION DIAGNOSTIC REPORT ==========" +
                Environment.NewLine +
                "Editor IPv4: " + (addressFound ? address : "UNAVAILABLE") +
                " | " + addressDiagnostic + Environment.NewLine +
                "TCP listener: " + LiveLinkDiscoveryService.ListenerStatus +
                Environment.NewLine +
                "UDP broadcaster: " + LiveLinkDiscoveryService.BroadcasterStatus +
                Environment.NewLine +
                "Native PlayerConnection sessions: " +
                EditorConnection.instance.ConnectedPlayers.Count +
                Environment.NewLine + LiveLinkDiagnostics.FormatActiveAndHistory());

            foreach (var session in LiveLinkCompatibilityManager.GetSessions())
            {
                Debug.Log("[LiveLink Diagnostic][PlayerConnection] " +
                    session.Key + " | " + session.State + " | " + session.Diagnostic);
            }
        }
    }
}
#endif
