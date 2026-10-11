using System;
using LiveLink;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveLink.Editor.Identity
{
    [InitializeOnLoad]
    public static class IdentityAutoInitializer
    {
        private static bool requested = true;
        private static double nextProcess;

        static IdentityAutoInitializer()
        {
            EditorApplication.hierarchyChanged += Request;
            Undo.undoRedoPerformed += Request;
            EditorSceneManager.sceneOpened += (scene, mode) => Request();
            EditorApplication.delayCall += Request;
        }

        private static void Request()
        {
            requested = true;
        }

        public static void Process()
        {
            if (!requested ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < nextProcess)
                return;

            requested = false;
            nextProcess = EditorApplication.timeSinceStartup + 0.2;

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);

                if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (LiveLinkIdentity identity in root.GetComponentsInChildren<LiveLinkIdentity>(true))
                    {
                        if (identity == null ||
                            PrefabUtility.IsPartOfPrefabAsset(identity) ||
                            !string.IsNullOrEmpty(identity.ObjectId) ||
                            !string.IsNullOrEmpty(identity.AuthoringKey))
                            continue;

                        Initialize(identity);
                    }
                }
            }
        }

        private static void Initialize(LiveLinkIdentity identity)
        {
            string id;
            do
            {
                id = Guid.NewGuid().ToString("N");
            }
            while (IsObjectIdUsed(id, identity));

            string key = GlobalObjectId.GetGlobalObjectIdSlow(identity).ToString();

            Undo.RecordObject(identity, "Initialize LiveLink Identity");

            var serialized = new SerializedObject(identity);
            serialized.Update();
            serialized.FindProperty("objectId").stringValue = id;
            serialized.FindProperty("authoringKey").stringValue = key;
            serialized.ApplyModifiedProperties();

            if (PrefabUtility.IsPartOfPrefabInstance(identity))
                PrefabUtility.RecordPrefabInstancePropertyModifications(identity);

            EditorSceneManager.MarkSceneDirty(identity.gameObject.scene);
            Debug.Log("[LiveLink] Initialized identity for " + identity.name + ".", identity);
        }

        private static bool IsObjectIdUsed(string id, LiveLinkIdentity excluded)
        {
            if (IdentityValidator.IsObjectIdUsedInBuildScenes(id, excluded))
                return true;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (LiveLinkIdentity candidate in root.GetComponentsInChildren<LiveLinkIdentity>(true))
                    {
                        if (candidate != null && candidate != excluded && candidate.ObjectId == id)
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
