using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 由 <see cref="StageAnimationDriver"/> 统一驱动的舞台动画组件接口。
/// ApplyAt(songTime) 是纯函数：把自身状态设置为该歌曲时间对应的正确状态，幂等。
/// </summary>
public interface IStageAnimationSource
{
    void ApplyAt(double songTime);
}

/// <summary>
/// 场景内唯一的舞台动画调度器。各舞台动画组件（显隐 / 飞入飞出 / 淡入淡出）
/// 在 OnEnable 里向它注册时间点或时间区间，由它统一按 SongTime 触发，
/// 从而避免上百个组件各自每帧 Update 空转。
///
/// - 离散事件（RegisterDiscrete）：到达该 beat 时触发一次；同一帧跨过的多个点按组件合并为一次。
/// - 连续区间（RegisterWindow）：在 [startBeat, endBeat) 内每帧调用 ApplyAt，
///   区间外零开销；到达 endBeat 定帧一次。
/// - 死亡回溯（SongTime 回跳）时做一次全量 resync，正常播放为 O(1) 摊销。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageAnimationDriver : MonoBehaviour
{
    private readonly struct DiscreteEntry
    {
        public readonly float beat;
        public readonly IStageAnimationSource source;

        public DiscreteEntry(float beat, IStageAnimationSource source)
        {
            this.beat = beat;
            this.source = source;
        }
    }

    private readonly struct WindowEntry
    {
        public readonly float startBeat;
        public readonly float endBeat;
        public readonly IStageAnimationSource source;

        public WindowEntry(
            float startBeat,
            float endBeat,
            IStageAnimationSource source)
        {
            this.startBeat = startBeat;
            this.endBeat = endBeat;
            this.source = source;
        }
    }

    private const float BeatEpsilon = 0.0001f;

    private static bool hasWarnedMissingDriver;

    [Header("Time Source")]
    [Tooltip("用于取 SongTime 与 beat 换算的 ChartPlayer；留空时自动查找场景中首个。")]
    [SerializeField] private ChartPlayer chartPlayer;
    [Tooltip("用于取 SongTime 的 Player；留空时自动从 chartPlayer 解析。")]
    [SerializeField] private Player player;

    private readonly HashSet<IStageAnimationSource> sourceSet =
        new HashSet<IStageAnimationSource>();
    private readonly List<IStageAnimationSource> sources =
        new List<IStageAnimationSource>();
    private readonly List<DiscreteEntry> discrete =
        new List<DiscreteEntry>();
    private readonly List<WindowEntry> windows =
        new List<WindowEntry>();

    private readonly HashSet<IStageAnimationSource> crossedSources =
        new HashSet<IStageAnimationSource>();
    private readonly HashSet<IStageAnimationSource> tickSources =
        new HashSet<IStageAnimationSource>();
    private readonly HashSet<int> activeWindows =
        new HashSet<int>();
    private readonly List<int> exitedWindows =
        new List<int>();

    private Player resolvedPlayer;
    private int nextDiscreteIndex;
    private int nextWindowIndex;
    private float lastBeat = float.NegativeInfinity;
    private bool dirty;

    public double SongTime => resolvedPlayer != null
        ? resolvedPlayer.SongTime
        : 0d;

    public static StageAnimationDriver FindDriver() =>
        Object.FindFirstObjectByType<StageAnimationDriver>();

    /// <summary>缺少调度器时由动画组件调用一次，提示补齐场景配置。</summary>
    public static void WarnMissingDriver(Object context)
    {
        if (hasWarnedMissingDriver)
            return;

        hasWarnedMissingDriver = true;
        Debug.LogWarning(
            "场景缺少 StageAnimationDriver，舞台动画组件将退回每帧 Update 模式。" +
            "请在场景中某个常驻物体上挂载 StageAnimationDriver 组件。",
            context);
    }

    public void RegisterDiscrete(IStageAnimationSource source, float beat)
    {
        if (source == null)
            return;

        if (sourceSet.Add(source))
            sources.Add(source);

        discrete.Add(new DiscreteEntry(beat, source));
        dirty = true;
    }

    public void RegisterWindow(
        IStageAnimationSource source,
        float startBeat,
        float endBeat)
    {
        if (source == null)
            return;

        if (sourceSet.Add(source))
            sources.Add(source);

        windows.Add(new WindowEntry(startBeat, endBeat, source));
        dirty = true;
    }

    public void Unregister(IStageAnimationSource source)
    {
        if (source == null)
            return;

        if (!sourceSet.Remove(source))
            return;

        sources.Remove(source);

        for (int i = discrete.Count - 1; i >= 0; i--)
        {
            if (discrete[i].source == source)
                discrete.RemoveAt(i);
        }

        for (int i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i].source == source)
                windows.RemoveAt(i);
        }

        dirty = true;
    }

    private void Awake()
    {
        hasWarnedMissingDriver = false;

        if (chartPlayer == null)
            chartPlayer = Object.FindFirstObjectByType<ChartPlayer>();

        resolvedPlayer = player != null
            ? player
            : (chartPlayer != null ? chartPlayer.ResolvePlayer() : null);
    }

    private void Update()
    {
        double songTime = SongTime;
        float beat = ToBeat(songTime);

        if (dirty)
        {
            Rebuild();
            Resync(songTime, beat);
            dirty = false;
            lastBeat = beat;
            return;
        }

        if (beat < lastBeat - BeatEpsilon)
        {
            Resync(songTime, beat);
        }
        else
        {
            Advance(songTime, beat);
        }

        lastBeat = beat;
    }

    private void Rebuild()
    {
        discrete.Sort((a, b) => a.beat.CompareTo(b.beat));
        windows.Sort((a, b) =>
        {
            int byStart = a.startBeat.CompareTo(b.startBeat);
            return byStart != 0 ? byStart : a.endBeat.CompareTo(b.endBeat);
        });
    }

    /// <summary>全量重放到当前时间：重置离散状态、重建窗口游标与活跃集。</summary>
    private void Resync(double songTime, float beat)
    {
        foreach (IStageAnimationSource source in sources)
            source.ApplyAt(songTime);

        nextDiscreteIndex = 0;
        while (nextDiscreteIndex < discrete.Count &&
               discrete[nextDiscreteIndex].beat <= beat + BeatEpsilon)
        {
            nextDiscreteIndex++;
        }

        nextWindowIndex = 0;
        activeWindows.Clear();
        while (nextWindowIndex < windows.Count &&
               windows[nextWindowIndex].startBeat <= beat + BeatEpsilon)
        {
            int windowIndex = nextWindowIndex;
            nextWindowIndex++;
            if (beat < windows[windowIndex].endBeat - BeatEpsilon)
                activeWindows.Add(windowIndex);
        }
    }

    private void Advance(double songTime, float beat)
    {
        // 1. 离散：跨过的点合并到组件维度，各触发一次。
        crossedSources.Clear();
        while (nextDiscreteIndex < discrete.Count &&
               discrete[nextDiscreteIndex].beat <= beat + BeatEpsilon)
        {
            crossedSources.Add(discrete[nextDiscreteIndex].source);
            nextDiscreteIndex++;
        }

        foreach (IStageAnimationSource source in crossedSources)
            source.ApplyAt(songTime);

        // 2. 连续区间：进入 / 每帧 tick / 离开定帧。
        tickSources.Clear();
        while (nextWindowIndex < windows.Count &&
               windows[nextWindowIndex].startBeat <= beat + BeatEpsilon)
        {
            int windowIndex = nextWindowIndex;
            nextWindowIndex++;

            if (beat < windows[windowIndex].endBeat - BeatEpsilon)
            {
                activeWindows.Add(windowIndex);
            }
            else
            {
                // 零时长，或一帧内已越过终点：直接定帧到终态。
                tickSources.Add(windows[windowIndex].source);
            }
        }

        exitedWindows.Clear();
        foreach (int windowIndex in activeWindows)
        {
            if (beat >= windows[windowIndex].endBeat - BeatEpsilon)
            {
                exitedWindows.Add(windowIndex);
                tickSources.Add(windows[windowIndex].source);
            }
            else
            {
                tickSources.Add(windows[windowIndex].source);
            }
        }

        foreach (int windowIndex in exitedWindows)
            activeWindows.Remove(windowIndex);

        foreach (IStageAnimationSource source in tickSources)
            source.ApplyAt(songTime);
    }

    private float ToBeat(double songTime)
    {
        if (chartPlayer != null && chartPlayer.BpmTimeline != null)
            return chartPlayer.BpmTimeline.SecondsToBeat((float)songTime);

        return (float)songTime * 2f; // 兜底 120 BPM
    }
}
