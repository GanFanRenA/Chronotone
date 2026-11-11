using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    public ChartPlayer chartPlayer;

    [Header("AutoPlay（自动模式）")]
    [Tooltip("启用后按谱面节拍自动跳跃并完成判定，手动输入将被忽略。")]
    [SerializeField] private bool autoModeEnabled = false;

    [Tooltip("AutoPlay 判定时刻。0 为音符正拍；负值提前，正值延后，单位为毫秒。数值会限制在当前 Great 判定窗内。")]
    [SerializeField, Range(-300, 300)]
    private int autoPlayOffsetMilliseconds = 0;

    [Tooltip("调度前置时间，给音频混音器准备缓冲")]
    public double scheduleLead = 0.15;

    [Header("死亡回溯")]
    [Tooltip("Miss 或格挡判定后的短暂停顿，随后开始倒放回溯。")]
    [SerializeField, Min(0f)] private float respawnDelaySeconds = 0.35f;
    [SerializeField, Min(0.01f)] private float rewindDurationSeconds = 0.65f;
    [SerializeField, Min(1)] private int motionSamplesPerSecond = 30;
    [SerializeField, Min(1f)] private float motionHistorySeconds = 30f;
    [SerializeField] private Color rewindTint = new Color(0.35f, 0.85f, 1f, 1f);
    [SerializeField, Min(0f)] private float rewindPulseFrequency = 18f;

    [Header("行动模式")]
    [SerializeField] private PlayerActionMode defaultActionMode = null;

    private enum RewindState
    {
        None,
        Waiting,
        Playing
    }

    private readonly struct MotionSample
    {
        public MotionSample(
            double songTime,
            Vector2 position,
            Quaternion rotation)
        {
            SongTime = songTime;
            Position = position;
            Rotation = rotation;
        }

        public double SongTime { get; }
        public Vector2 Position { get; }
        public Quaternion Rotation { get; }
    }

    private IReadOnlyList<NodeData> nodes;
    private AudioSource audioSource;
    private BasicJudgement basicJudgement;
    private SpriteRenderer playerRenderer;
    private Color playerRendererColor;
    private readonly SongClock songClock = new SongClock();
    private readonly List<MotionSample> recordedMotion = new List<MotionSample>();
    private readonly List<MotionSample> rewindPath = new List<MotionSample>();
    private PlayerActionMode[] actionModes = Array.Empty<PlayerActionMode>();
    private PlayerActionMode currentActionMode;
    private bool modesInitialized;
    private bool playbackStarted;
    private bool trackEnabled;
    private int trackReleaseNodeIndex = -1;
    private double trackReleaseSongTime;
    private float playbackStartBeat;
    private int playbackStartNodeIndex = -1;
    private Vector2 playbackStartPosition;
    private double playbackStartSongTime;
    private int latestRespawnNodeIndex = -1;
    private int nextDueNodeIndex;
    private RewindState rewindState;
    private float respawnDelayRemaining;
    private float rewindElapsed;
    private double rewindStartSongTime;
    private int rewindCheckpointNodeIndex;
    private Vector2 rewindCheckpointPosition;
    private Quaternion rewindCheckpointRotation;
    private double rewindCheckpointSongTime;
    private double lastRecordedSongTime;

    public bool IsRunning => songClock.IsRunning;
    public bool AudioHasStarted => songClock.AudioHasStarted;
    public double SongTime => songClock.SongTime;
    public Vector2 OriginalPosition { get; private set; }
    public Quaternion OriginalRotation { get; private set; }
    public int PlaybackStartNodeIndex => playbackStartNodeIndex;
    public Vector2 PlaybackStartPosition => playbackStartPosition;
    public double PlaybackStartSongTime => playbackStartSongTime;
    public StageDefinitionSO Stage =>
        chartPlayer != null ? chartPlayer.Stage : null;
    public ChartSO Chart => Stage != null ? Stage.Chart : null;
    public ChartTrackSO Track => Stage != null ? Stage.Track : null;
    public IReadOnlyList<NodeData> Nodes => nodes;
    public IReadOnlyList<PlayerActionMode> ActionModes => actionModes;
    public PlayerActionMode CurrentActionMode => currentActionMode;
    public bool IsRewinding => rewindState != RewindState.None;
    public bool IsControlLocked => IsRewinding;
    public bool IsTrackEnabled => trackEnabled;
    public int TrackReleaseNodeIndex => trackReleaseNodeIndex;
    public double TrackReleaseSongTime => trackReleaseSongTime;
    public bool AutoModeEnabled
    {
        get => autoModeEnabled;
        set => autoModeEnabled = value;
    }
    public int AutoPlayOffsetMilliseconds
    {
        get => autoPlayOffsetMilliseconds;
        set => autoPlayOffsetMilliseconds = value;
    }
    public double AutoPlayOffsetSeconds =>
        autoPlayOffsetMilliseconds / 1000d;

    public event Action<PlayerActionMode, PlayerActionMode> ActionModeChanged;

    private void OnValidate()
    {
        BasicJudgement judgement = basicJudgement != null
            ? basicJudgement
            : GetComponent<BasicJudgement>();
        if (judgement == null)
            return;

        int greatWindowMilliseconds = Mathf.FloorToInt(
            judgement.GreatWindowSeconds * 1000f);
        autoPlayOffsetMilliseconds = Mathf.Clamp(
            autoPlayOffsetMilliseconds,
            -greatWindowMilliseconds,
            greatWindowMilliseconds);
    }

    void Awake()
    {
        OriginalPosition = transform.position;
        OriginalRotation = transform.localRotation;

        if (!TryGetComponent(out basicJudgement))
        {
            Debug.LogError("Player 缺少 BasicJudgement 组件", this);
            enabled = false;
            return;
        }

        AutoPlayOffsetMilliseconds = Mathf.Clamp(
            autoPlayOffsetMilliseconds,
            -Mathf.FloorToInt(basicJudgement.GreatWindowSeconds * 1000f),
            Mathf.FloorToInt(basicJudgement.GreatWindowSeconds * 1000f));

        basicJudgement.JudgementResolved += HandleJudgement;
        playerRenderer = GetComponent<SpriteRenderer>();
        if (playerRenderer != null)
            playerRendererColor = playerRenderer.color;

        InitializeActionModes();
        ResolvePlaybackStartBeat();
    }

    private void Start()
    {
        if (chartPlayer == null || chartPlayer.Chart == null)
        {
            Debug.LogError("Block 没有绑定 ChartPlayer 或 ChartSO", this);
            enabled = false;
            return;
        }

        ChartSO chart = chartPlayer.Chart;
        if (chart.clip == null)
        {
            Debug.LogError("ChartSO 上没有 AudioClip", this);
            enabled = false;
            return;
        }

        nodes = chart.nodes;
        InitializeNodeQueues();
        rewindState = RewindState.None;
        respawnDelayRemaining = 0f;
        rewindElapsed = 0f;
        recordedMotion.Clear();
        rewindPath.Clear();
        lastRecordedSongTime = double.NegativeInfinity;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.LogError("Player 缺少 AudioSource 组件", this);
            enabled = false;
            return;
        }

        audioSource.playOnAwake = false;
        audioSource.Stop();
        audioSource.clip = chart.clip;
        audioSource.time = 0f;

        if (chart.clip.loadState == AudioDataLoadState.Loaded)
            BeginPlayback(chart);
        else
            StartCoroutine(BeginPlaybackWhenAudioDataReady(chart));
    }

    private IEnumerator BeginPlaybackWhenAudioDataReady(ChartSO chart)
    {
        AudioClip clip = chart.clip;
        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();

        while (clip.loadState == AudioDataLoadState.Loading)
            yield return null;

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogError("歌曲音频数据加载失败，无法开始播放", this);
            enabled = false;
            yield break;
        }

        BeginPlayback(chart);
    }

    private void BeginPlayback(ChartSO chart)
    {
        playbackStarted = true;
        trackEnabled = true;
        trackReleaseNodeIndex = -1;

        float startSongTime =
            chart.bpmTimeline.BeatToSeconds(playbackStartBeat);
        double audioSeconds;
        if (playbackStartNodeIndex < 0 && playbackStartBeat <= 0f)
        {
            playbackStartPosition = OriginalPosition;
            playbackStartSongTime = -chart.GlobalOffsetSeconds;
            audioSeconds = 0d;
        }
        else
        {
            playbackStartSongTime = startSongTime;
            playbackStartPosition = ResolvePlaybackStartPosition(chart);

            audioSeconds = startSongTime + chart.GlobalOffsetSeconds;
            double maxAudioSeconds =
                Math.Max(0d, audioSource.clip.length - 0.001d);
            if (audioSeconds < 0d || audioSeconds > maxAudioSeconds)
            {
                Debug.LogWarning(
                    $"开局拍点 {playbackStartBeat:0.###} 超出了音频可播放范围，已限制音频位置。",
                    this);
            }

        }

        // 轨道模拟必须在音频调度前完成，避免同步构建吞掉 scheduleLead。
        currentActionMode?.PrepareForPlayback();
        double scheduledDspTime = playbackStartNodeIndex < 0 &&
            playbackStartBeat <= 0f
                ? songClock.Schedule(
                    scheduleLead,
                    chart.GlobalOffsetSeconds)
                : songClock.Seek(
                    scheduleLead,
                    playbackStartSongTime);

        // dspTime 和 PlayScheduled 共用同一时钟；音频数据必须已加载，
        // 否则首次播放会比计划晚，而重生时因缓存已就绪反而能对齐。
        audioSource.Stop();
        audioSource.time = ClampAudioSeconds(
            audioSeconds,
            Math.Max(0d, audioSource.clip.length - 0.001d));
        audioSource.PlayScheduled(scheduledDspTime);

        currentActionMode?.Enter();
    }

    private static float ClampAudioSeconds(
        double audioSeconds,
        double maxAudioSeconds)
    {
        return (float)Math.Max(
            0d,
            Math.Min(audioSeconds, maxAudioSeconds));
    }

    void Update()
    {
        if (!playbackStarted)
            return;

        if (!songClock.AudioHasStarted)
            return;

        double songTime = SongTime;
        if (rewindState == RewindState.Waiting)
        {
            respawnDelayRemaining -= Time.unscaledDeltaTime;
            if (respawnDelayRemaining <= 0f)
                BeginRewindAnimation();
            return;
        }

        if (rewindState == RewindState.Playing)
        {
            UpdateRewindAnimation(Time.unscaledDeltaTime);
            return;
        }

        RecordMotion(songTime);
        basicJudgement.CaptureInput();
    }

    void FixedUpdate()
    {
        if (!playbackStarted ||
            IsControlLocked ||
            !songClock.AudioHasStarted)
        {
            return;
        }

        double fixedSongTime = SongTime;
        ProcessDueNodes(fixedSongTime);
        if (IsControlLocked)
            return;

        basicJudgement.Tick(fixedSongTime);
        if (IsControlLocked)
            return;

        ReleaseTrackForUnconsumedDueNode(fixedSongTime);
        currentActionMode?.FixedTick(
            fixedSongTime,
            Time.fixedDeltaTime);
    }

    void OnEnable()
    {
        if (playbackStarted)
        {
            currentActionMode?.Enter();
            if (IsControlLocked)
                currentActionMode?.SetControlLocked(true);
        }
    }

    void OnDisable()
    {
        currentActionMode?.Exit();
    }

    void OnDestroy()
    {
        if (basicJudgement != null)
        {
            basicJudgement.JudgementResolved -= HandleJudgement;
        }
    }

    public bool SwitchActionMode(PlayerActionMode mode)
    {
        if (IsControlLocked)
            return false;

        if (!modesInitialized)
        {
            Debug.LogWarning("行动模式尚未初始化", this);
            return false;
        }

        if (mode == null || Array.IndexOf(actionModes, mode) < 0)
        {
            Debug.LogWarning("目标行动模式未注册到当前 Player", this);
            return false;
        }

        SwitchActionModeInternal(mode);
        return true;
    }

    public bool SwitchActionMode<T>() where T : PlayerActionMode
    {
        if (IsControlLocked)
            return false;

        foreach (PlayerActionMode mode in actionModes)
        {
            if (mode is T)
            {
                SwitchActionModeInternal(mode);
                return true;
            }
        }

        Debug.LogWarning($"当前 Player 没有 {typeof(T).Name} 行动模式", this);
        return false;
    }

    public void ReportBlockedDeath()
    {
        if (!playbackStarted || IsControlLocked || !songClock.AudioHasStarted)
            return;

        Debug.Log("[死亡] 脱轨后持续受阻");
        BeginDeathRewind();
    }

    private void HandleJudgement(JudgementResult result)
    {
        if (!playbackStarted ||
            !result.HasNote ||
            nodes == null ||
            result.NoteIndex < 0 ||
            result.NoteIndex >= nodes.Count ||
            nodes[result.NoteIndex] == null)
            return;

        if (result.Grade != JudgementGrade.Perfect &&
            result.Grade != JudgementGrade.Great &&
            result.Grade != JudgementGrade.Bad &&
            result.Grade != JudgementGrade.Miss)
        {
            Debug.LogError(
                $"[判定] 已忽略未知判定结果：{result.Grade}",
                this);
            return;
        }

        if (result.IsSuccessfulHit)
        {
            trackEnabled = true;
            trackReleaseNodeIndex = -1;
            return;
        }

        if (result.Grade == JudgementGrade.Miss)
        {
            Debug.Log("[死亡] Miss，启动回溯");
            BeginDeathRewind();
            return;
        }

        if (!trackEnabled)
            return;

        ReleaseTrack(result.NoteIndex, result.SongTime);
    }

    private void ReleaseTrack(int nodeIndex, double songTime)
    {
        trackEnabled = false;
        trackReleaseNodeIndex = nodeIndex;
        trackReleaseSongTime = songTime;
    }

    private void ReleaseTrackForUnconsumedDueNode(double songTime)
    {
        if (!trackEnabled ||
            AutoModeEnabled ||
            !basicJudgement.TryGetUnresolvedPendingNoteAtOrBefore(
                songTime,
                out int nodeIndex,
                out double noteSongTime))
        {
            return;
        }

        ReleaseTrack(nodeIndex, noteSongTime);
    }

    private void BeginDeathRewind()
    {
        if (!TryBuildCheckpoint(
            out int checkpointNodeIndex,
            out Vector2 checkpointPosition,
            out double checkpointSongTime))
            return;

        rewindCheckpointNodeIndex = checkpointNodeIndex;
        rewindCheckpointPosition = checkpointPosition;
        rewindCheckpointRotation = OriginalRotation;
        rewindCheckpointSongTime = checkpointSongTime;
        rewindStartSongTime = SongTime;
        respawnDelayRemaining = Mathf.Max(0f, respawnDelaySeconds);
        rewindElapsed = 0f;
        rewindState = RewindState.Waiting;
        trackEnabled = false;

        basicJudgement.Pause();
        currentActionMode?.SetControlLocked(true);

        if (audioSource != null && audioSource.isPlaying)
            audioSource.Pause();

        if (playerRenderer != null)
            playerRendererColor = playerRenderer.color;
    }

    private void BeginRewindAnimation()
    {
        BuildRewindPath();
        rewindElapsed = 0f;

        if (rewindPath.Count == 0 ||
            rewindDurationSeconds <= 0.001f)
        {
            CompleteDeathRewind();
            return;
        }

        rewindState = RewindState.Playing;
        ApplyRewindVisual(0f);
    }

    private void UpdateRewindAnimation(float unscaledDeltaTime)
    {
        rewindElapsed += Mathf.Max(0f, unscaledDeltaTime);
        float progress = Mathf.Clamp01(
            rewindElapsed / Mathf.Max(0.01f, rewindDurationSeconds));

        ApplyRewindVisual(progress);
        if (progress >= 1f)
            CompleteDeathRewind();
    }

    private void CompleteDeathRewind()
    {
        if (rewindState == RewindState.None)
            return;

        RestoreRendererColor();
        RespawnToLatestCheckpoint();
        currentActionMode?.SetControlLocked(false);
        rewindState = RewindState.None;
        respawnDelayRemaining = 0f;
        rewindElapsed = 0f;
        rewindPath.Clear();
    }

    private void RecordMotion(double songTime)
    {
        double interval = 1d / Mathf.Max(1, motionSamplesPerSecond);
        if (songTime - lastRecordedSongTime < interval)
            return;

        recordedMotion.Add(new MotionSample(
            songTime,
            transform.position,
            transform.localRotation));
        lastRecordedSongTime = songTime;

        double historyStart = songTime - Mathf.Max(1f, motionHistorySeconds);
        int removeCount = 0;
        while (removeCount < recordedMotion.Count &&
               recordedMotion[removeCount].SongTime < historyStart)
            removeCount++;

        if (removeCount > 0)
            recordedMotion.RemoveRange(0, removeCount);
    }

    private void BuildRewindPath()
    {
        rewindPath.Clear();
        rewindPath.Add(new MotionSample(
            rewindCheckpointSongTime,
            rewindCheckpointPosition,
            rewindCheckpointRotation));

        foreach (MotionSample sample in recordedMotion)
        {
            if (sample.SongTime <= rewindCheckpointSongTime ||
                sample.SongTime >= rewindStartSongTime)
                continue;

            rewindPath.Add(sample);
        }

        rewindPath.Add(new MotionSample(
            rewindStartSongTime,
            transform.position,
            transform.localRotation));
    }

    private void ApplyRewindVisual(float progress)
    {
        MotionSample sample = SampleRewindPath(progress);
        if (currentActionMode != null)
            currentActionMode.SetRewindPose(sample.Position, sample.Rotation);
        else
            transform.SetPositionAndRotation(
                new Vector3(
                    sample.Position.x,
                    sample.Position.y,
                    transform.position.z),
                sample.Rotation);

        if (playerRenderer == null)
            return;

        float pulse = Mathf.PingPong(
            rewindElapsed * Mathf.Max(0f, rewindPulseFrequency),
            1f);
        Color color = Color.Lerp(playerRendererColor, rewindTint, 0.65f);
        color.a = playerRendererColor.a * Mathf.Lerp(0.35f, 1f, pulse);
        playerRenderer.color = color;
    }

    private MotionSample SampleRewindPath(float progress)
    {
        double targetSongTime = rewindStartSongTime +
            (rewindCheckpointSongTime - rewindStartSongTime) * progress;

        for (int i = 0; i < rewindPath.Count - 1; i++)
        {
            MotionSample from = rewindPath[i];
            MotionSample to = rewindPath[i + 1];
            if (targetSongTime > to.SongTime)
                continue;

            double span = to.SongTime - from.SongTime;
            float localProgress = span <= 0d
                ? 1f
                : (float)((targetSongTime - from.SongTime) / span);
            localProgress = Mathf.Clamp01(localProgress);

            return new MotionSample(
                targetSongTime,
                Vector2.Lerp(from.Position, to.Position, localProgress),
                Quaternion.Slerp(from.Rotation, to.Rotation, localProgress));
        }

        return rewindPath[rewindPath.Count - 1];
    }

    private void RestoreRendererColor()
    {
        if (playerRenderer != null)
            playerRenderer.color = playerRendererColor;
    }

    private bool TryBuildCheckpoint(
        out int checkpointNodeIndex,
        out Vector2 checkpointPosition,
        out double checkpointSongTime)
    {
        bool hasCheckpoint =
            nodes != null &&
            latestRespawnNodeIndex >= 0 &&
            latestRespawnNodeIndex < nodes.Count &&
            chartPlayer != null &&
            chartPlayer.Chart != null &&
            chartPlayer.Chart.bpmTimeline != null;

        checkpointNodeIndex = hasCheckpoint ? latestRespawnNodeIndex : -1;
        checkpointPosition = hasCheckpoint
            ? nodes[checkpointNodeIndex].position
            : playbackStartPosition;
        checkpointSongTime =
            hasCheckpoint &&
            chartPlayer != null &&
            chartPlayer.Chart != null
                ? chartPlayer.Chart.bpmTimeline.BeatToSeconds(
                    nodes[checkpointNodeIndex].beat)
                : playbackStartSongTime;
        return playbackStarted;
    }

    private void ResolvePlaybackStartBeat()
    {
        playbackStartBeat = 0f;
        playbackStartPosition = OriginalPosition;
        playbackStartNodeIndex = -1;

        if (chartPlayer == null ||
            chartPlayer.Chart == null ||
            chartPlayer.Chart.bpmTimeline == null)
            return;

#if UNITY_EDITOR
        ChartPlayer.TryConsumeEditorPlayStartBeat(
            chartPlayer,
            out playbackStartBeat);
#endif

        ChartSO chart = chartPlayer.Chart;
        if (chart.clip == null)
            return;

        float maxAudioSeconds = Mathf.Max(
            0f,
            chart.clip.length - 0.001f - chart.GlobalOffsetSeconds);
        float maxBeat = chart.bpmTimeline.SecondsToBeat(maxAudioSeconds);
        float clampedBeat = Mathf.Clamp(
            playbackStartBeat,
            0f,
            Mathf.Max(0f, maxBeat));
        if (!Mathf.Approximately(clampedBeat, playbackStartBeat))
        {
            Debug.LogWarning(
                $"红轴 {playbackStartBeat:0.###} 超出谱面可播放范围，已限制到 {clampedBeat:0.###}。",
                this);
            playbackStartBeat = clampedBeat;
        }

        if (chart.nodes == null)
            return;

        for (int i = chart.nodes.Count - 1; i >= 0; i--)
        {
            NodeData node = chart.nodes[i];
            if (node == null || node.beat >= playbackStartBeat)
                continue;

            playbackStartNodeIndex = i;
            playbackStartBeat = node.beat;
            break;
        }
    }

    private void InitializeNodeQueues()
    {
        nextDueNodeIndex = Mathf.Clamp(
            playbackStartNodeIndex + 1,
            0,
            nodes?.Count ?? 0);
        latestRespawnNodeIndex = -1;

        if (nodes == null)
            return;

        for (int i = 0; i < nextDueNodeIndex; i++)
        {
            if (nodes[i] != null &&
                nodes[i].mode == NodeMode.RespawnNode)
                latestRespawnNodeIndex = i;
        }
    }

    private Vector2 ResolvePlaybackStartPosition(ChartSO chart)
    {
        if (nodes != null &&
            playbackStartNodeIndex >= 0 &&
            playbackStartNodeIndex < nodes.Count &&
            nodes[playbackStartNodeIndex] != null)
            return nodes[playbackStartNodeIndex].position;

        if (playbackStartBeat <= 0f ||
            nodes == null ||
            nodes.Count == 0)
            return OriginalPosition;

        var baseline = new ChartMotionTimeline(
            OriginalPosition,
            -chart.GlobalOffsetSeconds,
            nodes,
            chart.bpmTimeline);
        return baseline.Sample(
            chart.bpmTimeline.BeatToSeconds(playbackStartBeat));
    }

    private void ProcessDueNodes(double songTime)
    {
        if (nodes == null || nodes.Count == 0 ||
            chartPlayer == null || chartPlayer.Chart == null ||
            chartPlayer.Chart.bpmTimeline == null ||
            !songClock.AudioHasStarted)
            return;

        while (nextDueNodeIndex < nodes.Count)
        {
            NodeData node = nodes[nextDueNodeIndex];
            if (node == null)
            {
                nextDueNodeIndex++;
                continue;
            }

            double nodeSongTime =
                chartPlayer.Chart.bpmTimeline.BeatToSeconds(node.beat);
            if (songTime < nodeSongTime)
                break;

            int nodeIndex = nextDueNodeIndex;

            if (node.mode == NodeMode.RespawnNode)
                latestRespawnNodeIndex = nodeIndex;

            nextDueNodeIndex++;
            if (IsControlLocked)
                return;
        }
    }

    private void RespawnToLatestCheckpoint()
    {
        if (!playbackStarted || audioSource == null ||
            chartPlayer == null || chartPlayer.Chart == null)
            return;

        int checkpointNodeIndex = rewindCheckpointNodeIndex;
        Vector2 checkpointPosition = rewindCheckpointPosition;
        double checkpointSongTime = rewindCheckpointSongTime;

        double audioSeconds =
            checkpointSongTime + chartPlayer.Chart.GlobalOffsetSeconds;
        double maxAudioSeconds = Math.Max(0d, audioSource.clip.length - 0.001d);
        audioSeconds = Math.Max(0d, Math.Min(audioSeconds, maxAudioSeconds));

        audioSource.Stop();
        audioSource.time = (float)audioSeconds;

        int resetNodeIndex = checkpointNodeIndex >= 0
            ? checkpointNodeIndex
            : playbackStartNodeIndex;
        basicJudgement.ResetFromCheckpoint(resetNodeIndex);

        currentActionMode?.RespawnTo(
            checkpointPosition,
            checkpointSongTime,
            checkpointNodeIndex);

        trackEnabled = true;
        trackReleaseNodeIndex = -1;
        nextDueNodeIndex = resetNodeIndex + 1;
        respawnDelayRemaining = 0f;
        recordedMotion.Clear();
        lastRecordedSongTime = double.NegativeInfinity;
        double scheduledDspTime = songClock.Seek(scheduleLead, checkpointSongTime);
        audioSource.PlayScheduled(scheduledDspTime);
    }

    private void InitializeActionModes()
    {
        actionModes = GetComponents<PlayerActionMode>();
        if (actionModes.Length == 0)
        {
            Debug.LogError("Player 没有配置任何行动模式", this);
            modesInitialized = true;
            return;
        }

        foreach (PlayerActionMode mode in actionModes)
            mode.Bind(this);

        PlayerActionMode initialMode = defaultActionMode;
        if (initialMode == null || Array.IndexOf(actionModes, initialMode) < 0)
        {
            if (defaultActionMode != null)
                Debug.LogWarning("默认行动模式不在当前 Player 的模式列表中，已使用第一个可用模式", this);

            foreach (PlayerActionMode mode in actionModes)
            {
                if (mode is PhysicsActionMode)
                {
                    initialMode = mode;
                    break;
                }
            }

            if (initialMode == null || Array.IndexOf(actionModes, initialMode) < 0)
                initialMode = actionModes[0];
        }

        currentActionMode = initialMode;
        modesInitialized = true;
    }

    private void SwitchActionModeInternal(PlayerActionMode nextMode)
    {
        if (nextMode == null || nextMode == currentActionMode)
            return;

        PlayerActionMode previousMode = currentActionMode;
        if (playbackStarted)
            previousMode?.Exit();

        currentActionMode = nextMode;

        if (playbackStarted)
            currentActionMode.Enter();

        ActionModeChanged?.Invoke(previousMode, currentActionMode);
    }
}
