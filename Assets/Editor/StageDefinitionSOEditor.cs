using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StageDefinitionSO))]
public sealed class StageDefinitionSOEditor : Editor
{
    private SerializedProperty scenePathProperty;
    private SerializedProperty chartProperty;
    private SerializedProperty trackProperty;

    private void OnEnable()
    {
        scenePathProperty = serializedObject.FindProperty("scenePath");
        chartProperty = serializedObject.FindProperty("chart");
        trackProperty = serializedObject.FindProperty("track");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
            scenePathProperty.stringValue);
        EditorGUI.BeginChangeCheck();
        scene = (SceneAsset)EditorGUILayout.ObjectField(
            "Scene",
            scene,
            typeof(SceneAsset),
            false);
        if (EditorGUI.EndChangeCheck())
        {
            scenePathProperty.stringValue = scene != null
                ? AssetDatabase.GetAssetPath(scene)
                : string.Empty;
        }

        EditorGUILayout.PropertyField(chartProperty);
        EditorGUILayout.PropertyField(trackProperty);
        serializedObject.ApplyModifiedProperties();

        StageDefinitionSO stage = (StageDefinitionSO)target;
        if (!stage.IsComplete)
        {
            EditorGUILayout.HelpBox(
                "Stage 必须同时绑定 Scene、Chart 和 Track。",
                MessageType.Warning);
        }
    }
}
