

#if LIVELINK_ENABLED && !UNITY_EDITOR

using System;
using System.Text;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;

namespace LiveLink
{
    public sealed class LiveLinkRuntimeAgent : MonoBehaviour
    {
        private static LiveLinkRuntimeAgent instance;
        private bool transformAuthorizationGranted;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            Debug.Log("[LiveLink] Runtime bootstrap reached");

            GameObject host =
                new GameObject("[LiveLink Runtime]");
            DontDestroyOnLoad(host);
            instance =
                host.AddComponent<LiveLinkRuntimeAgent>();
            LiveLinkDiscoveryAgent.Start();
        }


private void OnEnable()
{
    PlayerConnection.instance.Register(
        LiveLinkProtocol.CommandId,
        OnCommand);

    SceneManager.sceneLoaded += OnSceneLoaded;

    // Index all objects from the initial loaded scenes.
    LiveLinkObjectRegistry.ScanLoadedScenes();

    Debug.Log("[LiveLink] Runtime agent v0.2 started");
}

private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
{
    // Refresh the registry after additive or normal scene loads.
    LiveLinkObjectRegistry.ScanLoadedScenes();
}


private void OnDisable()
{
    SceneManager.sceneLoaded -= OnSceneLoaded;

    PlayerConnection.instance.Unregister(
        LiveLinkProtocol.CommandId,
        OnCommand);

    LiveLinkObjectRegistry.Clear();
}

        private void OnCommand(
            UnityEngine.Networking.PlayerConnection.MessageEventArgs args)
        {
            try
            {
                string json = Encoding.UTF8.GetString(args.data);
                LiveLinkCommand cmd =
                    JsonUtility.FromJson<LiveLinkCommand>(json);

                if (cmd == null)
                {
                    Reply("ERROR", "Invalid command");
                    return;
                }

                switch (cmd.type)
                {
                    case "PING":
                        Reply("OK", "PONG", null, cmd.requestId);
                        break;

                    case "MANIFEST_REQUEST":
                        SendManifestResponse(cmd.requestId);
                        break;

                    case "SYNC_AUTHORIZATION":
                        AuthorizeTransforms(cmd);
                        break;

                    case "TRANSFORM":
                        ApplyTransform(cmd);
                        break;

                    case "LIGHT_UPDATE":
                        ApplyLight(cmd);
                        break;

                    case "MATERIAL_UPDATE":
                        ApplyMaterial(cmd);
                        break;

                    default:
                        Reply("ERROR", "Unknown command");
                        break;
                }
            }
            catch (Exception e)
            {
                Reply("ERROR", e.Message);
            }
        }

        private void AuthorizeTransforms(LiveLinkCommand command)
        {
            LiveLinkBuildIdentity identity =
                Resources.Load<LiveLinkBuildIdentity>(
                    "LiveLinkBuildIdentity");

            transformAuthorizationGranted =
                identity != null &&
                !string.IsNullOrWhiteSpace(identity.ManifestFingerprint) &&
                string.Equals(
                    identity.ManifestFingerprint,
                    command.manifestFingerprint,
                    StringComparison.OrdinalIgnoreCase);

            Reply(
                transformAuthorizationGranted ? "OK" : "ERROR",
                transformAuthorizationGranted
                    ? "SYNC_AUTHORIZED"
                    : "Transform authorization fingerprint mismatch.",
                null,
                command.requestId);
        }

        private void SendManifestResponse(string requestId)
        {
            LiveLinkBuildIdentity identity =
                Resources.Load<LiveLinkBuildIdentity>(
                    "LiveLinkBuildIdentity");

            if (identity == null)
            {
                Reply(
                    "ERROR",
                    "Development manifest resource is missing.",
                    null,
                    requestId);
                return;
            }

            if (identity.ProtocolVersion <= 0 ||
                string.IsNullOrWhiteSpace(
                    identity.ManifestFingerprint))
            {
                Reply(
                    "ERROR",
                    "Development manifest resource is invalid.",
                    null,
                    requestId);
                return;
            }

            Reply(
                "OK",
                "MANIFEST_RESPONSE",
                new LiveLinkHandshakeResponse
                {
                    protocolVersion = identity.ProtocolVersion,
                    fingerprint = identity.ManifestFingerprint,
                    buildName = "LiveLink"
                },
                requestId);
        }

       
        private void ApplyTransform(LiveLinkCommand cmd)
{
    if (!transformAuthorizationGranted)
    {
        Reply(
            "ERROR",
            "Transform synchronization is not authorized for this build.",
            null,
            cmd.requestId);
        return;
    }

    if (!LiveLinkObjectRegistry.TryGet(
        cmd.objectId, out var identity, out var error))
    {
        Reply("ERROR", error);
        return;
    }

    Transform target = identity.transform;

    target.localPosition = cmd.position;

    target.localRotation =
        Quaternion.Euler(cmd.rotation);

    target.localScale = cmd.scale;

            Reply("OK", "Updated: " + cmd.objectId, null, cmd.requestId);
}

