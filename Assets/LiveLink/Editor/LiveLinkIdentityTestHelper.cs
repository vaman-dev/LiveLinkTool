
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LiveLink;

public static class LiveLinkIdentityTestHelper
{
    private static LiveLinkIdentity GetSelected()
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogError("Select a GameObject first.");
            return null;
        }

        var identity = Selection.activeGameObject
            .GetComponent<LiveLinkIdentity>();

        if (identity == null)
            Debug.LogError(
                "Selected object has no LiveLinkIdentity.");

        return identity;
    }

    private static void SetField(
        LiveLinkIdentity identity,
        string fieldName,
        string value)
    {
        if (identity == null)
            return;

        Undo.RecordObject(identity, "LiveLink Identity Test");

        var serialized = new SerializedObject(identity);
        serialized.Update();

        var property = serialized.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError("Missing field: " + fieldName);
            return;
        }

        property.stringValue = value;
        serialized.ApplyModifiedProperties();

        if (PrefabUtility.IsPartOfPrefabInstance(identity))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(
                identity);
        }

        EditorUtility.SetDirty(identity);

        if (identity.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(
                identity.gameObject.scene);
        }

        Debug.Log(
            $"[LiveLink Test] {identity.name}.{fieldName} = {value}");
    }

    [MenuItem("Tools/LiveLink Tests/Clear Object ID")]
    public static void ClearId()
    {
        SetField(GetSelected(), "objectId", "");
    }

    [MenuItem("Tools/LiveLink Tests/Clear Authoring Key")]
    public static void ClearOwner()
    {
        SetField(GetSelected(), "authoringKey", "");
    }

    [MenuItem("Tools/LiveLink Tests/Corrupt Authoring Key")]
    public static void CorruptOwner()
    {
        SetField(
            GetSelected(),
            "authoringKey",
            "INVALID_TEST_OWNER");
    }

    [MenuItem("Tools/LiveLink Tests/Copy ID From First To Second")]
    public static void DuplicateId()
    {
        var selected = Selection.gameObjects;

        if (selected.Length != 2)
        {
            Debug.LogError("Select exactly two GameObjects.");
            return;
        }

        // Explicitly use the active object as the source.
        var source = Selection.activeGameObject
            .GetComponent<LiveLinkIdentity>();

        var targetObject =
            selected[0] == Selection.activeGameObject
                ? selected[1]
                : selected[0];

        var target = targetObject
            .GetComponent<LiveLinkIdentity>();

        if (source == null || target == null)
        {
            Debug.LogError(
                "Both objects need LiveLinkIdentity.");
            return;
        }

        SetField(target, "objectId", source.ObjectId);

        Debug.LogWarning(
            "[LiveLink Test] Duplicate ID created. " +
            "Run validation, then restore or repair.");
    }
}
