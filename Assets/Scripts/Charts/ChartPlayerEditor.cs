using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChartPlayer))]
public class ChartPlayerEditor : Editor
{
    private SerializedProperty chartProperty;
    private SerializedProperty scanRootProperty;
    private SerializedProperty nodePrefabProperty;
    private SerializedProperty autoBakeProperty;
    private SerializedProperty timelineNodeClickClipProperty;
    private SerializedProperty timelineNodeClickVolumeProperty;
    private SerializedProperty timelineNodeClickSourcesProperty;

    private ChartPlayer pendingAutoLoadPlayer;
    private ChartSO pendingAutoLoadChart;
    private bool autoLoadQueued;

    private void OnEnable()
    {
        chartProperty = serializedObject.FindProperty("chart");
        scanRootProperty = serializedObject.FindProperty("scanRoot");
        nodePrefabProperty = serializedObject.FindProperty("nodePrefab");
        autoBakeProperty = serializedObject.FindProperty("autoBake");
        timelineNodeClickClipProperty =
            serializedObject.FindProperty("timelineNodeClickClip");
        timelineNodeClickVolumeProperty =
            serializedObject.FindProperty("timelineNodeClickVolume");
        timelineNodeClickSourcesProperty =
            serializedObject.FindProperty("timelineNodeClickSources");
    }

    public override void OnInspectorGUI()
    {
        ChartPlayer p = (ChartPlayer)target;
        if (p == null) return;

        serializedObject.Update();

        ChartSO previousChart = (ChartSO)chartProperty.objectReferenceValue;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(chartProperty);
        EditorGUILayout.PropertyField(scanRootProperty);
        EditorGUILayout.PropertyField(nodePrefabProperty);
        EditorGUILayout.PropertyField(autoBakeProperty);
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(
            "Chart Timeline Preview",
            EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(timelineNodeClickClipProperty);
        EditorGUILayout.PropertyField(timelineNodeClickVolumeProperty);
        EditorGUILayout.PropertyField(
            timelineNodeClickSourcesProperty,
            new GUIContent(
                "Node Click Sources",
                "建议在独立音源对象上添加 8 个 AudioSource 并全部引用。数组留空时才使用本对象上的全部 AudioSource。"),
            true);
        bool inspectorChanged = EditorGUI.EndChangeCheck();

        ChartSO selectedChart = (ChartSO)chartProperty.objectReferenceValue;
        if (inspectorChanged && selectedChart != previousChart &&
            p.dirty && previousChart != null)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "切换谱面",
                BuildSwitchMessage(p, previousChart, selectedChart),
                "烘焙后切换",
                "取消",
                "直接切换");

            if (choice == 1)
            {
                selectedChart = previousChart;
                chartProperty.objectReferenceValue = previousChart;
            }
            else if (choice == 0)
            {
                // serializedObject 尚未应用，当前 ChartPlayer.chart 仍指向旧谱面。
                p.Bake();
            }
        }

        serializedObject.ApplyModifiedProperties();

        if (selectedChart != previousChart && selectedChart != null)
            QueueAutoLoad(p, selectedChart);

        EditorGUILayout.Space(6);

        if (p.dirty)
            EditorGUILayout.HelpBox("有未同步的谱面改动。点下方按钮烘焙，或进入 Play 时自动烘焙。", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("谱面已同步。", MessageType.Info);

        if (GUILayout.Button("Bake From Scene"))
            p.Bake();

        using (new EditorGUI.DisabledScope(p.chart == null || Application.isPlaying))
        {
            if (GUILayout.Button("Load From Chart") &&
                EditorUtility.DisplayDialog(
                    "Load From Chart",
                    BuildLoadConfirmation(p),
                    "Load",
                    "Cancel"))
            {
                p.LoadFromChart();
            }
        }
    }

    private void QueueAutoLoad(ChartPlayer player, ChartSO expectedChart)
    {
        pendingAutoLoadPlayer = player;
        pendingAutoLoadChart = expectedChart;
        if (autoLoadQueued) return;

        autoLoadQueued = true;
        EditorApplication.delayCall += FlushAutoLoad;
    }

    private void FlushAutoLoad()
    {
        autoLoadQueued = false;

        ChartPlayer player = pendingAutoLoadPlayer;
        ChartSO expectedChart = pendingAutoLoadChart;
        pendingAutoLoadPlayer = null;
        pendingAutoLoadChart = null;

        if (player == null || Application.isPlaying ||
            player.chart != expectedChart)
        {
            return;
        }

        player.LoadFromChart();
        Repaint();
    }

    private static string BuildSwitchMessage(
        ChartPlayer player,
        ChartSO previousChart,
        ChartSO selectedChart)
    {
        string previousName = previousChart != null ? previousChart.name : "无";
        string selectedName = selectedChart != null ? selectedChart.name : "无";
        int existingCount = GetMarkerCount(player);
        int selectedCount = selectedChart != null && selectedChart.nodes != null
            ? selectedChart.nodes.Count
            : 0;

        return
            $"从“{previousName}”切换到“{selectedName}”。\n\n" +
            $"旧谱面有未烘焙改动。选择“烘焙后切换”会先保存旧谱面的 " +
            $"{existingCount} 个节点，再加载新谱面的 {selectedCount} 个节点。" +
            "选择“直接切换”会丢弃当前场景中的未烘焙改动。";
    }

    private static int GetMarkerCount(ChartPlayer player)
    {
        Transform root = player.scanRoot != null
            ? player.scanRoot
            : player.transform;
        return root.GetComponentsInChildren<NodeMarker>(true).Length;
    }

    private static string BuildLoadConfirmation(ChartPlayer player)
    {
        int existingCount = GetMarkerCount(player);
        int chartNodeCount = player.chart != null ? player.chart.nodes.Count : 0;

        return existingCount > 0
            ? $"将删除 scanRoot 下现有 {existingCount} 个节点，并从 ChartSO 重建 {chartNodeCount} 个节点。"
            : $"将从 ChartSO 重建 {chartNodeCount} 个节点。";
    }
}
