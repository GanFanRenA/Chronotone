using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ChartPlayer : MonoBehaviour
{
    public ChartSO chart;
    public Transform scanRoot;
    [Tooltip("留空时使用 Assets/Prefabs/Node.prefab。")]
    public NodeMarker nodePrefab;
    [Tooltip("按 NodeMode 配置节点与时间轴颜色；留空时使用 Resources/NodeAppearance/NodeAppearanceSet。")]
    public NodeAppearanceSet nodeAppearanceSet;
    public bool autoBake = false;   // 想要旧行为就勾上

    [System.NonSerialized] private bool updatingPositions;

    public bool IsUpdatingPositions => updatingPositions;

    private void Awake()
    {
        RefreshChartPositions();
        RefreshNodeAppearances();
    }

    public NodeAppearanceSet ResolveNodeAppearanceSet() =>
        nodeAppearanceSet != null
            ? nodeAppearanceSet
            : NodeAppearanceSet.LoadDefault();

    public void RefreshNodeAppearances()
    {
        NodeAppearanceSet appearanceSet = ResolveNodeAppearanceSet();
        if (appearanceSet == null)
            return;

        Transform root = scanRoot != null ? scanRoot : transform;
        foreach (NodeMarker marker in
                 root.GetComponentsInChildren<NodeMarker>(false))
        {
            if (marker != null)
                marker.ApplyAppearance(appearanceSet);
        }
    }

    public void RefreshChartPositions()
    {
        if (chart == null) return;
        Transform root = scanRoot != null ? scanRoot : transform;
        chart.UpdatePos(root.position.x);
    }

    public void UpdatePos(float fromBeat = 0f)
    {
        if (chart == null) return;

        Transform root = scanRoot != null ? scanRoot : transform;
        NodeMarker[] markers = root.GetComponentsInChildren<NodeMarker>(false);
        System.Array.Sort(markers, (a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0 ? byBeat : a.order.CompareTo(b.order);
        });
        if (markers.Length == 0) return;

        BpmTimeline timeline = chart.bpmTimeline;
        timeline?.RebuildDistanceCache();

        int firstIndex = 0;
        while (firstIndex < markers.Length &&
               markers[firstIndex].beat < fromBeat)
        {
            firstIndex++;
        }
        if (firstIndex >= markers.Length) return;

        float previousBeat = 0f;
        float previousX = root.position.x;
        if (firstIndex > 0)
        {
            previousBeat = Mathf.Max(0f, markers[firstIndex - 1].beat);
            previousX = markers[firstIndex - 1].transform.position.x;
        }

        updatingPositions = true;
        try
        {
            for (int i = firstIndex; i < markers.Length; i++)
            {
                NodeMarker marker = markers[i];
                if (marker == null) continue;

                marker.index = i;
                float beat = Mathf.Max(0f, marker.beat);
                float deltaX = timeline != null
                    ? timeline.DistanceBetweenBeats(previousBeat, beat)
                    : (beat - previousBeat) * 60f / 120f * 5f;
                float x = previousX + deltaX;

                bool changed = marker.UpdatePos(x, deltaX);
#if UNITY_EDITOR
                if (changed)
                {
                    EditorUtility.SetDirty(marker);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(marker.transform);
                }
#endif

                previousBeat = beat;
                previousX = x;
            }
        }
        finally
        {
            updatingPositions = false;
        }
    }

#if UNITY_EDITOR
    private const string DefaultNodePrefabPath = "Assets/Prefabs/Node.prefab";
    private const string EditorPlayStartBeatPrefix =
        "Chronotone.ChartTimeline.StartBeat.";

    [System.NonSerialized] public bool dirty;
    [System.NonSerialized] private bool updatePosQueued;
    [System.NonSerialized] private float queuedUpdateBeat;
    [System.NonSerialized] private int positionUpdateBatchDepth;

    public static void SetEditorPlayStartBeat(
        ChartPlayer player,
        float startBeat)
    {
        if (player == null)
            return;

        SessionState.SetFloat(
            BuildEditorPlayStartBeatKey(player),
            Mathf.Max(0f, startBeat));
    }

    public static bool TryConsumeEditorPlayStartBeat(
        ChartPlayer player,
        out float startBeat)
    {
        startBeat = 0f;
        if (player == null)
            return false;

        string key = BuildEditorPlayStartBeatKey(player);
        float storedBeat = SessionState.GetFloat(key, float.NaN);
        SessionState.EraseFloat(key);
        if (float.IsNaN(storedBeat))
            return false;

        startBeat = Mathf.Max(0f, storedBeat);
        return true;
    }

    private static string BuildEditorPlayStartBeatKey(ChartPlayer player)
    {
        string chartPath = player.chart != null
            ? AssetDatabase.GetAssetPath(player.chart)
            : string.Empty;
        return EditorPlayStartBeatPrefix +
            System.Uri.EscapeDataString(chartPath);
    }

    [InitializeOnLoadMethod]
    private static void HookPlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall -= RefreshAllPositions;
        EditorApplication.delayCall += RefreshAllPositions;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        foreach (var p in Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None))
            if (p.dirty) p.Bake();
    }

    private static void RefreshAllPositions()
    {
        if (Application.isPlaying) return;

        foreach (ChartPlayer player in
                 Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None))
        {
            if (player != null && player.chart != null)
                player.UpdatePos();
        }
    }

    // 由 Marker 调用：只标记，不写盘
    public void MarkDirty()
    {
        if (dirty) return;
        dirty = true;
        EditorUtility.SetDirty(this);       // 刷新 Inspector 状态
        if (autoBake) QueueBake();
    }

    // 由 autoBake 触发：合并同帧多次调用
    private void QueueBake()
    {
        EditorApplication.delayCall -= Bake;
        EditorApplication.delayCall += Bake;
    }

    public void QueueUpdatePos(float fromBeat = 0f)
    {
        if (this == null) return;

        fromBeat = Mathf.Max(0f, fromBeat);
        if (updatePosQueued)
            queuedUpdateBeat = Mathf.Min(queuedUpdateBeat, fromBeat);
        else
        {
            queuedUpdateBeat = fromBeat;
            updatePosQueued = true;
        }

        if (positionUpdateBatchDepth > 0) return;

        EditorApplication.delayCall -= FlushQueuedUpdatePos;
        EditorApplication.delayCall += FlushQueuedUpdatePos;
    }

    public void BeginPositionUpdateBatch()
    {
        positionUpdateBatchDepth++;
    }

    public void EndPositionUpdateBatch(float fallbackFromBeat)
    {
        positionUpdateBatchDepth = Mathf.Max(0, positionUpdateBatchDepth - 1);
        if (positionUpdateBatchDepth > 0) return;

        if (updatePosQueued)
            fallbackFromBeat = Mathf.Min(fallbackFromBeat, queuedUpdateBeat);
        QueueUpdatePos(fallbackFromBeat);
    }

    private void FlushQueuedUpdatePos()
    {
        if (this == null || !updatePosQueued) return;

        float fromBeat = queuedUpdateBeat;
        updatePosQueued = false;
        UpdatePos(fromBeat);
    }

    public static void RequestPositionUpdates(ChartSO changedChart)
    {
        if (changedChart == null) return;

        foreach (ChartPlayer player in
                 Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None))
        {
            if (player != null && player.chart == changedChart)
                player.QueueUpdatePos();
        }
    }

    [ContextMenu("Load From Chart")]
    public void LoadFromChart()
    {
        if (this == null || chart == null || Application.isPlaying) return;

        if (nodePrefab == null)
            nodePrefab = AssetDatabase.LoadAssetAtPath<NodeMarker>(DefaultNodePrefabPath);
        if (nodePrefab == null)
        {
            Debug.LogError(
                $"ChartPlayer 无法加载 Node Prefab：{DefaultNodePrefabPath}", this);
            return;
        }

        Transform root = scanRoot != null ? scanRoot : transform;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Load Chart Nodes");

        NodeMarker[] existingMarkers = root.GetComponentsInChildren<NodeMarker>(true);
        foreach (NodeMarker marker in existingMarkers)
        {
            if (marker != null)
                Undo.DestroyObjectImmediate(marker.gameObject);
        }

        for (int i = 0; i < chart.nodes.Count; i++)
        {
            NodeData data = chart.nodes[i];
            if (data == null) continue;

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(nodePrefab.gameObject, root);
            if (go == null)
            {
                Debug.LogError($"ChartPlayer 无法实例化 Node Prefab：{nodePrefab.name}", this);
                break;
            }

            Undo.RegisterCreatedObjectUndo(go, "Create Chart Node");

            NodeMarker marker = go.GetComponent<NodeMarker>();
            marker.beat = data.beat;
            marker.order = data.order;
            marker.mode = data.mode;
            marker.index = i;
            marker.name = $"Node_{i}:{data.beat:0.##}_{data.order}";
            marker.transform.position = new Vector3(
                root.position.x, data.position.y, root.position.z);
            marker.ApplyAppearance(ResolveNodeAppearanceSet());

            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker.transform);
            EditorUtility.SetDirty(marker);
        }

        UpdatePos();
        Undo.CollapseUndoOperations(undoGroup);

        dirty = false;
        EditorUtility.SetDirty(this);
    }

    [ContextMenu("Bake From Scene")]
    public void Bake()
    {
        if (this == null || chart == null || Application.isPlaying) return;

        UpdatePos();

        Transform root = scanRoot != null ? scanRoot : transform;
        NodeMarker[] markers = root.GetComponentsInChildren<NodeMarker>(false);

        System.Array.Sort(markers, (a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0 ? byBeat : a.order.CompareTo(b.order);
        });

        var result = new List<NodeData>(markers.Length);
        for (int i = 0; i < markers.Length; i++)
        {
            NodeMarker m = markers[i];
            m.index = i;

            string newName = $"Node_{i}:{m.beat:0.##}_{m.order}";
            if (m.name != newName) m.name = newName;

            m.transform.SetSiblingIndex(i);

            result.Add(new NodeData
            {
                beat = m.beat,
                order = m.order,
                mode = m.mode,
                position = m.transform.position,
                deltaX = m.deltaX
            });
        }

        chart.nodes = result;
        chart.UpdatePos(root.position.x);
        EditorUtility.SetDirty(chart);
        AssetDatabase.SaveAssets();

        dirty = false;
        EditorUtility.SetDirty(this);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            RefreshNodeAppearances();
    }
#endif
}
