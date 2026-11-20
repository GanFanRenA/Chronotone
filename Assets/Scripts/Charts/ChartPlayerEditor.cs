using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChartPlayer))]
public class ChartPlayerEditor : Editor
{
    private SerializedProperty clipProperty;
    private SerializedProperty bpmTimelineProperty;
    private SerializedProperty scanRootProperty;
    private SerializedProperty nodePrefabProperty;
    private SerializedProperty nodeAppearanceSetProperty;

    private void OnEnable()
    {
        clipProperty = serializedObject.FindProperty("clip");
        bpmTimelineProperty = serializedObject.FindProperty("bpmTimeline");
        scanRootProperty = serializedObject.FindProperty("scanRoot");
        nodePrefabProperty = serializedObject.FindProperty("nodePrefab");
        nodeAppearanceSetProperty =
            serializedObject.FindProperty("nodeAppearanceSet");
    }

    public override void OnInspectorGUI()
    {
        ChartPlayer p = (ChartPlayer)target;
        if (p == null)
            return;

        serializedObject.Update();

        EditorGUILayout.LabelField("Stage（关卡元数据）", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(clipProperty);
        EditorGUILayout.PropertyField(bpmTimelineProperty, true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(
            "Baked（烘焙产物，运行只读）",
            EditorStyles.boldLabel);
        if (p.HasBakedTrack)
        {
            int nodeCount = p.Nodes != null ? p.Nodes.Count : 0;
            EditorGUILayout.HelpBox(
                $"已烘焙：{nodeCount} 个节点。运行阶段直接读取缓存，不会重新烘焙。",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "尚未烘焙。进入 Play 前请点击下方 “Bake Stage”。",
                MessageType.Warning);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Scan & Appearance", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(scanRootProperty);
        EditorGUILayout.PropertyField(nodePrefabProperty);
        EditorGUILayout.PropertyField(nodeAppearanceSetProperty);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Bake Stage"))
                p.Bake();
        }
    }
}
