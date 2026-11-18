using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ChartPlayer : MonoBehaviour
{
    [SerializeField] private StageDefinitionSO stage;
    [Tooltip("当前关卡使用的铺面；多个铺面可以位于同一场景并按关卡切换。")]
    [SerializeField] private Tilemap surfaceTilemap;
    public Transform scanRoot;
    [Tooltip("留空时使用 Assets/Prefabs/Node.prefab。")]
    public NodeMarker nodePrefab;
    [Tooltip("按动作类型配置节点与时间轴颜色；留空时使用 Resources/NodeAppearance/NodeAppearanceSet。")]
    public NodeAppearanceSet nodeAppearanceSet;
    public bool autoBake = false;   // 想要旧行为就勾上

    [Header("Chart Timeline Preview")]
    [Tooltip("Chart Timeline 播放经过节点时使用的卡点音效。")]
    public AudioClip timelineNodeClickClip;
    [Tooltip("用于预调度卡点音效的 AudioSource 池。留空时自动使用本对象上的所有 AudioSource。")]
    public AudioSource[] timelineNodeClickSources =
        System.Array.Empty<AudioSource>();

    [System.NonSerialized] private bool updatingPositions;
    [System.NonSerialized] private GameSettingsSO gameSettings;
    [SerializeField, HideInInspector] private int nodeDataVersion;

    private const int CurrentNodeDataVersion = 1;

    public StageDefinitionSO Stage => stage;
    public Tilemap SurfaceTilemap => surfaceTilemap;
    public ChartSO Chart => stage != null ? stage.Chart : null;
    public ChartTrackSO Track => stage != null ? stage.Track : null;
    public bool IsUpdatingPositions => updatingPositions;
    public float TimelineNodeClickVolume =>
        GameSettings.TimelineNodeClickVolume;

    public GameSettingsSO GameSettings
    {
        get
        {
            if (gameSettings == null)
                gameSettings = GameSettingsSO.LoadDefault();
            return gameSettings;
        }
    }

    public Player ResolvePlayer()
    {
        Player[] players = Object.FindObjectsByType<Player>(
            FindObjectsSortMode.None);
        foreach (Player player in players)
        {
            if (player != null && player.chartPlayer == this)
                return player;
        }

        return players.Length == 1 ? players[0] : null;
    }

    private void Awake()
    {
        if (stage == null || !stage.IsComplete)
        {
            Debug.LogError(
                "ChartPlayer 必须绑定包含 Chart 和 Track 的 StageDefinition",
                this);
            enabled = false;
            return;
        }

        if (surfaceTilemap == null)
        {
            Debug.LogError(
                $"Stage '{stage.name}' 没有绑定铺面 Tilemap",
                this);
            enabled = false;
            return;
        }

        EnsureNodeDataMigrated();
        RefreshChartPositions();
        RefreshNodeAppearances();
    }

    /// <summary>
    /// 把旧 NodeType/NodeTrigger 数据一次性转换成动作 + 重力 + 自动 + 显示。
    /// 版本号放在 ChartPlayer 上，避免 Prefab 实例继承迁移标记导致漏转。
    /// </summary>
    public void EnsureNodeDataMigrated()
    {
        ChartSO chart = Chart;
        if (chart != null)
            chart.EnsureMigrated();

        if (nodeDataVersion >= CurrentNodeDataVersion)
            return;

        Transform root = scanRoot != null ? scanRoot : transform;
        NodeAppearanceSet appearanceSet = ResolveNodeAppearanceSet();
        foreach (NodeMarker marker in
                 root.GetComponentsInChildren<NodeMarker>(true))
        {
            if (marker == null)
                continue;

            // 旧 Jump + Manual 映射后正好等于新字段默认值；
            // 跳过可以避免覆盖已经在用新字段的新节点。
            if (marker.type == NodeType.Jump &&
                marker.trigger == NodeTrigger.Manual)
            {
                continue;
            }

            NodeData.MigrateLegacy(
                marker.type,
                marker.trigger,
                out NodeAction action,
                out NodeGravity gravity,
                out bool auto);
            marker.action = action;
            marker.gravity = gravity;
            marker.auto = auto;
            marker.visible = true;
            marker.ApplyAppearance(appearanceSet);
            marker.ApplyVisibility();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(marker);
                PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            }
#endif
        }

        nodeDataVersion = CurrentNodeDataVersion;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
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
        ChartSO chart = Chart;
        if (chart == null) return;
        Transform root = scanRoot != null ? scanRoot : transform;
        chart.UpdatePos(root.position.x);
    }

    public void UpdatePos(float fromBeat = 0f)
    {
        ChartSO chart = Chart;
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
                    : (beat - previousBeat) * 5f;
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
        string chartPath = player.Chart != null
            ? AssetDatabase.GetAssetPath(player.Chart)
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
            if (p.NeedsTrackBake()) p.Bake();
    }

    private bool NeedsTrackBake()
    {
        ChartSO chart = Chart;
        ChartTrackSO track = Track;
        Player player = ResolvePlayer();
        if (chart == null ||
            track == null ||
            !track.HasSamples ||
            !track.IsComplete ||
            !track.HasCurrentRouteVersion ||
            player == null)
        {
            return true;
        }

        PerfectRouteSample first = track.Samples[0];
        return dirty ||
            System.Math.Abs(
                first.Time + chart.GlobalOffsetSeconds) > 0.00001d ||
            Vector2.SqrMagnitude(
                first.Position - (Vector2)player.transform.position) >
            0.000001f;
    }

    private static void RefreshAllPositions()
    {
        if (Application.isPlaying) return;

        foreach (ChartPlayer player in
                 Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None))
        {
            if (player != null && player.Chart != null)
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

    public static void RequestChartChanged(ChartSO changedChart)
    {
        if (changedChart == null) return;

        foreach (ChartPlayer player in
                 Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None))
        {
            if (player != null && player.Chart == changedChart)
            {
                player.MarkDirty();
                player.QueueUpdatePos();
            }
        }
    }

    [ContextMenu("Load From Chart")]
    public void LoadFromChart()
    {
        ChartSO chart = Chart;
        if (this == null || chart == null || Application.isPlaying) return;

        chart.EnsureMigrated();
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
            marker.action = data.action;
            marker.gravity = data.gravity;
            marker.auto = data.auto;
            marker.visible = data.visible;
            marker.index = i;
            marker.name = $"Node_{i}:{data.beat:0.##}_{data.order}";
            marker.transform.position = new Vector3(
                root.position.x, data.position.y, root.position.z);
            marker.ApplyAppearance(ResolveNodeAppearanceSet());
            marker.ApplyVisibility();

            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker.transform);
            EditorUtility.SetDirty(marker);
        }

        UpdatePos();
        Undo.CollapseUndoOperations(undoGroup);

        nodeDataVersion = CurrentNodeDataVersion;
        dirty = false;
        EditorUtility.SetDirty(this);
    }

    [ContextMenu("Bake Chart + Track")]
    public void Bake()
    {
        ChartSO chart = Chart;
        ChartTrackSO track = Track;
        if (this == null ||
            chart == null ||
            track == null ||
            Application.isPlaying)
        {
            return;
        }

        EnsureNodeDataMigrated();
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

            var node = new NodeData
            {
                beat = m.beat,
                order = m.order,
                action = m.action,
                gravity = m.gravity,
                auto = m.auto,
                visible = m.visible,
                position = m.transform.position,
                deltaX = m.deltaX
            };
            node.MarkMigrated();
            result.Add(node);
        }

        if (!TryBuildTrack(result, out PerfectRoute route))
            return;

        chart.nodes = result;
        chart.UpdatePos(root.position.x);
        track.Set(route);
        EditorUtility.SetDirty(chart);
        EditorUtility.SetDirty(track);
        AssetDatabase.SaveAssets();

        dirty = false;
        EditorUtility.SetDirty(this);
    }

    private bool TryBuildTrack(
        IReadOnlyList<NodeData> nodes,
        out PerfectRoute route)
    {
        route = null;

        ChartSO chart = Chart;
        Player scenePlayer = ResolvePlayer();
        PhysicsActionMode physicsMode = scenePlayer != null
            ? scenePlayer.GetComponent<PhysicsActionMode>()
            : null;
        BoxCollider2D bodyCollider = scenePlayer != null
            ? scenePlayer.GetComponent<BoxCollider2D>()
            : null;
        Rigidbody2D body = scenePlayer != null
            ? scenePlayer.GetComponent<Rigidbody2D>()
            : null;

        if (chart == null ||
            chart.bpmTimeline == null ||
            scenePlayer == null ||
            physicsMode == null ||
            bodyCollider == null)
        {
            Debug.LogError(
                "烘焙 ChartTrack 需要 Chart、Player、PhysicsActionMode 和 BoxCollider2D",
                this);
            return false;
        }

        double startSongTime = -chart.GlobalOffsetSeconds;
        double endSongTime = startSongTime;
        for (int i = 0; i < nodes.Count; i++)
        {
            NodeData node = nodes[i];
            if (node == null)
                continue;

            endSongTime = System.Math.Max(
                endSongTime,
                chart.bpmTimeline.BeatToSeconds(node.beat));
        }

        if (chart.clip != null)
        {
            endSongTime = System.Math.Max(
                endSongTime,
                chart.clip.length - chart.GlobalOffsetSeconds);
        }

        endSongTime += Mathf.Max(1f, Time.fixedDeltaTime * 2f);

        Vector2 startPosition = scenePlayer.transform.position;
        var config = new PerfectRouteConfig(
            physicsMode,
            scenePlayer.transform.localEulerAngles.z);
        route = PerfectRouteBuilder.Build(
            startPosition,
            startSongTime,
            nodes,
            chart.bpmTimeline,
            config,
            bodyCollider,
            body,
            endSongTime,
            out PerfectRouteBuildFailure failure);

        if (route == null ||
            route.Count == 0 ||
            route.Blocked ||
            !route.IsComplete)
        {
            route = null;
            LogTrackBuildFailure(failure, nodes);
            return false;
        }

        PerfectRouteSample first = route.Samples[0];
        bool anchorMatches =
            System.Math.Abs(first.Time - startSongTime) <= 0.00001d &&
            Vector2.SqrMagnitude(first.Position - startPosition) <=
            0.000001f;
        if (!anchorMatches)
        {
            PerfectRouteSample actualFirst = route.Samples[0];
            route = null;
            Debug.LogError(
                "ChartTrack 首点必须等于角色起点和歌曲起始时刻。\n" +
                $"实际首点：时间 {actualFirst.Time:0.#####} 秒，" +
                $"位置 {actualFirst.Position}\n" +
                $"期望首点：时间 {startSongTime:0.#####} 秒，" +
                $"位置 {startPosition}\n" +
                "相关节点：无。这是起点/歌曲时间锚点问题，不是 Node 碰撞。",
                this);
            return false;
        }

        return true;
    }

    private void LogTrackBuildFailure(
        PerfectRouteBuildFailure failure,
        IReadOnlyList<NodeData> nodes)
    {
        if (!failure.Failed)
        {
            Debug.LogError(
                "ChartTrack 烘焙失败：轨道构建没有返回可用样本。",
                this);
            return;
        }

        string previousNode = DescribeNode(
            nodes,
            failure.PreviousNodeIndex);
        string targetNode = DescribeNode(
            nodes,
            failure.TargetNodeIndex);

        switch (failure.Reason)
        {
            case PerfectRouteBuildFailureReason.MissingBpmTimeline:
                Debug.LogError(
                    "ChartTrack 烘焙失败。\n" +
                    "原因：Chart 没有配置 BpmTimeline。\n" +
                    "相关节点：无，先检查 ChartSO 的 BPM 时间轴。",
                    this);
                return;

            case PerfectRouteBuildFailureReason.MissingBodyCollider:
                Debug.LogError(
                    "ChartTrack 烘焙失败。\n" +
                    "原因：Player 缺少 BoxCollider2D。\n" +
                    "相关节点：无，先检查 Player 的碰撞体。",
                    this);
                return;

            case PerfectRouteBuildFailureReason.Blocked:
                Debug.LogError(
                    "ChartTrack 烘焙失败。\n" +
                    $"相关节点：前往 {targetNode} 的路径，" +
                    $"上一个节点是 {previousNode}。\n" +
                    $"原因：角色在歌曲时间 {failure.SongTime:0.###} 秒、" +
                    $"位置 {failure.Position} 附近横向移动受阻。" +
                    DescribeBlockingCollider(failure) + "\n" +
                    $"角色状态：Grounded={failure.Grounded}，" +
                    $"角度={failure.Angle:0.##}°，" +
                    $"竖直速度={failure.VerticalSpeed:0.###}。\n" +
                    "处理建议：检查该节点前后的跳跃/重力模式，或把地面物体从 " +
                    "PhysicsActionMode 的 Blocking Layers 移到专用的 Wall 层。",
                    this);
                return;

            case PerfectRouteBuildFailureReason.SimulationIncomplete:
                Debug.LogError(
                    "ChartTrack 烘焙失败。\n" +
                    $"相关节点：前往 {targetNode} 的路径，" +
                    $"上一个节点是 {previousNode}。\n" +
                    $"原因：模拟在歌曲时间 {failure.SongTime:0.###} 秒仍未完成，" +
                    "达到了模拟步数上限。\n" +
                    $"最后位置：{failure.Position}。",
                    this);
                return;

            default:
                Debug.LogError(
                    "ChartTrack 烘焙失败，未识别的构建错误。",
                    this);
                return;
        }
    }

    private static string DescribeBlockingCollider(
        PerfectRouteBuildFailure failure)
    {
        if (string.IsNullOrEmpty(failure.BlockingObjectName))
            return "横向 BoxCast 检测到阻挡，但没有返回具体 Collider。";

        string layer = string.IsNullOrEmpty(failure.BlockingLayerName)
            ? "未命名层"
            : failure.BlockingLayerName;
        return
            $"阻挡物体为 '{failure.BlockingObjectName}'" +
            $"（Layer: {layer}），" +
            $"接触点 {failure.BlockingPoint}，" +
            $"法线 {failure.BlockingNormal}。";
    }

    private static string DescribeNode(
        IReadOnlyList<NodeData> nodes,
        int index)
    {
        if (nodes == null ||
            index < 0 ||
            index >= nodes.Count ||
            nodes[index] == null)
        {
            return "无";
        }

        NodeData node = nodes[index];
        return
            $"Node_{index}（Beat {node.beat:0.###}，" +
            $"动作 {node.action}，" +
            $"重力 {node.gravity}，" +
            $"自动 {(node.auto ? "是" : "否")}，" +
            $"显示 {(node.visible ? "是" : "否")}，" +
            $"位置 {node.position}）";
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            RefreshNodeAppearances();
    }
#endif
}
