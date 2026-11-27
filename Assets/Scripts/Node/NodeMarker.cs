using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NodeMarker : MonoBehaviour, IBeatMappable
{
    public float beat;
    public int order;

    [Tooltip("动作类型：None 无动作 / Jump 跳跃 / FastFall 快速下落 / Respawn 重生点。")]
    public NodeAction action = NodeAction.Jump;

    [Tooltip("重力：None 不改变 / Normal 反转方向并保留竖直速度 / Reset 反转方向并把垂直速度清零。重力转换永远先于动作执行。")]
    public NodeGravity gravity = NodeGravity.None;

    [Tooltip("自动：不判定、不计分、不显示判定提示，始终跟随轨道；Respawn 自动时经过即保存检查点。")]
    public bool auto;

    [Tooltip("显示：关闭后只在运行时隐藏节点，编辑模式仍然可见。")]
    public bool visible = true;

    [Tooltip("动作力度：Jump 为起跳速度，FastFall 为沿当前重力方向的速度脉冲（越大落得越快），Respawn 无效。0 表示使用 GameSettings 默认值。")]
    [Min(0f)]
    public float strength;

    [HideInInspector] public int index;
    [HideInInspector, System.NonSerialized] public float deltaX;

    // 由节奏段模板展开生成时打标，用于识别/同步/重生成。
    [HideInInspector] public string segmentTemplateId;
    [HideInInspector] public string segmentInstanceId;
    [HideInInspector] public int segmentTemplateNodeIndex = -1;

    [Header("Gravity Indicator")]
    [Tooltip("Gravity 不是 None 时显示的小白圆心。把 Node 下白色 Circle 子物体的 SpriteRenderer 拖到这里。")]
    [SerializeField] private SpriteRenderer gravityDot;

    // 仅供旧数据迁移，不再参与运行逻辑。由 ChartPlayer 统一迁移。
    [HideInInspector] public NodeType type;
    [HideInInspector] public NodeTrigger trigger;

    [System.NonSerialized] private SpriteRenderer nodeRenderer;
    private static bool missingGravityDotReported;

    public bool IsManuallyTriggered => !auto;

    public bool HasRouteEffect =>
        action == NodeAction.Jump ||
        action == NodeAction.FastFall ||
        gravity != NodeGravity.None;

    /// <summary>节奏段移动时把节点 beat 作为「主拍点」暴露给编辑器统一平移。</summary>
    public void CollectBeatAnchors(List<BeatAnchor> anchors)
    {
        if (anchors != null)
            anchors.Add(new BeatAnchor(beat, d => beat += d));
    }

    public bool UpdatePos(float x, float deltaX)
    {
        Vector3 position = transform.position;
        bool changed = !Mathf.Approximately(position.x, x) ||
            !Mathf.Approximately(this.deltaX, deltaX);
        position.x = x;
        transform.position = position;
        this.deltaX = deltaX;
        return changed;
    }

    public void ApplyAppearance(NodeAppearanceSet appearanceSet)
    {
        if (appearanceSet == null ||
            !appearanceSet.TryGetNodeColor(action, out Color color))
        {
            return;
        }

        EnsureRenderer();
        if (nodeRenderer == null)
            return;

        if (nodeRenderer.color == color)
            return;

        nodeRenderer.color = color;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(nodeRenderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(nodeRenderer);
        }
#endif
    }

    /// <summary>
    /// 运行时按 visible 隐藏节点；编辑模式一律显示，方便摆位。
    /// 白色重力圆心只由 gravity 决定，并和节点本体一起受 visible 控制。
    /// </summary>
    public void ApplyVisibility()
    {
        EnsureRenderer();
        bool editing = !Application.isPlaying;
        bool showNode = editing || visible;
        if (nodeRenderer != null && nodeRenderer.enabled != showNode)
            nodeRenderer.enabled = showNode;

        ApplyGravityDot(editing || visible);
    }

    private void ApplyGravityDot(bool allowed)
    {
        EnsureGravityDot();
        if (gravityDot == null)
        {
            ReportMissingGravityDot();
            return;
        }

        bool show = allowed && gravity != NodeGravity.None;
        if (gravityDot.enabled != show)
            gravityDot.enabled = show;

        if (!show)
            return;

        if (gravityDot.color != Color.white)
            gravityDot.color = Color.white;

        if (nodeRenderer == null)
            return;

        if (gravityDot.sortingLayerID != nodeRenderer.sortingLayerID)
            gravityDot.sortingLayerID = nodeRenderer.sortingLayerID;

        int sortingOrder = nodeRenderer.sortingOrder + 1;
        if (gravityDot.sortingOrder != sortingOrder)
            gravityDot.sortingOrder = sortingOrder;
    }

    private void EnsureRenderer()
    {
        if (nodeRenderer != null)
            return;

        // 旧 Prefab 里 SpriteRenderer 直接挂在根物体上。
        TryGetComponent(out nodeRenderer);
        if (nodeRenderer != null)
            return;

        // 新 Prefab 把节点本体 Sprite 放在子物体上（Sprite/Apperance），
        // 同层级还放了一个 GravityNode 子物体作为重力圆心。先解析重力圆心，
        // 再取子物体里第一个不是重力圆心的 SpriteRenderer 作为节点本体。
        EnsureGravityDot();
        foreach (SpriteRenderer candidate in
                 GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (candidate == null || candidate == gravityDot)
                continue;
            nodeRenderer = candidate;
            return;
        }
    }

    private void EnsureGravityDot()
    {
        if (gravityDot != null)
            return;

        // 允许把重力圆心子物体命名成 GravityNode（新 Prefab，可能嵌套在 Sprite 下）
        // 或 GravityDot（旧命名），省去手动拖引用。递归查找以兼容嵌套层级。
        gravityDot = FindChildSprite(transform, "GravityNode")
            ?? FindChildSprite(transform, "GravityDot");
    }

    private static SpriteRenderer FindChildSprite(
        Transform parent,
        string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName &&
                child.TryGetComponent(out SpriteRenderer renderer))
            {
                return renderer;
            }

            SpriteRenderer nested = FindChildSprite(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void ReportMissingGravityDot()
    {
#if UNITY_EDITOR
        if (missingGravityDotReported ||
            gravity == NodeGravity.None ||
            Application.isPlaying)
        {
            return;
        }

        missingGravityDotReported = true;
        Debug.LogWarning(
            "NodeMarker 找不到 GravityNode：请在 Node.prefab 下添加一个名为 " +
            "GravityNode 的子物体，挂白色 Circle 的 SpriteRenderer（缩放约 0.1）。" +
            "也可以手动把该 SpriteRenderer 拖到 Gravity Indicator 字段。",
            this);
#endif
    }

    private void ApplyOwnedAppearance()
    {
        ChartPlayer owner = GetComponentInParent<ChartPlayer>();
        NodeAppearanceSet appearanceSet = owner != null
            ? owner.ResolveNodeAppearanceSet()
            : NodeAppearanceSet.LoadDefault();
        ApplyAppearance(appearanceSet);
    }

    private void Awake()
    {
        EnsureRenderer();
        ApplyOwnedAppearance();
        ApplyVisibility();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器里任意节点字段（动作、重力、力度、位置等）变化后触发，
    /// 用于让 Chart Timeline 立即失效预览路线并重绘。
    /// </summary>
    public static event System.Action<NodeMarker> EditorChanged;

    private float lastValidatedBeat = float.NaN;

    private void OnValidate()
    {
        if (strength < 0f)
            strength = 0f;

        ApplyOwnedAppearance();
        ApplyVisibility();
        EditorChanged?.Invoke(this);

        ChartPlayer player = GetComponentInParent<ChartPlayer>();
        if (player == null || player.IsUpdatingPositions)
            return;

        player.MarkDirty();
        if (float.IsNaN(lastValidatedBeat) ||
            !Mathf.Approximately(lastValidatedBeat, beat))
        {
            lastValidatedBeat = beat;
            player.QueueUpdatePos(beat);
        }
    }

    private void Reset() => OnValidate();
#endif
}
