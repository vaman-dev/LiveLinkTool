
using System;
using UnityEngine;

namespace LiveLink
{
    public static class LiveLinkProtocol
    {
        public static readonly Guid CommandId =
            new Guid("a8525cfb-6730-4fa1-b42f-c617181ab361");

        public static readonly Guid ResponseId =
            new Guid("4be8ca7b-502e-4d42-b14b-a4829c2243bc");
    }

    [Serializable]
    public class LiveLinkCommand
    {
        public string requestId;
        public string type;
        public string manifestFingerprint;
        public string authPairingId;
        public string authChallenge;
        public string authProof;
        public string objectId;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public int lightComponentIndex;
        public LiveLinkLightState light;
        public int rendererComponentIndex;
        public int materialSlot;
        public string materialProperty;
        public LiveLinkMaterialState material;
    }

    [Serializable]
    public class LiveLinkResponse
    {
        public string requestId;
        public string status;
        public string message;
        public LiveLinkHandshakeResponse handshake;
        public string authPairingId;
        public string authChallenge;
        public string authProof;
    }

    [Serializable]
    public class LiveLinkHandshakeResponse
    {
        public int protocolVersion;
        public string fingerprint;
        public string buildName;
    }

    [Serializable]
    public class LiveLinkLightState
    {
        public bool enabled;
        public float intensity;
        public Color color;
        public float range;
        public float spotAngle;
    }

    public static class LiveLinkLightStateValidator
    {
        public static bool TryValidate(
            LiveLinkLightState state,
            out string error)
        {
            error = null;

            if (state == null)
            {
                error = "Light state is missing.";
                return false;
            }

            if (!IsFinite(state.intensity) || state.intensity < 0f)
            {
                error = "Light intensity must be finite and non-negative.";
                return false;
            }

            if (!IsFinite(state.range) || state.range <= 0f)
            {
                error = "Light range must be finite and greater than zero.";
                return false;
            }

            if (!IsFinite(state.spotAngle) ||
                state.spotAngle < 1f || state.spotAngle > 179f)
            {
                error = "Spot angle must be between 1 and 179 degrees.";
                return false;
            }

            if (!IsFinite(state.color.r) ||
                !IsFinite(state.color.g) ||
                !IsFinite(state.color.b) ||
                !IsFinite(state.color.a))
            {
                error = "Light color must contain finite values.";
                return false;
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class LiveLinkMaterialState
    {
        public Color color;
        public float value;
    }

    public static class LiveLinkMaterialStateValidator
    {
        public static bool IsSupportedProperty(string property)
        {
            return property == "BaseColor" ||
                property == "Metallic" ||
                property == "Smoothness" ||
                property == "EmissionColor";
        }

        public static bool TryValidate(
            string property,
            LiveLinkMaterialState state,
            out string error)
        {
            error = null;

            if (!IsSupportedProperty(property))
            {
                error = "Material property is not supported.";
                return false;
            }

            if (state == null)
            {
                error = "Material state is missing.";
                return false;
            }

            if (property == "BaseColor" || property == "EmissionColor")
            {
                if (!IsFinite(state.color.r) ||
                    !IsFinite(state.color.g) ||
                    !IsFinite(state.color.b) ||
                    !IsFinite(state.color.a))
                {
                    error = "Material color must contain finite values.";
                    return false;
                }
            }
            else if (!IsFinite(state.value) ||
                state.value < 0f || state.value > 1f)
            {
                error = "Material numeric values must be between 0 and 1.";
                return false;
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
