using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BasicJudgement : MonoBehaviour
{
    [System.NonSerialized] private float perfectWindowSeconds;
    [System.NonSerialized] private float greatWindowSeconds;
    [System.NonSerialized] private float badWindowSeconds;
    [System.NonSerialized] private bool settingsApplied;
    private Player player;
    private BasicJudgementFeedback feedback;
    private IReadOnlyList<NodeData> nodes;
    private double[] noteTimes;
    private bool[] judged;
    private int nextPendingNodeIndex;
    private bool blocked;
    private bool manualInputQueued;
    private double manualInputSongTime;
    public float GreatWindowSeconds
    {
        get
        {
            EnsureSettings();
            return greatWindowSeconds;
        }
    }

    public event Action<JudgementResult> JudgementResolved;
    public event Action InputPressed;

    void Awake()
    {
        player = GetComponent<Player>();
        EnsureSettings();
        feedback = GetComponentInChildren<BasicJudgementFeedback>(true);

        if (feedback == null)
        {
            Debug.LogError("BasicJudgement 缺少 BasicJudgementFeedback 子对象", this);
            enabled = false;
        }
    }

    private void EnsureSettings()
    {
        if (settingsApplied)
            return;

        GameSettingsSO settings = player != null
            ? player.GameSettings
            : GameSettingsSO.LoadDefault();
        perfectWindowSeconds = settings.PerfectWindowSeconds;
        greatWindowSeconds = settings.GreatWindowSeconds;
        badWindowSeconds = settings.BadWindowSeconds;
        settingsApplied = true;
    }

    void Start()
    {
        if (player == null ||
            player.chartPlayer == null ||
            player.chartPlayer.Chart == null)
        {
            enabled = false;
            return;
        }

        ChartSO chart = player.chartPlayer.Chart;
        nodes = chart.nodes;
        if (nodes == null ||
            nodes.Count == 0 ||
            chart.bpmTimeline == null)
        {
            enabled = false;
            return;
        }

        noteTimes = new double[nodes.Count];
        judged = new bool[nodes.Count];

        for (int i = 0; i < nodes.Count; i++)
        {
            noteTimes[i] = nodes[i] != null
                ? chart.bpmTimeline.BeatToSeconds(
                    nodes[i].beat)
                : double.PositiveInfinity;
        }

        nextPendingNodeIndex = Mathf.Clamp(
            player.PlaybackStartNodeIndex + 1,
            0,
            nodes.Count);
        for (int i = 0; i < judged.Length; i++)
            judged[i] = i < nextPendingNodeIndex;

        AdvanceToNextPendingNode();
    }

    public void CaptureInput()
    {
        if (!isActiveAndEnabled ||
            blocked ||
            player == null ||
            player.AutoModeEnabled ||
            !player.AudioHasStarted)
        {
            return;
        }

        if (Keyboard.current != null &&
            Keyboard.current.anyKey.wasPressedThisFrame)
        {
            manualInputQueued = true;
            manualInputSongTime = player.SongTime;
            InputPressed?.Invoke();
            return;
        }

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null)
            return;

        var touches = touchscreen.touches;
        for (int i = 0; i < touches.Count; i++)
        {
            var touch = touches[i];
            if (touch != null && touch.press.wasPressedThisFrame)
            {
                manualInputQueued = true;
                manualInputSongTime = player.SongTime;
                InputPressed?.Invoke();
                return;
            }
        }
    }

    public void Tick(double songTime)
    {
        if (!isActiveAndEnabled || blocked ||
            nodes == null || judged == null || player == null ||
            !player.IsRunning || !player.AudioHasStarted)
            return;

        bool hasInput = manualInputQueued;
        double inputSongTime = manualInputSongTime;
        manualInputQueued = false;

        if (!hasInput &&
            player.AutoModeEnabled &&
            TryGetAutoPlayInput(songTime, out double autoInputSongTime))
        {
            hasInput = true;
            inputSongTime = autoInputSongTime;
        }

        if (hasInput)
            HandlePress(inputSongTime);

        if (blocked)
            return;

        ExpireMissedNotes(songTime);
    }

    /// <summary>
    /// 当前待判定节点已越过正拍但尚未被消费时返回 true。
    /// 用于在节点正拍后取消轨道跟踪，等待玩家在后续判定窗内输入。
    /// </summary>
    public bool TryGetUnresolvedPendingNoteAtOrBefore(
        double songTime,
        out int nodeIndex,
        out double noteSongTime)
    {
        nodeIndex = -1;
        noteSongTime = 0d;

        if (!isActiveAndEnabled || blocked ||
            nodes == null || judged == null || noteTimes == null ||
            nextPendingNodeIndex < 0 ||
            nextPendingNodeIndex >= nodes.Count)
        {
            return false;
        }

        int pendingNodeIndex = nextPendingNodeIndex;
        if (judged[pendingNodeIndex] ||
            !IsJudgeable(pendingNodeIndex))
        {
            return false;
        }

        double pendingNoteSongTime = noteTimes[pendingNodeIndex];
        if (songTime < pendingNoteSongTime)
            return false;

        nodeIndex = pendingNodeIndex;
        noteSongTime = pendingNoteSongTime;
        return true;
    }

    private bool TryGetAutoPlayInput(
        double songTime,
        out double inputSongTime)
    {
        inputSongTime = 0d;
        if (nextPendingNodeIndex < 0 ||
            nextPendingNodeIndex >= noteTimes.Length)
            return false;

        double timeDelta = Math.Clamp(
            player.AutoPlayOffsetSeconds,
            -greatWindowSeconds,
            greatWindowSeconds);
        inputSongTime =
            noteTimes[nextPendingNodeIndex] + timeDelta;
        return songTime >= inputSongTime;
    }

    private void HandlePress(double songTime)
    {
        int noteIndex = nextPendingNodeIndex;
        if (noteIndex < 0)
            return;

        double timeDelta = songTime - noteTimes[noteIndex];
        if (timeDelta < -badWindowSeconds ||
            timeDelta > badWindowSeconds)
            return;

        ResolveNote(
            noteIndex,
            songTime,
            timeDelta);
    }

    private void ResolveNote(
        int noteIndex,
        double songTime,
        double timeDelta,
        JudgementGrade? forcedGrade = null)
    {
        if (!IsValidPendingNode(noteIndex) ||
            noteIndex != nextPendingNodeIndex)
            return;

        judged[noteIndex] = true;
        AdvanceToNextPendingNode();

        double absoluteDelta = Math.Abs(timeDelta);
        JudgementGrade grade;
        if (forcedGrade.HasValue)
            grade = forcedGrade.Value;
        else if (absoluteDelta <= perfectWindowSeconds)
            grade = JudgementGrade.Perfect;
        else if (absoluteDelta <= greatWindowSeconds)
            grade = JudgementGrade.Great;
        else
            grade = JudgementGrade.Bad;

        feedback.ShowJudgement(
            grade,
            GetFeedbackWorldPosition(noteIndex));
        ResolveJudgement(new JudgementResult(
            noteIndex,
            grade,
            timeDelta,
            songTime,
            true));
    }

    private void ExpireMissedNotes(double songTime)
    {
        while (!blocked && nextPendingNodeIndex >= 0)
        {
            int noteIndex = nextPendingNodeIndex;
            if (songTime <= noteTimes[noteIndex] + badWindowSeconds)
                return;

            ResolveNote(
                noteIndex,
                songTime,
                songTime - noteTimes[noteIndex],
                JudgementGrade.Miss);
        }
    }

    public void Pause()
    {
        blocked = true;
        manualInputQueued = false;
    }

    public void ResetFromCheckpoint(int checkpointNodeIndex)
    {
        if (nodes == null || judged == null)
            return;

        int nextIndex = Mathf.Clamp(checkpointNodeIndex + 1, 0, nodes.Count);
        for (int i = 0; i < judged.Length; i++)
            judged[i] = i < nextIndex;

        nextPendingNodeIndex = nextIndex;
        AdvanceToNextPendingNode();
        blocked = false;
        manualInputQueued = false;
    }

    private bool IsValidPendingNode(int nodeIndex)
    {
        return nodes != null && judged != null &&
            nodeIndex >= 0 && nodeIndex < nodes.Count &&
            !judged[nodeIndex] &&
            IsJudgeable(nodeIndex);
    }

    private bool IsJudgeable(int nodeIndex)
    {
        if (nodes[nodeIndex] == null)
            return false;

        return nodes[nodeIndex].IsManuallyTriggered;
    }

    private void AdvanceToNextPendingNode()
    {
        if (nodes == null || judged == null)
        {
            nextPendingNodeIndex = -1;
            return;
        }

        nextPendingNodeIndex = Mathf.Clamp(
            nextPendingNodeIndex,
            0,
            nodes.Count);

        while (nextPendingNodeIndex < nodes.Count &&
               (judged[nextPendingNodeIndex] ||
                !IsJudgeable(nextPendingNodeIndex)))
        {
            nextPendingNodeIndex++;
        }

        if (nextPendingNodeIndex >= nodes.Count)
            nextPendingNodeIndex = -1;
    }

    private void ResolveJudgement(JudgementResult result)
    {
        if (!result.HasNote || nodes == null ||
            result.NoteIndex >= nodes.Count ||
            nodes[result.NoteIndex] == null)
        {
            Debug.LogError(
                $"[判定] 已拒绝无节点判定结果：{result.Grade}",
                this);
            return;
        }

        Debug.Log(
            $"[判定] 节点 {result.NoteIndex} " +
            $"(Beat {nodes[result.NoteIndex].beat:0.###}) | " +
            $"{result.Grade} | {FormatTimingDelta(result.TimeDelta)}");

        JudgementResolved?.Invoke(result);
    }

    private static string FormatTimingDelta(double timeDelta)
    {
        long milliseconds = (long)Math.Round(
            timeDelta * 1000d,
            MidpointRounding.AwayFromZero);
        string sign = milliseconds > 0L ? "+" : string.Empty;
        return $"{sign}{milliseconds}ms";
    }

    private Vector3 GetFeedbackWorldPosition(int noteIndex)
    {
        Vector3 playerPosition = player != null
            ? player.transform.position
            : transform.position;
        if (nodes == null || noteIndex < 0 ||
            noteIndex >= nodes.Count || nodes[noteIndex] == null)
            return playerPosition;

        Vector3 nodePosition = (Vector3)nodes[noteIndex].position;
        return (playerPosition + nodePosition) * 0.5f;
    }
}
