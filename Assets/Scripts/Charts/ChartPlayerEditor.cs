using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChartPlayer))]
public class ChartPlayerEditor : Editor
{
    private SerializedProperty stageProperty;
    private SerializedProperty surfaceTilemapProperty;
    private SerializedProperty scanRootProperty;
    private SerializedProperty nodePrefabProperty;
    private SerializedProperty autoBakeProperty;
    private SerializedProperty timelineNodeClickClipProperty;
    private SerializedProperty timelineNodeClickSourcesProperty;

    private ChartPlayer pendingAutoLoadPlayer;
    private StageDefinitionSO pendingAutoLoadStage;
    private bool autoLoadQueued;

    private void OnEnable()
    {
        stageProperty = serializedObject.FindProperty("stage");
        surfaceTilemapProperty =
            serializedObject.FindProperty("surfaceTilemap");
        scanRootProperty = serializedObject.FindProperty("scanRoot");
        nodePrefabProperty = serializedObject.FindProperty("nodePrefab");
        autoBakeProperty = serializedObject.FindProperty("autoBake");
        timelineNodeClickClipProperty =
            serializedObject.FindProperty("timelineNodeClickClip");
        timelineNodeClickSourcesProperty =
            serializedObject.FindProperty("timelineNodeClickSources");
    }

    public override void OnInspectorGUI()
    {
        ChartPlayer p = (ChartPlayer)target;
        if (p == null) return;

        serializedObject.Update();

        StageDefinitionSO previousStage =
            (StageDefinitionSO)stageProperty.objectReferenceValue;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(stageProperty);
        EditorGUILayout.PropertyField(surfaceTilemapProperty);
        EditorGUILayout.PropertyField(scanRootProperty);
        EditorGUILayout.PropertyField(nodePrefabProperty);
        EditorGUILayout.PropertyField(autoBakeProperty);
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(
            "Chart Timeline Preview",
            EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(timelineNodeClickClipProperty);
        EditorGUILayout.PropertyField(
            timelineNodeClickSourcesProperty,
            new GUIContent(
                "Node Click Sources",
                "建议在独立音源对象上添加 8 个 AudioSource 并全部引用。数组留空时才使用本对象上的全部 AudioSource。"),
            true);
        bool inspectorChanged = EditorGUI.EndChangeCheck();

        StageDefinitionSO selectedStage =
            (StageDefinitionSO)stageProperty.objectReferenceValue;
        if (inspectorChanged &&
            selectedStage != previousStage &&
            p.dirty &&
            previousStage != null)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "切换关卡",
                BuildSwitchMessage(p, previousStage, selectedStage),
                "烘焙后切换",
                "取消",
                "直接切换");

            if (choice == 1)
            {
                selectedStage = previousStage;
                stageProperty.objectReferenceValue = previousStage;
            }
            else if (choice == 0)
            {
                // serializedObject 尚未应用，当前 ChartPlayer.Stage 仍指向旧关卡。
                p.Bake();
            }
        }

        serializedObject.ApplyModifiedProperties();

        if (selectedStage != previousStage && selectedStage != null)
            QueueAutoLoad(p, selectedStage);

        if (p.Stage == null)
        {
            EditorGUILayout.HelpBox(
                "ChartPlayer 尚未绑定 StageDefinition。",
                MessageType.Error);
        }
        else if (!p.Stage.IsComplete)
        {
            EditorGUILayout.HelpBox(
                "当前 Stage 缺少 Chart 或 Track。",
                MessageType.Error);
        }
        else if (p.SurfaceTilemap == null)
        {
            EditorGUILayout.HelpBox(
                "ChartPlayer 尚未绑定铺面 Tilemap。",
                MessageType.Error);
        }
        else if (p.Track == null ||
                 !p.Track.HasSamples ||
                 !p.Track.IsComplete)
        {
            EditorGUILayout.HelpBox(
                "ChartTrack 尚未烘焙或已失效，进入 Play 前会自动烘焙。",
                MessageType.Warning);
        }

        EditorGUILayout.Space(6);

        if (p.dirty)
            EditorGUILayout.HelpBox("有未同步的谱面改动。点下方按钮烘焙，或进入 Play 时自动烘焙。", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("谱面已同步。", MessageType.Info);

        if (GUILayout.Button("Bake Chart + Track"))
            p.Bake();

        using (new EditorGUI.DisabledScope(p.Chart == null || Application.isPlaying))
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

    private void QueueAutoLoad(
        ChartPlayer player,
        StageDefinitionSO expectedStage)
    {
        pendingAutoLoadPlayer = player;
        pendingAutoLoadStage = expectedStage;
        if (autoLoadQueued) return;

        autoLoadQueued = true;
        EditorApplication.delayCall += FlushAutoLoad;
    }

    private void FlushAutoLoad()
    {
        autoLoadQueued = false;

        ChartPlayer player = pendingAutoLoadPlayer;
        StageDefinitionSO expectedStage = pendingAutoLoadStage;
        pendingAutoLoadPlayer = null;
        pendingAutoLoadStage = null;

        if (player == null || Application.isPlaying ||
            player.Stage != expectedStage)
        {
            return;
        }

        player.LoadFromChart();
        Repaint();
    }

    private static string BuildSwitchMessage(
        ChartPlayer player,
        StageDefinitionSO previousStage,
        StageDefinitionSO selectedStage)
    {
        string previousName = previousStage != null
            ? previousStage.name
            : "无";
        string selectedName = selectedStage != null
            ? selectedStage.name
            : "无";
        int existingCount = GetMarkerCount(player);
        int selectedCount =
            selectedStage != null &&
            selectedStage.Chart != null &&
            selectedStage.Chart.nodes != null
            ? selectedStage.Chart.nodes.Count
            : 0;

        return
            $"从“{previousName}”切换到“{selectedName}”。\n\n" +
            $"旧关卡有未烘焙改动。选择“烘焙后切换”会先保存旧关卡的 " +
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
        int chartNodeCount = player.Chart != null
            ? player.Chart.nodes.Count
            : 0;

        return existingCount > 0
            ? $"将删除 scanRoot 下现有 {existingCount} 个节点，并从 ChartSO 重建 {chartNodeCount} 个节点。"
            : $"将从 ChartSO 重建 {chartNodeCount} 个节点。";
    }
}
