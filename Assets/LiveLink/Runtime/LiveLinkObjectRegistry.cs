
#if LIVELINK_ENABLED && !UNITY_EDITOR

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveLink
{
    public static class LiveLinkObjectRegistry
    {
        private static readonly Dictionary<string,
            HashSet<LiveLinkIdentity>> objects = new();

        public static int RegisteredIds => objects.Count;

        public static void Register(LiveLinkIdentity identity)
        {
            if (identity == null ||
                string.IsNullOrWhiteSpace(identity.ObjectId))
                return;

            string id = identity.ObjectId;

            if (!objects.TryGetValue(id, out var entries))
            {
                entries = new HashSet<LiveLinkIdentity>();
                objects.Add(id, entries);
            }

            entries.Add(identity);

            if (entries.Count > 1)
            {
                Debug.LogError(
                    $"[LiveLink] Duplicate ID '{id}'. " +
                    "Assign a unique ID to each object.");
            }
        }

        public static void Unregister(LiveLinkIdentity identity)
        {
            if (ReferenceEquals(identity, null))
                return;

            string id = identity.ObjectId;

            if (!objects.TryGetValue(id, out var entries))
                return;

            entries.Remove(identity);

            if (entries.Count == 0)
                objects.Remove(id);
        }

        public static bool TryGet(
            string id,
            out LiveLinkIdentity identity,
            out string error)
        {
            identity = null;
            error = null;

            if (string.IsNullOrWhiteSpace(id) ||
                !objects.TryGetValue(id, out var entries))
            {
                error = "Unknown object ID: " + id;
                return false;
            }

            // Remove references to destroyed Unity objects.
            entries.RemoveWhere(item => item == null);

            if (entries.Count == 0)
            {
                objects.Remove(id);
                error = "Object no longer exists: " + id;
                return false;
            }

            if (entries.Count > 1)
            {
                error = "Duplicate object ID: " + id;
                return false;
            }

            foreach (var item in entries)
            {
                identity = item;
                return true;
            }

            error = "Object not found: " + id;
            return false;
        }

        public static void ScanLoadedScenes()
        {
            objects.Clear();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded)
                    continue;

                foreach (GameObject root in
                         scene.GetRootGameObjects())
                {
                    var identities =
                        root.GetComponentsInChildren<
                            LiveLinkIdentity>(true);

                    foreach (var identity in identities)
                        Register(identity);
                }
            }

            Debug.Log(
                $"[LiveLink] Indexed {objects.Count} IDs.");
        }

        public static void Clear()
        {
            objects.Clear();
        }
    }
}

#endif
