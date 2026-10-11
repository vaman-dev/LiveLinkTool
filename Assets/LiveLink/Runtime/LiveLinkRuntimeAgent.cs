

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

       [RuntimeInitializeOnLoadMethod(
    RuntimeInitializeLoadType.AfterSceneLoad)]
private static void Bootstrap()
{
    if (!Debug.isDebugBuild)
        return;

if (instance != null)
return;
GameObject host =
new GameObject("[LiveLink Runtime]");
DontDestroyOnLoad(host);
instance =
host.AddComponent<LiveLinkRuntimeAgent>();
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
                        Reply("OK", "PONG");
                        break;

                    case "TRANSFORM":
                        ApplyTransform(cmd);
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

       
private void ApplyTransform(LiveLinkCommand cmd)
{
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

    Reply("OK", "Updated: " + cmd.objectId);
}


        private void Reply(string status, string message)
        {
            LiveLinkResponse response = new LiveLinkResponse
            {
                status = status,
                message = message
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
