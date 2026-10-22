using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Node : MonoBehaviour
{
    public float beat;
    public int order;
    public int index;   // 派生结果，由所属 Chart 写入

#if UNITY_EDITOR
    private void OnValidate() => NotifyChart();
    private void Reset() => NotifyChart();
    private void OnDestroy() => NotifyChart();

    private void NotifyChart()
    {
        Chart chart = GetComponentInParent<Chart>();
        if (chart != null)
            chart.QueueRefresh();
    }
#endif
}