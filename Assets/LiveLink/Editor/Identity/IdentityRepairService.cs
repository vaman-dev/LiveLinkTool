using System;
using System.Collections.Generic;
using LiveLink;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveLink.Editor.Identity
{
    public static class IdentityRepairService
    {
        public static bool AdoptIdentity(LiveLinkIdentity identity)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.ObjectId))
            {
                Debug.LogError("[LiveLink] Adopt Identity requires an existing LiveLink object ID.");
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(identity))
            {
                Debug.LogError("[LiveLink] Repair the scene instance, not the prefab asset.", identity);
                return false;
            }

            if (IdentityValidator.IsObjectIdUsedInBuildScenes(identity.ObjectId, identity))
            {
                Debug.LogError("[LiveLink] Cannot adopt identity because another known build-scene object uses the same ID: " + identity.ObjectId, identity);
                return false;
            }

            string key = GlobalObjectId.GetGlobalObjectIdSlow(identity).ToString();
            SetIdentity(identity, identity.ObjectId, key, "Adopt LiveLink Identity");
            return true;
        }

        public static bool ReassignIdentity(LiveLinkIdentity identity)
        {
            if (identity == null)
                return false;

            if (PrefabUtility.IsPartOfPrefabAsset(identity))
            {
                Debug.LogError("[LiveLink] Repair the scene instance, not the prefab asset.", identity);
                return false;
            }

            string id;
            do
            {
                id = Guid.NewGuid().ToString("N");
            }
            while (IdentityValidator.IsObjectIdUsedInBuildScenes(id, identity));

            string key = GlobalObjectId.GetGlobalObjectIdSlow(identity).ToString();
            SetIdentity(identity, id, key, "Reassign LiveLink Identity");
            return true;
        }

        private static void SetIdentity(
            LiveLinkIdentity identity,
            string objectId,
            string authoringKey,
            string undoName)
        {
            Undo.RecordObject(identity, undoName);

            var serialized = new SerializedObject(identity);
            serialized.Update();
            serialized.FindProperty("objectId").stringValue = objectId;
            serialized.FindProperty("authoringKey").stringValue = authoringKey;
            serialized.ApplyModifiedProperties();

            if (PrefabUtility.IsPartOfPrefabInstance(identity))
                PrefabUtility.RecordPrefabInstancePropertyModifications(identity);

            EditorSceneManager.MarkSceneDirty(identity.gameObject.scene);
        }
    }
}
