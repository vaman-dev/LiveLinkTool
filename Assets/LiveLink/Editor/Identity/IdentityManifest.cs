using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using LiveLink;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveLink.Editor.Identity
{
    public static class IdentityManifest
    {
        public static string Compute(IEnumerable<string> scenePaths)
        {
            var records = new List<string>();

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
                        continue;

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (LiveLinkIdentity identity in root.GetComponentsInChildren<LiveLinkIdentity>(true))
                        {
                            if (identity == null || PrefabUtility.IsPartOfPrefabAsset(identity))
                                continue;

                            string key = GlobalObjectId.GetGlobalObjectIdSlow(identity).ToString();
                            records.Add(scene.path + "|" + key + "|" + identity.ObjectId);
                        }
                    }
                }
                finally
                {
                    if (openedHere && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }

            records.Sort(StringComparer.Ordinal);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(string.Join("\n", records));
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
