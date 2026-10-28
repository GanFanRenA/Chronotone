using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BpmSegment
{
    public float beat;      // 该段从第几拍开始，第一段必须是 0
    public float bpm = 120f;
    [Tooltip("每个 beat 沿 X 轴跨越的格数；当前每格为 1 个世界单位。")]
    public float speed = 5f;
}

[Serializable]
public class BpmTimeline
{
    public List<BpmSegment> segments = new List<BpmSegment>
    {
        new BpmSegment { beat = 0f, bpm = 120f, speed = 5f }
    };

    [NonSerialized] private float[] distancePrefixBySegment;

    public BpmSegment GetSegmentAtBeat(float beat)
    {
        if (segments == null || segments.Count == 0)
            return null;

        BpmSegment result = segments[0];
        for (int i = 1; i < segments.Count; i++)
        {
            if (beat < segments[i].beat) break;
            result = segments[i];
        }
        return result;
    }

    public float SpeedAtBeat(float beat)
    {
        BpmSegment segment = GetSegmentAtBeat(beat);
        return segment != null ? segment.speed : 0f;
    }

    public void RebuildDistanceCache()
    {
        if (segments == null || segments.Count == 0)
        {
            distancePrefixBySegment = Array.Empty<float>();
            return;
        }

        if (distancePrefixBySegment == null ||
            distancePrefixBySegment.Length != segments.Count)
        {
            distancePrefixBySegment = new float[segments.Count];
        }

        distancePrefixBySegment[0] = 0f;
        for (int i = 0; i < segments.Count - 1; i++)
        {
            BpmSegment current = segments[i];
            BpmSegment next = segments[i + 1];
            if (current == null || next == null)
            {
                distancePrefixBySegment[i + 1] = distancePrefixBySegment[i];
                continue;
            }

            float beatSpan = Mathf.Max(0f, next.beat - current.beat);
            distancePrefixBySegment[i + 1] =
                distancePrefixBySegment[i] +
                Mathf.Max(0f, current.speed) * beatSpan;
        }
    }

    // 从 beat 0 开始的累计世界坐标距离。
    public float DistanceAtBeat(float beat)
    {
        if (segments == null || segments.Count == 0)
        {
            return Mathf.Max(0f, beat) * 5f;
        }

        if (distancePrefixBySegment == null ||
            distancePrefixBySegment.Length != segments.Count)
        {
            RebuildDistanceCache();
        }

        beat = Mathf.Max(0f, beat);
        int segmentIndex = FindSegmentIndex(beat);
        BpmSegment segment = segments[segmentIndex];
        if (segment == null)
            return distancePrefixBySegment[segmentIndex];

        return distancePrefixBySegment[segmentIndex] +
            Mathf.Max(0f, segment.speed) *
            Mathf.Max(0f, beat - segment.beat);
    }

    public float DistanceBetweenBeats(float fromBeat, float toBeat)
    {
        float firstBeat = Mathf.Max(0f, Mathf.Min(fromBeat, toBeat));
        float lastBeat = Mathf.Max(firstBeat, toBeat);
        return DistanceAtBeat(lastBeat) - DistanceAtBeat(firstBeat);
    }

    private int FindSegmentIndex(float beat)
    {
        int low = 0;
        int high = segments.Count - 1;
        int result = 0;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            BpmSegment segment = segments[middle];
            float segmentBeat = segment != null ? segment.beat : 0f;
            if (segmentBeat <= beat)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return result;
    }

    // 逻辑拍 → 音频时间（秒）
    public float BeatToSeconds(float beat)
    {
        if (segments == null || segments.Count == 0)
            return beat * 60f / 120f;

        float seconds = 0f;
        float prevBeat = segments[0].beat;
        float prevBpm = segments[0].bpm;

        for (int i = 1; i < segments.Count; i++)
        {
            BpmSegment seg = segments[i];
            if (beat <= seg.beat) break;

            seconds += (seg.beat - prevBeat) * 60f / prevBpm;
            prevBeat = seg.beat;
            prevBpm = seg.bpm;
        }

        seconds += (beat - prevBeat) * 60f / prevBpm;
        return seconds;
    }

    // 音频时间（秒） → 逻辑拍
    public float SecondsToBeat(float seconds)
    {
        if (segments == null || segments.Count == 0)
            return seconds * 2f;

        float acc = 0f;
        for (int i = 0; i < segments.Count; i++)
        {
            BpmSegment seg = segments[i];
            if (seg == null)
                continue;

            float nextBeat = (i + 1 < segments.Count &&
                              segments[i + 1] != null)
                ? segments[i + 1].beat
                : float.MaxValue;

            if (nextBeat == float.MaxValue)
                return seg.beat + (seconds - acc) * seg.bpm / 60f;

            float segmentDuration =
                (nextBeat - seg.beat) * 60f / seg.bpm;
            if (acc + segmentDuration >= seconds)
                return seg.beat + (seconds - acc) * seg.bpm / 60f;

            acc += segmentDuration;
        }

        BpmSegment last = segments[segments.Count - 1];
        return last != null
            ? last.beat + (seconds - acc) * last.bpm / 60f
            : seconds * 2f;
    }
}
