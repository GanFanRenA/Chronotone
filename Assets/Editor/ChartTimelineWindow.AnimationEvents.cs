using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ChartTimelineWindow
{
    private const float AnimationLaneHeight = 72f;
    private const float AnimationLaneHeaderHeight = 16f;
    private const float AnimationRowHeight = 9f;
    private const float AnimationDotSize = 7f;
    private const float AnimationBarThickness = 4f;
    private const float AnimationBeatEpsilon = 0.0001f;
    private const float AnimationLanePaddingY = 3f;

    private static readonly Color LayerVisibilityColor =
        new Color(0.45f, 0.85f, 1f);
    private static readonly Color FadeColor =
        new Color(0.35f, 0.95f, 0.55f);
    private static readonly Color FlyColor =
        new Color(1f, 0.7f, 0.25f);

    private readonly List<StageAnimationEvent> cachedAnimationEvents =
        new List<StageAnimationEvent>();
    private readonly List<PositionedAnimationEvent> positionedAnimationEvents =
        new List<PositionedAnimationEvent>();
    private readonly List<OverflowMarker> overflowMarkers =
        new List<OverflowMarker>();
    private string cachedAnimationScenePath = string.Empty;
    private bool animationEventsDirty = true;

    private static Texture2D animationDotTexture;

    private sealed class PositionedAnimationEvent
    {
        public StageAnimationEvent Event;
        public float X;
        public float EndX;
        public float CenterY;
        public Rect DotRect;
        public Rect EndDotRect;
        public bool Overflow;
    }

    private sealed class OverflowMarker
    {
        public float X;
        public float Y;
        public int Count;
    }

    // ---- 事件收集 ----

    private List<StageAnimationEvent> GetAnimationEvents()
    {
        if (targetPlayer == null)
        {
            cachedAnimationEvents.Clear();
            return cachedAnimationEvents;
        }

        string scenePath = targetPlayer.gameObject.scene.IsValid()
            ? targetPlayer.gameObject.scene.path
            : string.Empty;

        if (animationEventsDirty || cachedAnimationScenePath != scenePath)
        {
            cachedAnimationScenePath = scenePath;
            cachedAnimationEvents.Clear();
            CollectAnimationEventsFromScene(cachedAnimationEvents);
            SortAnimationEvents(cachedAnimationEvents);
            animationEventsDirty = false;
        }

        return cachedAnimationEvents;
    }

    private void CollectAnimationEventsFromScene(List<StageAnimationEvent> events)
    {
        Scene scene = targetPlayer.gameObject.scene;
        if (!scene.IsValid())
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            if (root == null)
                continue;

            MonoBehaviour[] behaviours =
                root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IStageAnimationEventSource source)
                    source.CollectAnimationEvents(events);
            }
        }
    }

    private static void SortAnimationEvents(List<StageAnimationEvent> events)
    {
        events.Sort((a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            if (byBeat != 0)
                return byBeat;

            int byKind = a.kind.CompareTo(b.kind);
            if (byKind != 0)
                return byKind;

            return string.CompareOrdinal(a.label, b.label);
        });
    }

    // ---- 布局 ----

    private void BuildAnimationLayout(Rect laneRect, List<StageAnimationEvent> events)
    {
        positionedAnimationEvents.Clear();
        overflowMarkers.Clear();
        if (events == null || events.Count == 0)
            return;

        Rect content = new Rect(
            laneRect.x,
            laneRect.y + AnimationLaneHeaderHeight,
            laneRect.width,
            laneRect.height - AnimationLaneHeaderHeight);

        int maxRows = Mathf.Max(
            1,
            Mathf.FloorToInt(
                (content.height - AnimationLanePaddingY * 2f) /
                AnimationRowHeight));
        float firstCenterY =
            content.y + AnimationLanePaddingY + AnimationRowHeight * 0.5f;

        int i = 0;
        while (i < events.Count)
        {
            float groupBeat = events[i].beat;
            int j = i;
            while (j < events.Count &&
                   Mathf.Abs(events[j].beat - groupBeat) <= AnimationBeatEpsilon)
            {
                j++;
            }

            int groupCount = j - i;
            float groupX = content.x + (groupBeat - scrollBeat) * pixelsPerBeat;

            for (int k = 0; k < groupCount; k++)
            {
                StageAnimationEvent evt = events[i + k];
                var p = new PositionedAnimationEvent
                {
                    Event = evt,
                    X = groupX,
                    CenterY = firstCenterY +
                        Mathf.Min(k, maxRows - 1) * AnimationRowHeight,
                    Overflow = k >= maxRows
                };
                p.EndX = evt.IsSpan
                    ? content.x + (evt.endBeat - scrollBeat) * pixelsPerBeat
                    : p.X;
                p.DotRect = new Rect(
                    p.X - AnimationDotSize * 0.5f,
                    p.CenterY - AnimationDotSize * 0.5f,
                    AnimationDotSize,
                    AnimationDotSize);
                p.EndDotRect = evt.IsSpan
                    ? new Rect(
                        p.EndX - AnimationDotSize * 0.5f,
                        p.CenterY - AnimationDotSize * 0.5f,
                        AnimationDotSize,
                        AnimationDotSize)
                    : p.DotRect;
                positionedAnimationEvents.Add(p);
            }

            if (groupCount > maxRows)
            {
                overflowMarkers.Add(new OverflowMarker
                {
                    X = groupX,
                    Y = content.yMax - AnimationLanePaddingY,
                    Count = groupCount - maxRows
                });
            }

            i = j;
        }
    }

    // ---- 绘制 ----

    private void DrawAnimationLane(Rect rect, List<StageAnimationEvent> events)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.14f));
        DrawAnimationLegend(new Rect(rect.x, rect.y, rect.width, AnimationLaneHeaderHeight));
        EditorGUI.DrawRect(
            new Rect(rect.x, rect.y + AnimationLaneHeaderHeight - 1, rect.width, 1),
            new Color(0.3f, 0.3f, 0.3f));

        BuildAnimationLayout(rect, events);

        // 先画区间带，再画点，保证点落在区间之上。
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (!p.Event.IsSpan || p.Overflow)
                continue;

            Color c = ColorForKind(p.Event.kind);
            Color bar = new Color(c.r, c.g, c.b, 0.35f);
            float left = Mathf.Min(p.X, p.EndX);
            float right = Mathf.Max(p.X, p.EndX);
            EditorGUI.DrawRect(
                new Rect(
                    left,
                    p.CenterY - AnimationBarThickness * 0.5f,
                    right - left,
                    AnimationBarThickness),
                bar);
        }

        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (p.Overflow)
                continue;

            Color c = ColorForKind(p.Event.kind);
            DrawDot(p.DotRect, c);
            if (p.Event.IsSpan)
                DrawDot(p.EndDotRect, c);

            string tooltip = TooltipFor(p.Event);
            GUI.Label(p.DotRect, new GUIContent(string.Empty, tooltip));
            if (p.Event.IsSpan)
                GUI.Label(p.EndDotRect, new GUIContent(string.Empty, tooltip));
        }

        foreach (OverflowMarker marker in overflowMarkers)
        {
            GUI.Label(
                new Rect(marker.X + 3f, marker.Y - 12f, 40f, 14f),
                $"+{marker.Count}",
                EditorStyles.miniLabel);
        }
    }

    private void DrawAnimationLegend(Rect header)
    {
        float y = header.y + 2f;
        DrawLegendItem(4f, y, LayerVisibilityColor, "显隐", 34f);
        DrawLegendItem(46f, y, FadeColor, "淡入/淡出", 64f);
        DrawLegendItem(122f, y, FlyColor, "飞入/飞出", 64f);
    }

    private void DrawLegendItem(float x, float y, Color color, string text, float labelWidth)
    {
        DrawDot(new Rect(x, y + 4f, 6f, 6f), color);
        GUI.Label(new Rect(x + 9f, y, labelWidth, 14f), text, EditorStyles.miniLabel);
    }

    private static void DrawDot(Rect rect, Color color)
    {
        Texture2D texture = EnsureAnimationDotTexture();
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, texture);
        GUI.color = previous;
    }

    private static Color ColorForKind(StageAnimationEventKind kind)
    {
        switch (kind)
        {
            case StageAnimationEventKind.Fade:
                return FadeColor;
            case StageAnimationEventKind.Fly:
                return FlyColor;
            default:
                return LayerVisibilityColor;
        }
    }

    private static string TooltipFor(StageAnimationEvent evt)
    {
        string ownerName = evt.owner != null ? evt.owner.name : string.Empty;
        string beatText = evt.IsSpan
            ? $"beat {evt.beat:0.###} → {evt.endBeat:0.###}"
            : $"beat {evt.beat:0.###}";
        return string.IsNullOrEmpty(ownerName)
            ? $"{evt.label} · {beatText}"
            : $"{evt.label} · {ownerName} · {beatText}";
    }

    private static Texture2D EnsureAnimationDotTexture()
    {
        if (animationDotTexture != null)
            return animationDotTexture;

        const int size = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "ChartTimeline.AnimationDot",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float center = size * 0.5f - 0.5f;
        float radius = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        animationDotTexture = texture;
        return texture;
    }

    // ---- 交互 ----

    private void HandleAnimationInput(Rect laneRect)
    {
        Event e = Event.current;

        if (e.type != EventType.MouseDown || e.button != 0 ||
            !laneRect.Contains(e.mousePosition))
        {
            return;
        }

        PositionedAnimationEvent hit = FindAnimationHit(e.mousePosition);
        if (hit == null)
            return;

        Object owner = hit.Event.owner;
        if (owner is GameObject gameObject)
            Selection.activeGameObject = gameObject;
        else if (owner != null)
            Selection.activeObject = owner;

        if (owner != null)
            EditorGUIUtility.PingObject(owner);

        e.Use();
        RequestRepaint();
    }

    private PositionedAnimationEvent FindAnimationHit(Vector2 mouse)
    {
        const float threshold = 6f;

        // 区间带优先命中。
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (p.Overflow || !p.Event.IsSpan)
                continue;

            Rect bar = new Rect(
                Mathf.Min(p.X, p.EndX),
                p.CenterY - AnimationBarThickness * 0.5f - 2f,
                Mathf.Abs(p.EndX - p.X),
                AnimationBarThickness + 4f);
            if (bar.Contains(mouse))
                return p;
        }

        PositionedAnimationEvent best = null;
        float bestDistance = threshold;
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (p.Overflow)
                continue;

            float distance = DistanceToRect(mouse, p.DotRect);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = p;
            }

            if (p.Event.IsSpan)
            {
                float endDistance = DistanceToRect(mouse, p.EndDotRect);
                if (endDistance < bestDistance)
                {
                    bestDistance = endDistance;
                    best = p;
                }
            }
        }

        return best;
    }

    private static float DistanceToRect(Vector2 point, Rect rect)
    {
        float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
        float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
