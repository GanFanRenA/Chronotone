using UnityEngine;

public class NodeMarker : MonoBehaviour
{
    public float beat;
    public int order;
    [HideInInspector] public int index;   // 派生结果，由 ChartPlayer 写回

#if UNITY_EDITOR
    private void OnValidate()
    {
        ChartPlayer player = GetComponentInParent<ChartPlayer>();
        if (player != null) player.MarkDirty();   // 不再直接 QueueBake
    }

    private void Reset() => OnValidate();
#endif
}