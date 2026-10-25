using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NodeMarker : MonoBehaviour
{
    public float beat;
    public int order;
    public NodeMode mode = NodeMode.JumpNode;
    [HideInInspector] public int index;   // 派生结果，由 ChartPlayer 写回
    [HideInInspector, System.NonSerialized] public float deltaX;

    [System.NonSerialized] private SpriteRenderer nodeRenderer;

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
            !appearanceSet.TryGetNodeColor(mode, out Color color))
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
        ApplyOwnedAppearance();
    }

#if UNITY_EDITOR
    private float lastValidatedBeat = float.NaN;

    private void OnValidate()
    {
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
