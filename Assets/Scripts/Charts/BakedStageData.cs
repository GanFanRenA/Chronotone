using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次烘焙的产物：节点缓存 + 完美路径采样。
/// 单独存成资产，ChartPlayer 里只保存一个引用。
/// 如果把上万条采样直接序列化在场景/预制体实例上，Unity 会写成逐元素覆盖，
/// 场景文件会膨胀到十几 MB，打开要几十秒；采样量越大越明显。
/// </summary>
[CreateAssetMenu(
    menuName = "Rhythm/Baked Stage Data",
    fileName = "BakedStageData")]
public sealed class BakedStageData : ScriptableObject
{
    [SerializeField] private NodeData[] nodes = Array.Empty<NodeData>();
    [SerializeField] private PerfectRouteSample[] samples =
        Array.Empty<PerfectRouteSample>();
    [SerializeField] private bool blocked;
    [SerializeField] private bool complete;

    public IReadOnlyList<NodeData> Nodes => nodes ?? Array.Empty<NodeData>();

    internal PerfectRouteSample[] SamplesArray =>
        samples ?? Array.Empty<PerfectRouteSample>();

    public int NodeCount => nodes != null ? nodes.Length : 0;
    public int SampleCount => samples != null ? samples.Length : 0;
    public bool Blocked => blocked;
    public bool Complete => complete;

    /// <summary>可直接用于运行的轨道：有采样、模拟完整、没被阻挡。</summary>
    public bool HasRoute =>
        samples != null && samples.Length > 0 && complete && !blocked;

    public void SetData(
        NodeData[] bakedNodes,
        PerfectRouteSample[] routeSamples,
        bool routeBlocked,
        bool routeComplete)
    {
        nodes = bakedNodes ?? Array.Empty<NodeData>();
        samples = routeSamples ?? Array.Empty<PerfectRouteSample>();
        blocked = routeBlocked;
        complete = routeComplete;
    }
}
