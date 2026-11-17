using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NodeMarker : MonoBehaviour
{
    public float beat;
    public int order;
    [Tooltip("节点的运动类型。")]
    [FormerlySerializedAs("mode")]
    public NodeType type = NodeType.Jump;
    [Tooltip("Manual 需要点击并判定；Auto 自动触发且不判定；None 不触发。Respawn 固定为 None。")]
    public NodeTrigger trigger = NodeTrigger.Manual;
    [HideInInspector] public int index;   // 派生结果，由 ChartPlayer 写回
    [HideInInspector, System.NonSerialized] public float deltaX;

    [System.NonSerialized] private SpriteRenderer nodeRenderer;

    public NodeTrigger EffectiveTrigger =>
        NodeData.NormalizeTrigger(type, trigger);
    public bool IsManuallyTriggered =>
        EffectiveTrigger == NodeTrigger.Manual;

    public void NormalizeTriggerForType()
    {
        trigger = NodeData.NormalizeTrigger(type, trigger);
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
            !appearanceSet.TryGetNodeColor(type, out Color color))
        {
            return;
        }

        if (nodeRenderer == null &&
            !TryGetComponent(out nodeRenderer))
        {
            return;
        }

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
        NormalizeTriggerForType();
        ApplyOwnedAppearance();
    }

#if UNITY_EDITOR
    private float lastValidatedBeat = float.NaN;

    private void OnValidate()
    {
        NormalizeTriggerForType();
        ApplyOwnedAppearance();

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
