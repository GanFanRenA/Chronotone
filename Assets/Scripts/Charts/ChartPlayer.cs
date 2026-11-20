using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class ChartPlayer : MonoBehaviour
{
    [Header("Stage")]
    [SerializeField] private AudioClip clip;
    [SerializeField] private BpmTimeline bpmTimeline = new BpmTimeline();

    [Header("Baked")]
    [SerializeField] private NodeData[] bakedNodes = System.Array.Empty<NodeData>();
    [SerializeField] private PerfectRouteSample[] bakedTrackSamples =
        System.Array.Empty<PerfectRouteSample>();
    [SerializeField] private bool bakedTrackBlocked;
    [SerializeField] private bool bakedTrackComplete;

    [Header("Scan & Appearance")]
    public Transform scanRoot;
    [Tooltip("留空时使用 Assets/Prefabs/Node.prefab。")]
    public NodeMarker nodePrefab;
    [Tooltip("按动作类型配置节点与时间轴颜色；留空时使用 Resources/NodeAppearance/NodeAppearanceSet。")]
    public NodeAppearanceSet nodeAppearanceSet;

    [Header("Visual Effects")]
    [Tooltip("视效事件列表，在 Chart Timeline 里编辑；运行时由 StageVfxDriver 触发。")]
    [SerializeField] private List<StageVfxEvent> vfxEvents =
        new List<StageVfxEvent>();

    [System.NonSerialized] private bool updatingPositions;
    [System.NonSerialized] private GameSettingsSO gameSettings;
    [SerializeField, HideInInspector] private int nodeDataVersion;

    private const int CurrentNodeDataVersion = 2;

    public AudioClip Clip => clip;
    public BpmTimeline BpmTimeline => bpmTimeline;

    public float GlobalOffsetSeconds => GameSettings.GlobalOffsetSeconds;

    public IReadOnlyList<NodeData> Nodes => bakedNodes;
    public IReadOnlyList<StageVfxEvent> VfxEvents => vfxEvents;
#if UNITY_EDITOR
    public List<StageVfxEvent> VfxEventsEditable => vfxEvents;
#endif
    public bool HasStageData => clip != null && bpmTimeline != null;
    public bool HasBakedTrack =>
        bakedTrackSamples != null &&
        bakedTrackSamples.Length > 0 &&
        bakedTrackComplete &&
        !bakedTrackBlocked;
    public bool IsUpdatingPositions => updatingPositions;

    public PerfectRoute CreateBakedRoute() =>
        HasBakedTrack
            ? new PerfectRoute(
                bakedTrackSamples,
                bakedTrackBlocked,
                bakedTrackComplete)
            : null;

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
        if (!HasStageData)
        {
            Debug.LogError(
                "ChartPlayer 未配置 AudioClip 或 BpmTimeline（关卡元数据）。",
                this);
            enabled = false;
            return;
        }

        if (!HasBakedTrack)
        {
            Debug.LogError(
                "关卡尚未烘焙：请先在编辑器里对 ChartPlayer 执行 " +
                "“Bake Stage”，再进入 Play。运行阶段不会自动重烘焙。",
                this);
            enabled = false;
            return;
        }

        EnsureNodeDataMigrated();
        RefreshNodeAppearances();
    }

    /// <summary>
    /// 把旧 NodeType/NodeTrigger 数据一次性转换成动作 + 重力 + 自动 + 显示，
    /// 再为旧节点补齐显式的力度值。版本号放在 ChartPlayer 上，避免 Prefab 实例继承迁移标记导致漏转。
    /// 现在只迁移场景里的 NodeMarker，不再迁移 ChartSO 资产。
    /// </summary>
    public void EnsureNodeDataMigrated()
    {
        if (nodeDataVersion >= CurrentNodeDataVersion)
            return;

        GameSettingsSO settings = GameSettings;
        Transform root = scanRoot != null ? scanRoot : transform;
        NodeAppearanceSet appearanceSet = ResolveNodeAppearanceSet();
        bool migrateLegacyAction = nodeDataVersion < 1;
        foreach (NodeMarker marker in
                 root.GetComponentsInChildren<NodeMarker>(true))
        {
            if (marker == null)
                continue;

            if (migrateLegacyAction &&
                !(marker.type == NodeType.Jump &&
                  marker.trigger == NodeTrigger.Manual))
            {
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
            }

            if (marker.strength <= 0f)
            {
                marker.strength = NodeData.GetDefaultStrength(
                    marker.action,
                    settings);
            }

            marker.ApplyAppearance(appearanceSet);
            marker.ApplyVisibility();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(marker);
                PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
                var scene = marker.gameObject.scene;
                if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                    EditorSceneManager.MarkSceneDirty(scene);
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
        UpdatePos();
    }

    /// <summary>
    /// 按 beat/order 排序场景 NodeMarker，并用 BpmTimeline 计算每个节点的 X 坐标。
    /// Y 坐标保留作者在场景里的摆放。烘焙前会调用，保证节点位置与拍点一致。
    /// </summary>
    public void UpdatePos(float fromBeat = 0f)
    {
        if (bpmTimeline == null)
            return;

        Transform root = scanRoot != null ? scanRoot : transform;
        NodeMarker[] markers = root.GetComponentsInChildren<NodeMarker>(false);
        System.Array.Sort(markers, (a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0 ? byBeat : a.order.CompareTo(b.order);
        });
        if (markers.Length == 0)
            return;

        bpmTimeline.RebuildDistanceCache();

        int firstIndex = 0;
        while (firstIndex < markers.Length &&
               markers[firstIndex].beat < fromBeat)
        {
            firstIndex++;
        }
        if (firstIndex >= markers.Length)
            return;

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
                if (marker == null)
                    continue;

                marker.index = i;
                float beat = Mathf.Max(0f, marker.beat);
                float deltaX = bpmTimeline.DistanceBetweenBeats(
                    previousBeat,
                    beat);
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

    /// <summary>
    /// 显式烘焙（编辑器内执行一次）：扫描场景 NodeMarker → 生成节点缓存 → 物理模拟
    /// 完美路径 → 把结果序列化写回本组件。运行阶段只反序列化，不重新模拟。
    /// </summary>
    [ContextMenu("Bake Stage")]
    public void Bake()
    {
        if (this == null || Application.isPlaying)
            return;

        if (bpmTimeline == null)
        {
            Debug.LogError("Bake 需要先配置 BpmTimeline", this);
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

        var nodes = new List<NodeData>(markers.Length);
        for (int i = 0; i < markers.Length; i++)
        {
            NodeMarker m = markers[i];
            m.index = i;

            string newName = $"Node_{i}:{m.beat:0.##}_{m.order}";
            if (m.name != newName)
                m.name = newName;

            m.transform.SetSiblingIndex(i);

            var node = new NodeData
            {
                beat = m.beat,
                order = m.order,
                action = m.action,
                gravity = m.gravity,
                auto = m.auto,
                visible = m.visible,
                strength = m.strength,
                position = m.transform.position,
                deltaX = m.deltaX
            };
            node.MarkMigrated();
            nodes.Add(node);
        }

        if (!TrySimulateRoute(nodes, out PerfectRoute route))
            return;

        bakedNodes = nodes.ToArray();

        PerfectRouteSample[] samples = new PerfectRouteSample[route.Count];
        for (int i = 0; i < route.Count; i++)
            samples[i] = route.Samples[i];
        bakedTrackSamples = samples;
        bakedTrackBlocked = route.Blocked;
        bakedTrackComplete = route.IsComplete;

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
        var scene = gameObject.scene;
        if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.MarkSceneDirty(scene);
#endif

        Debug.Log(
            $"烘焙完成：{bakedNodes.Length} 个节点，" +
            $"{bakedTrackSamples.Length} 个轨道样本。",
            this);
    }

    private bool TrySimulateRoute(
        IReadOnlyList<NodeData> nodes,
        out PerfectRoute route)
    {
        route = null;

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

        if (bpmTimeline == null ||
            scenePlayer == null ||
            physicsMode == null ||
            bodyCollider == null)
        {
            Debug.LogError(
                "烘焙 Stage 需要 Player、PhysicsActionMode 和 BoxCollider2D。",
                this);
            return false;
        }

        double startSongTime = -GlobalOffsetSeconds;
        double endSongTime = startSongTime;
        for (int i = 0; i < nodes.Count; i++)
        {
            NodeData node = nodes[i];
            if (node == null)
                continue;

            endSongTime = System.Math.Max(
                endSongTime,
                bpmTimeline.BeatToSeconds(node.beat));
        }

        if (clip != null)
        {
            endSongTime = System.Math.Max(
                endSongTime,
                clip.length - GlobalOffsetSeconds);
        }

        endSongTime += Mathf.Max(1f, Time.fixedDeltaTime * 2f);

        Vector2 startPosition = scenePlayer.transform.position;
        var config = new PerfectRouteConfig(
            physicsMode,
            scenePlayer.transform.localEulerAngles.z);
        StageCollisionPresence presence = BuildStageCollisionPresence();
        route = PerfectRouteBuilder.Build(
            startPosition,
            startSongTime,
            nodes,
            bpmTimeline,
            config,
            bodyCollider,
            body,
            endSongTime,
            out PerfectRouteBuildFailure failure,
            presence.IsActive);

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
                "轨道首点必须等于角色起点和歌曲起始时刻。\n" +
                $"实际首点：时间 {actualFirst.Time:0.#####} 秒，" +
                $"位置 {actualFirst.Position}\n" +
                $"期望首点：时间 {startSongTime:0.00000} 秒，" +
                $"位置 {startPosition}\n" +
                "这是起点/歌曲时间锚点问题，不是 Node 碰撞。",
                this);
            return false;
        }

        return true;
    }

    private StageCollisionPresence BuildStageCollisionPresence()
    {
        GameObject[] roots = gameObject.scene.IsValid()
            ? gameObject.scene.GetRootGameObjects()
            : null;
        return StageCollisionPresence.Build(bpmTimeline, roots);
    }

    private void LogTrackBuildFailure(
        PerfectRouteBuildFailure failure,
        IReadOnlyList<NodeData> nodes)
    {
        if (!failure.Failed)
        {
            Debug.LogError(
                "轨道烘焙失败：轨道构建没有返回可用样本。",
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
                    "轨道烘焙失败。\n" +
                    "原因：ChartPlayer 没有配置 BpmTimeline。",
                    this);
                return;

            case PerfectRouteBuildFailureReason.MissingBodyCollider:
                Debug.LogError(
                    "轨道烘焙失败。\n" +
                    "原因：Player 缺少 BoxCollider2D。",
                    this);
                return;

            case PerfectRouteBuildFailureReason.Blocked:
                Debug.LogError(
                    "轨道烘焙失败。\n" +
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
                    "轨道烘焙失败。\n" +
                    $"相关节点：前往 {targetNode} 的路径，" +
                    $"上一个节点是 {previousNode}。\n" +
                    $"原因：模拟在歌曲时间 {failure.SongTime:0.###} 秒仍未完成，" +
                    "达到了模拟步数上限。\n" +
                    $"最后位置：{failure.Position}。",
                    this);
                return;

            default:
                Debug.LogError(
                    "轨道烘焙失败，未识别的构建错误。",
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
            $"力度 {node.strength:0.###}，" +
            $"位置 {node.position}）";
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
        // 一场景一关卡后，用场景路径作为唯一键（原来是 ChartSO 资产路径）。
        string scenePath = player != null && player.gameObject.scene.IsValid()
            ? player.gameObject.scene.path
            : string.Empty;
        return EditorPlayStartBeatPrefix +
            System.Uri.EscapeDataString(scenePath);
    }

    [InitializeOnLoadMethod]
    private static void HookDomainReload()
    {
        EditorApplication.delayCall -= RefreshAllPositions;
        EditorApplication.delayCall += RefreshAllPositions;
    }

    private static void RefreshAllPositions()
    {
        if (Application.isPlaying)
            return;

        foreach (ChartPlayer player in
                 Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None))
        {
            if (player == null || player.BpmTimeline == null)
                continue;

            // 域名重载后主动补做节点力度迁移，避免必须打开时间轴或进入 Play 才生效。
            player.EnsureNodeDataMigrated();
            player.UpdatePos();
        }
    }

    // 由 Marker 调用：只标记，不再自动烘焙
    public void MarkDirty()
    {
        if (dirty)
            return;

        dirty = true;
        EditorUtility.SetDirty(this);       // 刷新 Inspector 状态
    }

    public void QueueUpdatePos(float fromBeat = 0f)
    {
        if (this == null)
            return;

        fromBeat = Mathf.Max(0f, fromBeat);
        if (updatePosQueued)
            queuedUpdateBeat = Mathf.Min(queuedUpdateBeat, fromBeat);
        else
        {
            queuedUpdateBeat = fromBeat;
            updatePosQueued = true;
        }

        if (positionUpdateBatchDepth > 0)
            return;

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
        if (positionUpdateBatchDepth > 0)
            return;

        if (updatePosQueued)
            fallbackFromBeat = Mathf.Min(fallbackFromBeat, queuedUpdateBeat);
        QueueUpdatePos(fallbackFromBeat);
    }

    private void FlushQueuedUpdatePos()
    {
        if (this == null || !updatePosQueued)
            return;

        float fromBeat = queuedUpdateBeat;
        updatePosQueued = false;
        UpdatePos(fromBeat);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            RefreshNodeAppearances();
    }
#endif
}
