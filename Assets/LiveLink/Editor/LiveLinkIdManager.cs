using System;
using System.Collections.Generic;
using LiveLink;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LiveLink.Editor.Identity;

namespace LiveLink.Editor
{
    [InitializeOnLoad]
    public static class LiveLinkIdManager
    {
        private sealed class Entry
        {
            public LiveLinkIdentity identity;
            public string key;
            public string id;
            public string owner;
        }

        private static bool scanRequested = true;
        private static double nextScan;

        static LiveLinkIdManager()
        {
            EditorApplication.hierarchyChanged += RequestScan;
            Undo.undoRedoPerformed += RequestScan;
            EditorApplication.update += Update;
            EditorSceneManager.sceneOpened += (scene, mode) => RequestScan();
            EditorApplication.delayCall += RequestScan;
        }

        private static void RequestScan()
        {
            scanRequested = true;
        }

        private static void Update()
        {
            if (!scanRequested)
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
                return;

            if (EditorApplication.timeSinceStartup < nextScan)
                return;

            scanRequested = false;
            nextScan = EditorApplication.timeSinceStartup + 0.2;
            IdentityAutoInitializer.Process();
            ValidateLoadedScenes();
        }

        [MenuItem("Tools/LiveLink/Validate Object IDs")]
        public static void ValidateLoadedScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // v0.2.4 validation is read-only. Repairs are explicit and
            // are performed through IdentityRepairService.
            IdentityValidationResult result =
                IdentityValidator.Validate();
            result.Log();
            return;

#if LIVELINK_LEGACY_MUTATING_VALIDATOR
            var entries = new List<Entry>();
            var groups = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);

                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                if (string.IsNullOrEmpty(scene.path))
                {
                    Debug.LogWarning("[LiveLink] Save the scene before generating persistent IDs: " + scene.name);
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    var components = root.GetComponentsInChildren<LiveLinkIdentity>(true);

                    foreach (var identity in components)
                    {
                        if (identity == null)
                            continue;

                        string key = GlobalObjectId.GetGlobalObjectIdSlow(identity.gameObject).ToString();
                        var entry = new Entry
                        {
                            identity = identity,
                            key = key,
                            id = identity.ObjectId,
                            owner = identity.AuthoringKey
                        };

                        entries.Add(entry);

                        if (string.IsNullOrWhiteSpace(entry.id))
                            continue;

                        if (!groups.TryGetValue(entry.id, out var group))
                        {
                            group = new List<Entry>();
                            groups.Add(entry.id, group);
                        }

                        group.Add(entry);
                    }
                }
            }

            int generated = 0;
            int corrected = 0;
            int claimed = 0;
            int conflicts = 0;

            var used = new HashSet<string>(groups.Keys, StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.id))
                    continue;

                Assign(entry, NewId(used), entry.key);
                generated++;
            }

            foreach (var pair in groups)
            {
                List<Entry> group = pair.Value;

                if (group.Count == 1)
                {
                    Entry entry = group[0];

                    if (string.IsNullOrEmpty(entry.owner))
                    {
                        Assign(entry, entry.id, entry.key);
                        claimed++;
                    }
                    else if (entry.owner != entry.key)
                    {
                        Debug.LogWarning("[LiveLink] Authoring identity changed: " + entry.identity.name + ". Review before updating its ID.");
                        conflicts++;
                    }

                    continue;
                }

                Entry original = null;
                int validOwners = 0;

                foreach (Entry entry in group)
                {
                    if (entry.owner == entry.key)
                    {
                        original = entry;
                        validOwners++;
                    }
                }

                if (validOwners != 1)
                {
                    Debug.LogError("[LiveLink] Ambiguous duplicate ID '" + pair.Key + "'. No unique original owner was found. Manual resolution required.");
                    conflicts++;
                    continue;
                }

                foreach (Entry entry in group)
                {
                    if (entry == original)
                        continue;

                    Assign(entry, NewId(used), entry.key);
                    corrected++;
                }
            }

            if (generated + corrected + claimed + conflicts > 0)
            {
                Debug.Log($"[LiveLink] IDs: generated={generated}, duplicates fixed={corrected}, legacy claimed={claimed}, conflicts={conflicts}");
            }
#endif
        }

        private static string NewId(HashSet<string> used)
        {
            string id;

            do
            {
                id = Guid.NewGuid().ToString("N");
            }
            while (!used.Add(id));

            return id;
        }

        private static void Assign(Entry entry, string id, string key)
        {
            var serialized = new SerializedObject(entry.identity);
            serialized.Update();
            serialized.FindProperty("objectId").stringValue = id;
            serialized.FindProperty("authoringKey").stringValue = key;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (PrefabUtility.IsPartOfPrefabInstance(entry.identity))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(entry.identity);
            }

            EditorSceneManager.MarkSceneDirty(entry.identity.gameObject.scene);
        }
    }
}
