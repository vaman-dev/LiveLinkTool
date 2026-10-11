using System;
using System.Collections.Generic;
using LiveLink;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveLink.Editor.Identity
{
    public static class IdentityValidator
    {
        private sealed class Entry
        {
            public LiveLinkIdentity Identity;
            public string Key;
            public string Id;
            public string Owner;
        }

        public static IdentityValidationResult Validate()
        {
            var result = new IdentityValidationResult();
            var entries = new List<Entry>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded)
                    CollectScene(scene, entries, result);
            }

            Analyze(entries, result);
            return result;
        }

        public static IdentityValidationResult ValidateScenePaths(IEnumerable<string> scenePaths)
        {
            var result = new IdentityValidationResult();
            var entries = new List<Entry>();
            var openedScenes = new List<Scene>();

            foreach (string path in scenePaths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                Scene scene = SceneManager.GetSceneByPath(path);
                bool openedHere = false;

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    openedHere = scene.IsValid() && scene.isLoaded;
                }

                try
                {
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        result.Issues.Add(new IdentityValidationIssue(
                            IdentityIssueType.UnsavedScene,
                            IdentityIssueSeverity.Error,
                            IdentityRepairAction.ManualResolution,
                            null, "", "", "",
                            "Could not load build scene for identity validation: " + path));
                        continue;
                    }

                    if (openedHere)
                        openedScenes.Add(scene);

                    CollectScene(scene, entries, result);
                }
                finally
                {
                    // Temporary scenes stay open until Analyze has finished,
                    // because validation entries reference their components.
                }
            }

            try
            {
                Analyze(entries, result);
                return result;
            }
            finally
            {
                foreach (Scene openedScene in openedScenes)
                {
                    if (openedScene.IsValid() && openedScene.isLoaded)
                        EditorSceneManager.CloseScene(openedScene, true);
                }
            }
        }

        public static bool IsObjectIdUsedInBuildScenes(
            string objectId,
            LiveLinkIdentity excluded)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return false;

            var paths = new List<string>();
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (buildScene.enabled && !string.IsNullOrEmpty(buildScene.path))
                    paths.Add(buildScene.path);
            }

            if (paths.Count == 0)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path))
                        paths.Add(scene.path);
                }
            }

            foreach (string path in paths)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool openedHere = false;

                try
                {
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        openedHere = scene.IsValid() && scene.isLoaded;
                    }

                    if (!scene.IsValid() || !scene.isLoaded)
                        continue;

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (LiveLinkIdentity candidate in root.GetComponentsInChildren<LiveLinkIdentity>(true))
                        {
                            if (candidate != null && candidate != excluded && candidate.ObjectId == objectId)
                                return true;
                        }
                    }
                }
                finally
                {
                    if (openedHere && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }

            return false;
        }

        private static void CollectScene(
            Scene scene,
            List<Entry> entries,
            IdentityValidationResult result)
        {
            if (string.IsNullOrEmpty(scene.path))
            {
                result.Issues.Add(new IdentityValidationIssue(
                    IdentityIssueType.UnsavedScene,
                    IdentityIssueSeverity.Error,
                    IdentityRepairAction.ManualResolution,
                    null, "", "", "",
                    "Scene is unsaved. Save it before validating persistent LiveLink identities: " + scene.name));
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (LiveLinkIdentity identity in root.GetComponentsInChildren<LiveLinkIdentity>(true))
                {
                    if (identity == null)
                        continue;

                    if (PrefabUtility.IsPartOfPrefabAsset(identity))
                    {
                        result.Issues.Add(new IdentityValidationIssue(
                            IdentityIssueType.OwnershipMismatch,
                            IdentityIssueSeverity.Error,
                            IdentityRepairAction.ManualResolution,
                            identity, identity.ObjectId, identity.AuthoringKey, "",
                            "Prefab asset identity must be resolved on a scene instance, not on the prefab asset: " + identity.name + "."));
                        continue;
                    }

                    string key = GlobalObjectId.GetGlobalObjectIdSlow(identity).ToString();
                    entries.Add(new Entry
                    {
                        Identity = identity,
                        Key = key,
                        Id = identity.ObjectId,
                        Owner = identity.AuthoringKey
                    });
                }
            }
        }

        private static void Analyze(
            List<Entry> entries,
            IdentityValidationResult result)
        {
            var groups = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);

            foreach (Entry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    result.Issues.Add(new IdentityValidationIssue(
                        IdentityIssueType.MissingObjectId,
                        IdentityIssueSeverity.Error,
                        IdentityRepairAction.ReassignIdentity,
                        entry.Identity, entry.Id, entry.Owner, entry.Key,
                        "Missing LiveLink object ID on " + entry.Identity.name + "."));
                }

                if (string.IsNullOrWhiteSpace(entry.Owner))
                {
                    result.Issues.Add(new IdentityValidationIssue(
                        IdentityIssueType.MissingAuthoringKey,
                        IdentityIssueSeverity.Error,
                        IdentityRepairAction.AdoptIdentity,
                        entry.Identity, entry.Id, entry.Owner, entry.Key,
                        "Missing authoring ownership key on " + entry.Identity.name + "."));
                }
                else if (entry.Owner != entry.Key)
                {
                    result.Issues.Add(new IdentityValidationIssue(
                        IdentityIssueType.OwnershipMismatch,
                        IdentityIssueSeverity.Error,
                        IdentityRepairAction.AdoptIdentity,
                        entry.Identity, entry.Id, entry.Owner, entry.Key,
                        "Authoring ownership mismatch on " + entry.Identity.name + "."));
                }

                if (string.IsNullOrWhiteSpace(entry.Id))
                    continue;

                if (!groups.TryGetValue(entry.Id, out var group))
                {
                    group = new List<Entry>();
                    groups.Add(entry.Id, group);
                }

                group.Add(entry);
            }

            foreach (var pair in groups)
            {
                List<Entry> group = pair.Value;
                if (group.Count < 2)
                    continue;

                Entry owner = null;
                int validOwners = 0;

                foreach (Entry entry in group)
                {
                    if (entry.Owner == entry.Key)
                    {
                        owner = entry;
                        validOwners++;
                    }
                }

                if (validOwners != 1)
                {
                    result.Issues.Add(new IdentityValidationIssue(
                        IdentityIssueType.AmbiguousDuplicate,
                        IdentityIssueSeverity.Error,
                        IdentityRepairAction.ManualResolution,
                        owner != null ? owner.Identity : group[0].Identity,
                        pair.Key, "", "",
                        "Duplicate LiveLink ID '" + pair.Key + "' has no unique original owner."));
                    continue;
                }

                foreach (Entry entry in group)
                {
                    if (entry != owner)
                    {
                        result.Issues.Add(new IdentityValidationIssue(
                            IdentityIssueType.DuplicateObjectId,
                            IdentityIssueSeverity.Error,
                            IdentityRepairAction.ReassignIdentity,
                            entry.Identity, entry.Id, entry.Owner, entry.Key,
                            "Duplicate LiveLink ID '" + pair.Key + "' on " + entry.Identity.name + "."));
                    }
                }
            }
        }
    }
}
