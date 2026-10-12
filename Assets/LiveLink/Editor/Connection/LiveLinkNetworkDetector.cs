#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

namespace LiveLink.Editor.Connection
{
    public static class LiveLinkNetworkDetector
    {
        public sealed class Candidate
        {
            public string Address { get; }
            public string InterfaceName { get; }
            public NetworkInterfaceType InterfaceType { get; }
            public int Score { get; }
            public bool IsVirtual { get; }

            internal Candidate(
                string address,
                string interfaceName,
                NetworkInterfaceType interfaceType,
                int score,
                bool isVirtual)
            {
                Address = address;
                InterfaceName = interfaceName;
                InterfaceType = interfaceType;
                Score = score;
                IsVirtual = isVirtual;
            }

            public override string ToString()
            {
                return Address + " (" + InterfaceName + ")";
            }
        }

        public static IReadOnlyList<Candidate> GetCandidates()
        {
            var candidates = new List<Candidate>();

            foreach (NetworkInterface networkInterface in
                     NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus !=
                    OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType ==
                        NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType ==
                        NetworkInterfaceType.Tunnel ||
                    networkInterface.NetworkInterfaceType ==
                        NetworkInterfaceType.Unknown)
                {
                    continue;
                }

                bool isVirtual = IsVirtualInterface(networkInterface);
                int score = ScoreInterface(networkInterface, isVirtual);

                IPInterfaceProperties properties =
                    networkInterface.GetIPProperties();

                foreach (UnicastIPAddressInformation address in
                         properties.UnicastAddresses)
                {
                    if (!IsUsableIPv4(address.Address))
                    {
                        continue;
                    }

                    candidates.Add(new Candidate(
                        address.Address.ToString(),
                        networkInterface.Name,
                        networkInterface.NetworkInterfaceType,
                        score,
                        isVirtual));
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Address,
                    StringComparer.Ordinal)
                .ToArray();
        }

        public static bool TryGetPreferredAddress(
            out string address,
            out string diagnostic)
        {
            IReadOnlyList<Candidate> candidates = GetCandidates();

            if (candidates.Count == 0)
            {
                address = string.Empty;
                diagnostic = "No usable non-loopback IPv4 address found.";
                return false;
            }

            int bestScore = candidates[0].Score;
            List<Candidate> best = candidates
                .Where(candidate => candidate.Score == bestScore)
                .ToList();

            if (best.Count != 1)
            {
                address = string.Empty;
                diagnostic = "Multiple equally preferred IPv4 addresses: " +
                    string.Join(", ", best.Select(candidate =>
                        candidate.Address));
                return false;
            }

            address = best[0].Address;
            diagnostic = "Selected " + best[0] + ".";
            return true;
        }

        public static bool IsUsableIPv4(IPAddress address)
        {
            if (address == null ||
                address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            byte[] bytes = address.GetAddressBytes();

            // Exclude loopback and IPv4 link-local/APIPA addresses.
            if (bytes[0] == 127 ||
                (bytes[0] == 169 && bytes[1] == 254))
            {
                return false;
            }

            return true;
        }

        public static bool TryReadBootConfigAddress(
            string buildOutputPath,
            out string address,
            out string diagnostic)
        {
            address = string.Empty;
            diagnostic = string.Empty;

            string bootConfigPath = GetBootConfigPath(buildOutputPath);

            if (string.IsNullOrEmpty(bootConfigPath) ||
                !File.Exists(bootConfigPath))
            {
                diagnostic = "Player boot.config was not found at " +
                    bootConfigPath;
                return false;
            }

            foreach (string line in File.ReadAllLines(bootConfigPath))
            {
                const string key = "player-connection-ip=";

                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                address = line.Substring(key.Length).Trim();
                diagnostic = "Player boot.config contains " + address + ".";
                return !string.IsNullOrEmpty(address);
            }

            diagnostic = "player-connection-ip is not present in boot.config.";
            return false;
        }

        public static string GetBootConfigPath(string buildOutputPath)
        {
            if (string.IsNullOrEmpty(buildOutputPath))
            {
                return string.Empty;
            }

            string outputDirectory = Path.GetDirectoryName(buildOutputPath);
            string executableName =
                Path.GetFileNameWithoutExtension(buildOutputPath);

            if (string.IsNullOrEmpty(outputDirectory) ||
                string.IsNullOrEmpty(executableName))
            {
                return string.Empty;
            }

            return Path.Combine(
                outputDirectory,
                executableName + "_Data",
                "boot.config");
        }

        [MenuItem("Tools/LiveLink/Refresh Network Configuration")]
        private static void LogNetworkConfiguration()
        {
            IReadOnlyList<Candidate> candidates = GetCandidates();

            if (TryGetPreferredAddress(out string address,
                    out string diagnostic))
            {
                Debug.Log("[LiveLink] Network address: " + address +
                    ". " + diagnostic + " Candidates: " +
                    string.Join(", ", candidates));
                return;
            }

            Debug.LogWarning("[LiveLink] Network address selection is " +
                "ambiguous or unavailable. " + diagnostic +
                " Candidates: " + string.Join(", ", candidates));
        }

        private static int ScoreInterface(
            NetworkInterface networkInterface,
            bool isVirtual)
        {
            int score;

            switch (networkInterface.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Ethernet:
                    score = 100;
                    break;
                case NetworkInterfaceType.Wireless80211:
                    score = 90;
                    break;
                default:
                    score = 70;
                    break;
            }

            return isVirtual ? score - 100 : score;
        }

        private static bool IsVirtualInterface(
            NetworkInterface networkInterface)
        {
            string value = (networkInterface.Name + " " +
                networkInterface.Description).ToUpperInvariant();

            string[] markers =
            {
                "VPN", "VIRTUAL", "VMWARE", "VIRTUALBOX", "HYPER-V",
                "TAP", "WSL", "DOCKER", "LOOPBACK"
            };

            return markers.Any(value.Contains);
        }
    }
}
#endif
