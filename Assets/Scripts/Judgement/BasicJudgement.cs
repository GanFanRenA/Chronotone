using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BasicJudgement : MonoBehaviour
{
    [SerializeField, Range(0.02f, 0.2f)]
    private float perfectWindowSeconds = 0.06f;

    [SerializeField, Range(0.04f, 0.3f)]
    private float greatWindowSeconds = 0.14f;

    [SerializeField, Range(0.16f, 0.4f)]
    private float badWindowSeconds = 0.24f;

    private Player player;
    private BasicJudgementFeedback feedback;
    private IReadOnlyList<NodeData> nodes;
    private double[] noteTimes;
    private bool[] judged;
    private int nextPendingNodeIndex;
    private bool blocked;
    public float GreatWindowSeconds => greatWindowSeconds;

    public event Action<JudgementResult> JudgementResolved;

    void OnValidate()
    {
        greatWindowSeconds = Mathf.Max(perfectWindowSeconds, greatWindowSeconds);
        badWindowSeconds = Mathf.Max(greatWindowSeconds, badWindowSeconds);
    }

    void Awake()
    {
        player = GetComponent<Player>();
        feedback = GetComponentInChildren<BasicJudgementFeedback>(true);

        if (feedback == null)
        {
            Debug.LogError("BasicJudgement 缺少 BasicJudgementFeedback 子对象", this);
            enabled = false;
        }
    }

    void Start()
    {
        if (player == null || player.chartPlayer == null || player.chartPlayer.chart == null)
        {
            enabled = false;
            return;
        }

        nodes = player.chartPlayer.chart.nodes;
        if (nodes == null || nodes.Count == 0)
        {
            enabled = false;
            return;
        }

        noteTimes = new double[nodes.Count];
        judged = new bool[nodes.Count];

        for (int i = 0; i < nodes.Count; i++)
            noteTimes[i] = player.chartPlayer.chart.bpmTimeline.BeatToSeconds(nodes[i].beat);

        nextPendingNodeIndex = Mathf.Clamp(
            player.PlaybackStartNodeIndex + 1,
            0,
            nodes.Count);
        for (int i = 0; i < judged.Length; i++)
            judged[i] = i < nextPendingNodeIndex;

        AdvanceToNextPendingNode();
    }

    public void Tick(double songTime)
    {
        if (!isActiveAndEnabled || blocked ||
            nodes == null || judged == null || player == null ||
            !player.IsRunning || !player.AudioHasStarted)
            return;

        if (player.AutoModeEnabled)
        {
            TickAutoPlay(songTime);
            return;
        }

        ExpireMissedNotes(songTime);
        if (blocked)
            return;

        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            HandlePress(songTime);

        if (blocked)
            return;

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null)
            return;

        var touches = touchscreen.touches;
        for (int i = 0; i < touches.Count; i++)
        {
            var touch = touches[i];
            if (touch != null && touch.press.wasPressedThisFrame)
            {
                HandlePress(songTime);
                if (blocked)
                    return;
            }
        }
    }

    private void TickAutoPlay(double songTime)
    {
        while (!blocked && nextPendingNodeIndex >= 0)
        {
            int nodeIndex = nextPendingNodeIndex;
            double timeDelta = Math.Clamp(
                player.AutoPlayOffsetSeconds,
                -greatWindowSeconds,
                greatWindowSeconds);
            double autoPlaySongTime = noteTimes[nodeIndex] + timeDelta;
            if (songTime < autoPlaySongTime)
                return;

            player.RequestNodeAction(nodes[nodeIndex].mode);
            ResolveNote(
                nodeIndex,
                autoPlaySongTime,
                timeDelta);
        }
    }

    private void HandlePress(double songTime)
    {
        int noteIndex = nextPendingNodeIndex;
        if (noteIndex < 0)
            return;

        player.RequestNodeAction(nodes[noteIndex].mode);

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
        NodeMode mode = nodes[nodeIndex].mode;
        return mode == NodeMode.JumpNode ||
            mode == NodeMode.GravityNode ||
            mode == NodeMode.FastFallNode ||
            mode == NodeMode.GravityFastFallNode ||
            mode == NodeMode.WeakGravityNode ||
            mode == NodeMode.GravityJumpNode;
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
            result.NoteIndex >= nodes.Count)
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
