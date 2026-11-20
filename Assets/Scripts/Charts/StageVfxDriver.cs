using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 视效事件运行时调度器：读取 ChartPlayer.VfxEvents，按 Player.SongTime 触发。
///
/// 屏幕震动通过 Cinemachine 的 CinemachineBasicMultiChannelPerlin 噪声实现——
/// 到达拍点后，在持续时间内按线性衰减设置 AmplitudeGain，频率用 FrequencyGain。
/// 该噪声组件应挂在场景里的 CinemachineCamera 物体上（与 CinemachineCamera 同级），
/// 并为其 NoiseProfile 赋值一个 NoiseSettings（例如 6D Shake 预设）。
///
/// SongTime 回跳（死亡回溯）时，震动幅度会随 SongTime 自动收敛到当前正确状态。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageVfxDriver : MonoBehaviour
{
    [Header("Time Source")]
    [Tooltip("用于取 SongTime 与 beat→秒 换算的 ChartPlayer；留空自动查找场景首个。")]
    [SerializeField] private ChartPlayer chartPlayer;
    [Tooltip("用于取 SongTime 的 Player；留空自动从 chartPlayer 解析。")]
    [SerializeField] private Player player;

    [Header("Shake Output")]
    [Tooltip("CinemachineBrain；留空自动查找。")]
    [SerializeField] private CinemachineBrain brain;
    [Tooltip("屏幕震动噪声组件；留空自动从当前激活的 CinemachineCamera 上查找。")]
    [SerializeField] private CinemachineBasicMultiChannelPerlin perlin;

    private Player resolvedPlayer;
    private bool warnedMissingNoise;

    private void Awake()
    {
        if (chartPlayer == null)
            chartPlayer = Object.FindFirstObjectByType<ChartPlayer>();

        resolvedPlayer = player != null
            ? player
            : (chartPlayer != null ? chartPlayer.ResolvePlayer() : null);
    }

    private void Update()
    {
        double songTime = resolvedPlayer != null
            ? resolvedPlayer.SongTime
            : 0d;
        ApplyShake(songTime);
    }

    private void ApplyShake(double songTime)
    {
        ResolveNoise();
        if (perlin == null)
            return;

        float amplitude = 0f;
        float frequency = 0f;

        IReadOnlyList<StageVfxEvent> events =
            chartPlayer != null ? chartPlayer.VfxEvents : null;
        if (events != null && chartPlayer.BpmTimeline != null)
        {
            foreach (StageVfxEvent e in events)
            {
                if (e == null ||
                    e.kind != StageVfxEventKind.ScreenShake ||
                    e.duration <= 0f)
                {
                    continue;
                }

                double start = chartPlayer.BpmTimeline.BeatToSeconds(e.beat);
                if (songTime < start)
                    continue;

                double end = start + e.duration;
                if (songTime >= end)
                    continue;

                // 窗口内线性衰减到 0，形成「触一下、震一下、渐停」的观感。
                float progress = (float)((songTime - start) / e.duration);
                float gain = e.amplitude * (1f - progress);
                if (gain > amplitude)
                    amplitude = gain;
                if (e.frequency > frequency)
                    frequency = e.frequency;
            }
        }

        perlin.AmplitudeGain = amplitude;
        perlin.FrequencyGain = Mathf.Max(0.01f, frequency);
    }

    private void ResolveNoise()
    {
        if (perlin != null)
            return;

        if (brain == null)
            brain = Object.FindFirstObjectByType<CinemachineBrain>();

        if (brain != null)
        {
            ICinemachineCamera active = brain.ActiveVirtualCamera;
            if (active is Component activeCamera)
                perlin = activeCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        }

        if (perlin == null)
            perlin = Object.FindFirstObjectByType<CinemachineBasicMultiChannelPerlin>();

        if (perlin == null && !warnedMissingNoise)
        {
            warnedMissingNoise = true;
            Debug.LogWarning(
                "StageVfxDriver 找不到 CinemachineBasicMultiChannelPerlin：" +
                "请在场景的 CinemachineCamera 物体上添加该组件，并为其 NoiseProfile " +
                "赋值一个 NoiseSettings（例如 6D Shake 预设）。",
                this);
        }
    }
}