        private void ApplyLight(LiveLinkCommand command)
        {
            if (!transformAuthorizationGranted)
            {
                Reply("ERROR",
                    "Light synchronization is not authorized for this build.",
                    null, command.requestId);
                return;
            }

            if (!LiveLinkLightStateValidator.TryValidate(
                    command.light, out string validationError))
            {
                Reply("ERROR", validationError, null, command.requestId);
                return;
            }

            if (!LiveLinkObjectRegistry.TryGet(
                    command.objectId, out var identity, out var error))
            {
                Reply("ERROR", error, null, command.requestId);
                return;
            }

            Light[] lights = identity.GetComponents<Light>();
            if (command.lightComponentIndex < 0 ||
                command.lightComponentIndex >= lights.Length)
            {
                Reply("ERROR", "Light component index is invalid.",
                    null, command.requestId);
                return;
            }

            Light target = lights[command.lightComponentIndex];
            target.enabled = command.light.enabled;
            target.intensity = command.light.intensity;
            target.color = command.light.color;

            if (target.type == LightType.Point ||
                target.type == LightType.Spot)
            {
                target.range = command.light.range;
            }

            if (target.type == LightType.Spot)
                target.spotAngle = command.light.spotAngle;

            Reply("OK", "Updated Light: " + command.objectId,
                null, command.requestId);
        }

        private void ApplyMaterial(LiveLinkCommand command)
        {
            if (!transformAuthorizationGranted)
            {
                Reply("ERROR",
                    "Material synchronization is not authorized for this build.",
                    null, command.requestId);
                return;
            }

            if (!LiveLinkMaterialStateValidator.TryValidate(
                    command.materialProperty,
                    command.material,
                    out string validationError))
            {
                Reply("ERROR", validationError, null, command.requestId);
                return;
            }

            if (!LiveLinkObjectRegistry.TryGet(
                    command.objectId, out var identity, out var error))
            {
                Reply("ERROR", error, null, command.requestId);
                return;
            }

            Renderer[] renderers = identity.GetComponents<Renderer>();
            if (command.rendererComponentIndex < 0 ||
                command.rendererComponentIndex >= renderers.Length)
            {
                Reply("ERROR", "Renderer component index is invalid.",
                    null, command.requestId);
                return;
            }

            Renderer target = renderers[command.rendererComponentIndex];
            Material[] materials = target.sharedMaterials;
            if (command.materialSlot < 0 ||
                command.materialSlot >= materials.Length ||
                materials[command.materialSlot] == null)
            {
                Reply("ERROR", "Material slot is invalid or empty.",
                    null, command.requestId);
                return;
            }

            Material source = materials[command.materialSlot];
            int propertyId;
            if (command.materialProperty == "BaseColor")
                propertyId = source.HasProperty("_BaseColor")
                    ? Shader.PropertyToID("_BaseColor")
                    : source.HasProperty("_Color")
                        ? Shader.PropertyToID("_Color") : -1;
            else if (command.materialProperty == "Metallic")
                propertyId = source.HasProperty("_Metallic")
                    ? Shader.PropertyToID("_Metallic") : -1;
            else if (command.materialProperty == "Smoothness")
                propertyId = source.HasProperty("_Smoothness")
                    ? Shader.PropertyToID("_Smoothness")
                    : source.HasProperty("_Glossiness")
                        ? Shader.PropertyToID("_Glossiness") : -1;
            else
                propertyId = source.HasProperty("_EmissionColor")
                    ? Shader.PropertyToID("_EmissionColor") : -1;

            if (propertyId < 0)
            {
                Reply("ERROR", "The selected shader does not support " +
                    command.materialProperty + ".", null, command.requestId);
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block, command.materialSlot);
            if (command.materialProperty == "BaseColor" ||
                command.materialProperty == "EmissionColor")
                block.SetColor(propertyId, command.material.color);
            else
                block.SetFloat(propertyId, command.material.value);
            target.SetPropertyBlock(block, command.materialSlot);

            Reply("OK", "Updated Material: " + command.objectId,
                null, command.requestId);
        }


        private void Reply(
            string status,
            string message,
            LiveLinkHandshakeResponse handshake = null,
            string requestId = null)
        {
            LiveLinkResponse response = new LiveLinkResponse
            {
                status = status,
                message = message,
                requestId = requestId,
                handshake = handshake
            };

            byte[] data = Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(response));

            PlayerConnection.instance.Send(
                LiveLinkProtocol.ResponseId,
                data);
        }
    }
}

#endif
