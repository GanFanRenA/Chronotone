using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StageDefinitionSO))]
public sealed class StageDefinitionSOEditor : Editor
{
    private SerializedProperty chartProperty;
    private SerializedProperty trackProperty;

    private void OnEnable()
    {
        chartProperty = serializedObject.FindProperty("chart");
        trackProperty = serializedObject.FindProperty("track");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(chartProperty);
        EditorGUILayout.PropertyField(trackProperty);
        serializedObject.ApplyModifiedProperties();

        StageDefinitionSO stage = (StageDefinitionSO)target;
        if (!stage.IsComplete)
        {
            EditorGUILayout.HelpBox(
                "Stage 必须同时绑定 Chart 和 Track；铺面由场景中的 ChartPlayer 绑定。",
                MessageType.Warning);
        }
    }
}
