using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum JudgementGrade
{
    Perfect,
    Great,
    Bad,
    Miss
}

[RequireComponent(typeof(Player))]
public class BasicJudgement : MonoBehaviour
{
    [SerializeField, Range(0.02f, 0.2f)]
    private float perfectWindowSeconds = 0.06f;

    [SerializeField, Range(0.04f, 0.3f)]
    private float greatWindowSeconds = 0.14f;

    private Player player;
    private BasicJudgementFeedback feedback;
    private IReadOnlyList<NodeData> nodes;
    private double[] noteTimes;
    private bool[] judged;
    private int expiryIndex;

    private Camera worldCamera;

    void Awake()
    {
        player = GetComponent<Player>();
        feedback = GetComponentInChildren<BasicJudgementFeedback>(true);

        if (feedback == null)
        {
            var feedbackObject = new GameObject("JudgementFeedback");
            feedbackObject.transform.SetParent(transform, false);
            feedback = feedbackObject.AddComponent<BasicJudgementFeedback>();
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

        worldCamera = Camera.main;
    }

    void Update()
    {
        if (nodes == null || judged == null || player == null || !player.IsRunning)
            return;

        double songTime = player.SongTime;
        ExpireMissedNotes(songTime);

        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            HandlePress(songTime, null);

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null)
            return;

        var touches = touchscreen.touches;
        for (int i = 0; i < touches.Count; i++)
        {
            var touch = touches[i];
            if (touch != null && touch.press.wasPressedThisFrame)
                HandlePress(songTime, touch.position.ReadValue());
        }
    }

    private void HandlePress(double songTime, Vector2? pressPosition)
    {
        int noteIndex = FindClosestPendingNote(songTime, out double timeDelta);
        Vector2 effectPosition = pressPosition ?? GetFeedbackScreenPosition(noteIndex);

        if (noteIndex < 0)
        {
            feedback.ShowJudgement(JudgementGrade.Bad, effectPosition);
            return;
        }

        judged[noteIndex] = true;
        JudgementGrade grade = Math.Abs(timeDelta) <= perfectWindowSeconds
            ? JudgementGrade.Perfect
            : JudgementGrade.Great;

        if (pressPosition.HasValue)
            feedback.ShowJudgement(grade, effectPosition);

        feedback.ShowJudgement(grade, GetFeedbackScreenPosition(noteIndex));
    }

    private int FindClosestPendingNote(double songTime, out double closestDelta)
    {
        int bestIndex = -1;
        double bestDistance = double.PositiveInfinity;
        closestDelta = 0d;

        for (int i = expiryIndex; i < noteTimes.Length; i++)
        {
            if (judged[i])
                continue;

            double delta = songTime - noteTimes[i];
            if (delta < -greatWindowSeconds)
                break;

            if (delta > greatWindowSeconds)
                continue;

            double distance = Math.Abs(delta);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
                closestDelta = delta;
            }
        }

        return bestIndex;
    }

    private void ExpireMissedNotes(double songTime)
    {
        while (expiryIndex < noteTimes.Length)
        {
            if (judged[expiryIndex])
            {
                expiryIndex++;
                continue;
            }

            if (songTime <= noteTimes[expiryIndex] + greatWindowSeconds)
                break;

            judged[expiryIndex] = true;
            feedback.ShowJudgement(JudgementGrade.Miss, GetFeedbackScreenPosition(expiryIndex));
            expiryIndex++;
        }
    }

    private Vector2 GetFeedbackScreenPosition(int noteIndex)
    {
        if (noteIndex >= 0 && worldCamera != null)
        {
            Vector3 position = (Vector3)nodes[noteIndex].position;
            Vector3 screenPosition = worldCamera.WorldToScreenPoint(position);

            if (screenPosition.z >= 0f)
            {
                screenPosition.x = Mathf.Clamp(screenPosition.x, 80f, Screen.width - 80f);
                screenPosition.y = Mathf.Clamp(screenPosition.y, 100f, Screen.height - 100f);
                return screenPosition;
            }
        }

        return new Vector2(Screen.width * 0.5f, Screen.height * 0.62f);
    }
}
