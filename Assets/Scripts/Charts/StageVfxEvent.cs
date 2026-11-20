using UnityEngine;

/// <summary>视效事件大类，用于 Chart Timeline 上色与图例。</summary>
public enum StageVfxEventKind
{
    ScreenShake = 0
}

/// <summary>
/// 视效事件：直接写在 ChartPlayer 上、在 Chart Timeline 里编辑的演出事件。
/// 目前只有屏幕震动；以后新增视效类型时扩展 kind 与字段即可。
/// beat 是触发拍点，运行时用 ChartPlayer.BpmTimeline 转成秒。
/// </summary>
[System.Serializable]
public sealed class StageVfxEvent
{
    [Tooltip("触发拍点（相对谱面，运行时用 ChartPlayer.BpmTimeline 转秒）。")]
    public float beat;

    [Tooltip("视效类型。")]
    public StageVfxEventKind kind = StageVfxEventKind.ScreenShake;

    [Tooltip("屏幕震动持续时间（秒）。")]
    [Min(0f)]
    public float duration = 0.3f;

    [Tooltip("震动幅度（映射为 Cinemachine 噪声的 AmplitudeGain）。")]
    [Min(0f)]
    public float amplitude = 0.25f;

    [Tooltip("震动频率（映射为 Cinemachine 噪声的 FrequencyGain）。")]
    [Min(0f)]
    public float frequency = 1f;
}
