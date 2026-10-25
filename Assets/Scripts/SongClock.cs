using UnityEngine;

/// <summary>
/// 谱面播放使用的唯一 DSP 时钟。
/// 调度后、音频真正开始前 PlaybackTime 为负数；SongTime 再应用谱面全局偏移。
/// 时钟运行后不会自行停止，歌曲结束后仍会继续计时，玩法层可据此做收尾处理。
/// </summary>
public sealed class SongClock
{
    private double scheduledDspTime;
    private double scheduledSongTime;

    public bool IsRunning { get; private set; }

    public bool AudioHasStarted =>
        IsRunning && AudioSettings.dspTime >= scheduledDspTime;

    /// <summary>从计划播放时刻开始计算的原始播放时间，调度等待期间为负数。</summary>
    public double PlaybackTime =>
        IsRunning ? AudioSettings.dspTime - scheduledDspTime : 0d;

    /// <summary>应用谱面全局偏移后的玩法时间。</summary>
    public double SongTime =>
        IsRunning
            ? scheduledSongTime + AudioSettings.dspTime - scheduledDspTime
            : 0d;

    /// <summary>安排音频开始，并返回应传给 PlayScheduled 的 DSP 时刻。</summary>
    public double Schedule(double leadSeconds, double chartOffsetSeconds)
    {
        scheduledDspTime = AudioSettings.dspTime + System.Math.Max(0d, leadSeconds);
        scheduledSongTime = -chartOffsetSeconds;
        IsRunning = true;
        return scheduledDspTime;
    }

    /// <summary>从指定歌曲时间重新调度，用于检查点回退。</summary>
    public double Seek(double leadSeconds, double targetSongTime)
    {
        scheduledDspTime = AudioSettings.dspTime + System.Math.Max(0d, leadSeconds);
        scheduledSongTime = targetSongTime;
        IsRunning = true;
        return scheduledDspTime;
    }

    public void Stop()
    {
        scheduledDspTime = 0d;
        scheduledSongTime = 0d;
        IsRunning = false;
    }
}
