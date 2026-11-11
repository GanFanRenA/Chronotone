using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct MotionTimelinePoint
{
    public MotionTimelinePoint(double time, Vector2 position, int noteIndex)
    {
        Time = time;
        Position = position;
        NoteIndex = noteIndex;
    }

    public double Time { get; }
    public Vector2 Position { get; }
    public int NoteIndex { get; }
    public bool IsStartPoint => NoteIndex < 0;
}

/// <summary>
/// 将歌曲时间映射到玩家位置。起点使用游戏开始位置，
/// 后续点来自谱面 Node；查询只依赖绝对歌曲时间。
/// </summary>
public sealed class ChartMotionTimeline
{
    private readonly MotionTimelinePoint[] points;
    private int currentIndex;

    public ChartMotionTimeline(
        Vector2 originalPosition,
        double playbackStartSongTime,
        IReadOnlyList<NodeData> nodes,
        BpmTimeline bpmTimeline)
    {
        var builtPoints = new List<MotionTimelinePoint>(
            (nodes?.Count ?? 0) + 1)
        {
            new MotionTimelinePoint(playbackStartSongTime, originalPosition, -1)
        };

        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                double noteTime = bpmTimeline.BeatToSeconds(nodes[i].beat);
                if (noteTime <= playbackStartSongTime)
                    continue;

                builtPoints.Add(new MotionTimelinePoint(
                    noteTime,
                    nodes[i].position,
                    i));
            }
        }

        points = builtPoints.ToArray();
    }

    public IReadOnlyList<MotionTimelinePoint> Points => points;
    public int PointCount => points.Length;

    public Vector2 Sample(double songTime)
    {
        if (points.Length == 0)
            return Vector2.zero;

        if (songTime <= points[0].Time)
            return points[0].Position;

        while (currentIndex < points.Length - 1 &&
               songTime >= points[currentIndex + 1].Time)
        {
            currentIndex++;
        }

        if (currentIndex >= points.Length - 1)
            return points[points.Length - 1].Position;

        MotionTimelinePoint from = points[currentIndex];
        MotionTimelinePoint to = points[currentIndex + 1];
        double span = to.Time - from.Time;

        if (span <= 0d)
            return to.Position;

        double progress = Math.Clamp((songTime - from.Time) / span, 0d, 1d);
        return Vector2.Lerp(from.Position, to.Position, (float)progress);
    }
}
