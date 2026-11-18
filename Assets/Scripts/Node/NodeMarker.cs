using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NodeMarker : MonoBehaviour
{
    public float beat;
    public int order;

    [Tooltip("动作类型：None 无动作 / Jump 跳跃 / FastFall 快速下落 / Respawn 重生点。")]
    public NodeAction action = NodeAction.Jump;

    [Tooltip("重力：None 不改变 / Normal 强制正常 / Reset 反转并把垂直速度清零。")]
    public NodeGravity gravity = NodeGravity.None;

    [Tooltip("自动：不判定、不计分、不显示判定提示，始终跟随轨道；Respawn 自动时经过即保存检查点。")]
    public bool auto;

    [Tooltip("显示：关闭后只在运行时隐藏节点，编辑模式仍然可见。")]
    public bool visible = true;

    [Tooltip("动作力度：Jump 为起跳速度，FastFall 为向下速度脉冲（越大落得越快），Respawn 无效。0 表示使用 GameSettings 默认值。")]
    [Min(0f)]
    public float strength;

    [HideInInspector] public int index;
    [HideInInspector, System.NonSerialized] public float deltaX;

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
        if (nodeRenderer == null)
            TryGetComponent(out nodeRenderer);
    }

    private void EnsureGravityDot()
    {
        if (gravityDot != null)
            return;

        // 允许直接把子物体命名成 GravityDot，省去手动拖引用。
        Transform dot = transform.Find("GravityDot");
        if (dot != null)
            dot.TryGetComponent(out gravityDot);
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
            "NodeMarker 找不到 GravityDot：请在 Node.prefab 下添加一个名为 " +
            "GravityDot 的子物体，挂白色 Circle 的 SpriteRenderer（缩放约 0.3）。" +
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
    private float lastValidatedBeat = float.NaN;

    private void OnValidate()
    {
        if (strength < 0f)
            strength = 0f;

        ApplyOwnedAppearance();
        ApplyVisibility();

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
