using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChartPlayer))]
public class ChartPlayerEditor : Editor
{
    private SerializedProperty clipProperty;
    private SerializedProperty bpmTimelineProperty;
    private SerializedProperty bakedDataProperty;
    private SerializedProperty scanRootProperty;
    private SerializedProperty nodePrefabProperty;
    private SerializedProperty nodeAppearanceSetProperty;

    private void OnEnable()
    {
        clipProperty = serializedObject.FindProperty("clip");
        bpmTimelineProperty = serializedObject.FindProperty("bpmTimeline");
        bakedDataProperty = serializedObject.FindProperty("bakedData");
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
        EditorGUILayout.PropertyField(bakedDataProperty);

        BakedStageData data = p.BakedData;
        if (data != null && p.HasBakedTrack)
        {
            EditorGUILayout.HelpBox(
                $"已烘焙：{data.NodeCount} 个节点，" +
                $"{data.SampleCount} 个轨道样本。\n" +
                "运行阶段直接读取该资产，不会重新烘焙。",
                MessageType.Info);
        }
        else if (data == null && p.HasLegacyBakedData)
        {
            EditorGUILayout.HelpBox(
                $"旧版内联烘焙数据：{p.Nodes.Count} 个节点。\n" +
                "内联数组会被 Unity 拆成逐元素覆盖写进场景，" +
                "上万条采样会让场景打开极慢。建议迁移到独立资产。",
                MessageType.Warning);
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

        if (data == null && p.HasLegacyBakedData)
        {
            if (GUILayout.Button("迁移旧烘焙数据到独立资产"))
                p.MigrateLegacyBakedData();
        }

        EditorGUILayout.Space(6);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Bake Stage"))
                p.Bake();
        }
    }
}
