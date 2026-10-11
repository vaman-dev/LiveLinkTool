using LiveLink;
using LiveLink.Editor.Identity;
using UnityEditor;
using UnityEngine;

namespace LiveLink.Editor
{
    [CustomEditor(typeof(LiveLinkIdentity))]
    public sealed class LiveLinkIdentityEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var id = serializedObject.FindProperty("objectId");
            var owner = serializedObject.FindProperty("authoringKey");

            EditorGUILayout.LabelField("LiveLink Identity", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Runtime Object ID", id.stringValue);
                EditorGUILayout.TextField("Authoring Key", owner.stringValue);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Validate Scene IDs"))
            {
                LiveLinkIdManager.ValidateLoadedScenes();
                serializedObject.Update();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(id.stringValue)))
            {
                if (GUILayout.Button("Adopt Identity"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Adopt LiveLink Identity",
                        "This preserves the current Runtime Object ID and changes its authoring owner. Continue?",
                        "Adopt",
                        "Cancel"))
                    {
                        IdentityRepairService.AdoptIdentity((LiveLinkIdentity)target);
                        serializedObject.Update();
                    }
                }
            }

            if (GUILayout.Button("Reassign Identity"))
            {
                if (EditorUtility.DisplayDialog(
                    "Reassign LiveLink Identity",
                    "This generates a new Runtime Object ID. The development Player must be rebuilt before synchronization. Continue?",
                    "Reassign",
                    "Cancel"))
                {
                    IdentityRepairService.ReassignIdentity((LiveLinkIdentity)target);
                    serializedObject.Update();
                }
            }

            bool valid = !string.IsNullOrWhiteSpace(id.stringValue) &&
                         !string.IsNullOrWhiteSpace(owner.stringValue);

            EditorGUILayout.HelpBox(
                valid
                    ? "Object has authoring and runtime identity metadata."
                    : "Identity metadata is missing. Save the scene and run validation.",
                valid ? MessageType.Info : MessageType.Warning);

            EditorGUILayout.HelpBox(
                "Identity repairs are explicit, undoable, and require saving the scene. Rebuild the Player after changing IDs.",
                MessageType.None);
        }
    }
}
