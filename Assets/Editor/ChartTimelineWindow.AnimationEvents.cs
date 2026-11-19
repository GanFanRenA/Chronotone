using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ChartTimelineWindow
{
    private const float MinAnimationLaneHeight = 60f;
    private const float AnimationLaneHeaderHeight = 16f;
    private const float AnimationRowHeight = 9f;
    private const float AnimationDotSize = 7f;
    private const float AnimationBarThickness = 4f;
    private const float AnimationBeatEpsilon = 0.0001f;
    private const float AnimationLanePaddingY = 3f;
    private const float AnimationScrollbarWidth = 12f;
    private const float AnimationWheelScrollSpeed = 40f;
    private const float AnimationDragThreshold = 3f;

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
    private string cachedAnimationScenePath = string.Empty;
    private bool animationEventsDirty = true;

    [SerializeField] private float animationScrollY;
    private float animationContentHeight;
    private float animationMaxScrollY;

    private bool animDragActive;
    private bool animDragged;
    private float animDragMouseStartY;
    private float animDragStartScrollY;
    private PositionedAnimationEvent animPendingClickHit;

    private static Texture2D animationDotTexture;

    private sealed class PositionedAnimationEvent
    {
        public StageAnimationEvent Event;
        public int Row;
        public float X;
        public float EndX;
        public float CenterY;
        public Rect DotRect;
        public Rect EndDotRect;
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

    private static Rect AnimationContentRect(Rect laneRect)
    {
        return new Rect(
            laneRect.x,
            laneRect.y + AnimationLaneHeaderHeight,
            laneRect.width - AnimationScrollbarWidth,
            laneRect.height - AnimationLaneHeaderHeight);
    }

    private void BuildAnimationLayout(Rect laneRect, List<StageAnimationEvent> events)
    {
        positionedAnimationEvents.Clear();
        if (events == null || events.Count == 0)
        {
            animationContentHeight = 0f;
            animationMaxScrollY = 0f;
            animationScrollY = 0f;
            return;
        }

        Rect content = AnimationContentRect(laneRect);

        // 分区：区间与点分开处理，保持各自按 beat 排序。
        var spans = new List<StageAnimationEvent>();
        var dots = new List<StageAnimationEvent>();
        foreach (StageAnimationEvent evt in events)
        {
            if (evt.IsSpan) spans.Add(evt);
            else dots.Add(evt);
        }

        // 区间：贪婪区间着色，重叠的才分到不同行；不重叠可共用一行。
        var spanRowEnds = new List<float>();
        foreach (StageAnimationEvent span in spans)
        {
            int row = 0;
            while (row < spanRowEnds.Count && spanRowEnds[row] > span.beat)
                row++;

            if (row == spanRowEnds.Count)
                spanRowEnds.Add(span.endBeat);
            else
                spanRowEnds[row] = span.endBeat;

            AddPositionedEvent(span, row);
        }
        int spanRowCount = spanRowEnds.Count;

        // 点：按 beat 分组，同一 beat 从区间行之后开始纵向堆叠。
        int maxDotGroupSize = 0;
        int i = 0;
        while (i < dots.Count)
        {
            float groupBeat = dots[i].beat;
            int j = i;
            while (j < dots.Count &&
                   Mathf.Abs(dots[j].beat - groupBeat) <= AnimationBeatEpsilon)
            {
                j++;
            }

            int groupCount = j - i;
            if (groupCount > maxDotGroupSize)
                maxDotGroupSize = groupCount;

            for (int k = 0; k < groupCount; k++)
                AddPositionedEvent(dots[i + k], spanRowCount + k);

            i = j;
        }

        int totalRows = Mathf.Max(1, spanRowCount + maxDotGroupSize);
        float viewportHeight = content.height;
        animationContentHeight =
            AnimationLanePaddingY * 2f + totalRows * AnimationRowHeight;
        animationMaxScrollY =
            Mathf.Max(0f, animationContentHeight - viewportHeight);
        animationScrollY = Mathf.Clamp(animationScrollY, 0f, animationMaxScrollY);

        // 回填屏幕坐标（依赖最终 animationScrollY）。
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            p.X = content.x + (p.Event.beat - scrollBeat) * pixelsPerBeat;
            p.EndX = p.Event.IsSpan
                ? content.x + (p.Event.endBeat - scrollBeat) * pixelsPerBeat
                : p.X;
            p.CenterY = content.y + AnimationLanePaddingY - animationScrollY +
                p.Row * AnimationRowHeight + AnimationRowHeight * 0.5f;
            p.DotRect = new Rect(
                p.X - AnimationDotSize * 0.5f,
                p.CenterY - AnimationDotSize * 0.5f,
                AnimationDotSize,
                AnimationDotSize);
            p.EndDotRect = p.Event.IsSpan
                ? new Rect(
                    p.EndX - AnimationDotSize * 0.5f,
                    p.CenterY - AnimationDotSize * 0.5f,
                    AnimationDotSize,
                    AnimationDotSize)
                : p.DotRect;
        }
    }

    private void AddPositionedEvent(StageAnimationEvent evt, int row)
    {
        positionedAnimationEvents.Add(new PositionedAnimationEvent
        {
            Event = evt,
            Row = row
        });
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

        Rect content = AnimationContentRect(rect);

        // 先画所有区间带。
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (!p.Event.IsSpan || !IsAnimationEventVisible(p, content))
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

        // 再画点（区间两端的小点 + 独立事件点）。
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (!IsAnimationEventVisible(p, content))
                continue;

            Color c = ColorForKind(p.Event.kind);
            if (p.Event.IsSpan)
            {
                DrawDot(p.DotRect, c);
                DrawDot(p.EndDotRect, c);
                string tooltip = TooltipFor(p.Event);
                GUI.Label(p.DotRect, new GUIContent(string.Empty, tooltip));
                GUI.Label(p.EndDotRect, new GUIContent(string.Empty, tooltip));
            }
            else
            {
                DrawDot(p.DotRect, c);
                GUI.Label(p.DotRect, new GUIContent(string.Empty, TooltipFor(p.Event)));
            }
        }

        DrawAnimationScrollbar(rect);
    }

    private void DrawAnimationScrollbar(Rect laneRect)
    {
        if (animationContentHeight <= 0f || animationMaxScrollY <= 0f)
            return;

        Rect content = AnimationContentRect(laneRect);
        Rect track = new Rect(
            laneRect.xMax - AnimationScrollbarWidth,
            content.y,
            AnimationScrollbarWidth,
            content.height);

        EditorGUI.DrawRect(track, new Color(0.09f, 0.09f, 0.09f));

        float ratio = content.height / animationContentHeight;
        float thumbHeight = Mathf.Max(16f, content.height * ratio);
        float thumbTravel = Mathf.Max(0f, content.height - thumbHeight);
        float thumbY = track.y +
            thumbTravel * (animationScrollY / animationMaxScrollY);
        EditorGUI.DrawRect(
            new Rect(track.x + 1f, thumbY, track.width - 2f, thumbHeight),
            new Color(0.42f, 0.42f, 0.45f));
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

    private static bool IsAnimationEventVisible(PositionedAnimationEvent p, Rect content)
    {
        float verticalMargin = AnimationDotSize;
        if (p.CenterY < content.y - verticalMargin ||
            p.CenterY > content.yMax + verticalMargin)
        {
            return false;
        }

        float left = Mathf.Min(p.X, p.EndX);
        float right = Mathf.Max(p.X, p.EndX);
        return right >= content.x - 20f && left <= content.xMax + 20f;
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
        Rect content = AnimationContentRect(laneRect);
        Rect scrollArea = new Rect(
            laneRect.x,
            laneRect.y + AnimationLaneHeaderHeight,
            laneRect.width,
            laneRect.height - AnimationLaneHeaderHeight);

        // 滚轮：ctrl 缩放（与节点轨道一致），否则上下滚动动画内容。
        if (e.type == EventType.ScrollWheel && scrollArea.Contains(e.mousePosition))
        {
            if (e.control || e.command)
            {
                float mouseBeat =
                    scrollBeat + (e.mousePosition.x - content.x) / pixelsPerBeat;
                pixelsPerBeat = Mathf.Clamp(
                    pixelsPerBeat * Mathf.Pow(1.1f, -e.delta.y),
                    MinPixelsPerBeat,
                    MaxPixelsPerBeat);
                scrollBeat = Mathf.Max(
                    MinScrollBeat,
                    mouseBeat - (e.mousePosition.x - content.x) / pixelsPerBeat);
            }
            else
            {
                ScrollAnimationVertically(e.delta.y * AnimationWheelScrollSpeed);
            }
            e.Use();
            RequestRepaint();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 &&
            scrollArea.Contains(e.mousePosition))
        {
            animDragActive = true;
            animDragged = false;
            animDragMouseStartY = e.mousePosition.y;
            animDragStartScrollY = animationScrollY;
            animPendingClickHit = FindAnimationHit(e.mousePosition);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0 && animDragActive)
        {
            float deltaY = e.mousePosition.y - animDragMouseStartY;
            if (Mathf.Abs(deltaY) > AnimationDragThreshold)
                animDragged = true;

            if (animDragged)
            {
                animationScrollY = Mathf.Clamp(
                    animDragStartScrollY - deltaY,
                    0f,
                    animationMaxScrollY);
            }
            e.Use();
            RequestRepaint();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0 && animDragActive)
        {
            animDragActive = false;
            if (!animDragged && animPendingClickHit != null)
                SelectAnimationOwner(animPendingClickHit);

            animPendingClickHit = null;
            animDragged = false;
            e.Use();
            RequestRepaint();
            return;
        }
    }

    private void ScrollAnimationVertically(float delta)
    {
        animationScrollY = Mathf.Clamp(
            animationScrollY + delta,
            0f,
            animationMaxScrollY);
    }

    private void SelectAnimationOwner(PositionedAnimationEvent hit)
    {
        Object owner = hit.Event.owner;
        if (owner is GameObject gameObject)
            Selection.activeGameObject = gameObject;
        else if (owner != null)
            Selection.activeObject = owner;

        if (owner != null)
            EditorGUIUtility.PingObject(owner);
    }

    private PositionedAnimationEvent FindAnimationHit(Vector2 mouse)
    {
        const float threshold = 6f;

        // 区间带优先命中。
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (!p.Event.IsSpan)
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
