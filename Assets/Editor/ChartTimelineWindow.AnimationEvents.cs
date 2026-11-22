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

    private float animDragMouseStartX;
    private bool animMoveCandidate;
    private bool animMoveDragged;
    private float animMoveMouseStartBeat;
    private float animMoveMinBeat;
    private readonly List<StageAnimationEvent> animationSelection =
        new List<StageAnimationEvent>();
    private readonly List<AnimationComponentTarget> animationMoveTargets =
        new List<AnimationComponentTarget>();

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

    private sealed class AnimationBeatField
    {
        public SerializedProperty Beat;
        public SerializedProperty EndBeat;
        public float StartBeat;
        public float StartEndBeat;
    }

    private sealed class AnimationComponentTarget
    {
        public Component Component;
        public SerializedObject Serialized;
        public readonly List<AnimationBeatField> Fields =
            new List<AnimationBeatField>();
    }

    private sealed class VisibilityBindingKey
    {
        public readonly int ControllerId;
        public readonly string LayerId;

        public VisibilityBindingKey(int controllerId, string layerId)
        {
            ControllerId = controllerId;
            LayerId = layerId;
        }

        public override bool Equals(object obj)
        {
            return obj is VisibilityBindingKey other &&
                   other.ControllerId == ControllerId &&
                   string.Equals(other.LayerId, LayerId, System.StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return ControllerId * 397 ^
                   (LayerId != null ? LayerId.GetHashCode() : 0);
        }
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
            PruneAnimationSelection();
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

        // 选中高亮（区间整体、单点仅点）。
        foreach (PositionedAnimationEvent p in positionedAnimationEvents)
        {
            if (!IsAnimationSelected(p.Event) ||
                !IsAnimationEventVisible(p, content))
            {
                continue;
            }

            if (p.Event.IsSpan)
            {
                float left = Mathf.Min(p.X, p.EndX);
                float right = Mathf.Max(p.X, p.EndX);
                DrawSelectionOutline(new Rect(
                    left - 1f,
                    p.CenterY - AnimationDotSize * 0.5f - 1f,
                    right - left + 2f,
                    AnimationDotSize + 2f));
            }
            else
            {
                DrawSelectionOutline(p.DotRect);
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
        GUI.Label(
            new Rect(194f, y, header.width - 198f, 14f),
            "ctrl+左键多选 · 拖动选中点改拍点",
            EditorStyles.miniLabel);
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
        Rect scrollArea = new Rect(
            laneRect.x,
            laneRect.y + AnimationLaneHeaderHeight,
            laneRect.width,
            laneRect.height - AnimationLaneHeaderHeight);

        // 滚轮：默认横向滚动时间轴（与节点轨道一致），ctrl 缩放，
        // shift 上下滚动动画内容。
        if (e.type == EventType.ScrollWheel && laneRect.Contains(e.mousePosition))
        {
            if (e.shift && !(e.control || e.command) &&
                scrollArea.Contains(e.mousePosition))
            {
                ScrollAnimationVertically(e.delta.y * AnimationWheelScrollSpeed);
                e.Use();
                RequestRepaint();
                return;
            }

            if (HandleTimelineWheel(e, laneRect))
                return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 &&
            scrollArea.Contains(e.mousePosition))
        {
            bool ctrl = e.control || e.command;
            PositionedAnimationEvent hit = FindAnimationHit(e.mousePosition);

            // ctrl+左键：切换时间轴多选，不跳 Hierarchy。
            if (ctrl)
            {
                if (hit != null)
                {
                    ToggleAnimationSelection(hit.Event);
                    RequestRepaint();
                }
                e.Use();
                return;
            }

            animDragActive = true;
            animDragged = false;
            animMoveCandidate = hit != null && IsAnimationSelected(hit.Event);
            animMoveDragged = false;
            animDragMouseStartX = e.mousePosition.x;
            animDragMouseStartY = e.mousePosition.y;
            animDragStartScrollY = animationScrollY;
            animMoveMouseStartBeat = ScreenXToBeat(e.mousePosition.x, laneRect);
            animPendingClickHit = hit;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0 && animDragActive)
        {
            float deltaX = e.mousePosition.x - animDragMouseStartX;
            float deltaY = e.mousePosition.y - animDragMouseStartY;
            float absX = Mathf.Abs(deltaX);
            float absY = Mathf.Abs(deltaY);

            // 按住已选中的点拖动：水平为主则批量移动，垂直为主则竖向滚动。
            if (animMoveCandidate && !animMoveDragged && !animDragged)
            {
                if (absX > AnimationDragThreshold && absX >= absY)
                {
                    animMoveDragged = true;
                    BeginAnimationMoveDrag();
                }
                else if (absY > AnimationDragThreshold && absY > absX)
                {
                    animDragged = true;
                }
            }

            if (animMoveDragged)
            {
                UpdateAnimationMoveDrag(e, laneRect);
                e.Use();
                RequestRepaint();
                return;
            }

            if (!animDragged && absY > AnimationDragThreshold)
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

            if (animMoveDragged)
            {
                EndAnimationMoveDrag();
            }
            else if (!animDragged)
            {
                if (animPendingClickHit != null)
                {
                    // 单击：跳 Hierarchy 选中 owner；随后 OnSelectionChange 会
                    // 反向把该 owner 对应的事件高亮到时间轴上。
                    SelectAnimationOwner(animPendingClickHit);
                }
                else
                {
                    // 空白处单击：清空时间轴动画多选。
                    ClearAnimationSelection();
                }
                RequestRepaint();
            }

            animPendingClickHit = null;
            animMoveCandidate = false;
            animMoveDragged = false;
            animDragged = false;
            e.Use();
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

    // ---- 多选与批量移动 ----

    private bool IsAnimationSelected(StageAnimationEvent evt)
    {
        foreach (StageAnimationEvent selected in animationSelection)
        {
            if (AnimationEventMatches(selected, evt))
                return true;
        }

        return false;
    }

    private void AddAnimationSelection(StageAnimationEvent evt)
    {
        if (evt.owner == null || IsAnimationSelected(evt))
            return;

        animationSelection.Add(evt);
    }

    private void RemoveAnimationSelection(StageAnimationEvent evt)
    {
        for (int i = animationSelection.Count - 1; i >= 0; i--)
        {
            if (AnimationEventMatches(animationSelection[i], evt))
                animationSelection.RemoveAt(i);
        }
    }

    private void ToggleAnimationSelection(StageAnimationEvent evt)
    {
        if (IsAnimationSelected(evt))
            RemoveAnimationSelection(evt);
        else
            AddAnimationSelection(evt);
    }

    private void ClearAnimationSelection()
    {
        animationSelection.Clear();
    }

    private void PruneAnimationSelection()
    {
        for (int i = animationSelection.Count - 1; i >= 0; i--)
        {
            if (animationSelection[i].owner == null)
                animationSelection.RemoveAt(i);
        }
    }

    private static bool AnimationEventMatches(
        StageAnimationEvent a,
        StageAnimationEvent b)
    {
        return a.kind == b.kind &&
               a.owner == b.owner &&
               string.Equals(a.label, b.label, System.StringComparison.Ordinal);
    }

    private void BeginAnimationMoveDrag()
    {
        DisposeAnimationMoveTargets();
        animMoveMinBeat = float.MaxValue;

        // 遍历当帧已布局的事件（beat 为当前值），避免选中快照的 beat 过期
        // 导致显隐事件在多次拖动后无法按拍点重新定位。
        foreach (PositionedAnimationEvent positioned in positionedAnimationEvents)
        {
            if (!IsAnimationSelected(positioned.Event))
                continue;

            StageAnimationEvent evt = positioned.Event;
            Component component = ResolveAnimationComponent(evt);
            if (component == null)
                continue;

            AnimationComponentTarget target = null;
            foreach (AnimationComponentTarget existing in animationMoveTargets)
            {
                if (existing.Component == component)
                {
                    target = existing;
                    break;
                }
            }

            if (target == null)
            {
                target = new AnimationComponentTarget
                {
                    Component = component,
                    Serialized = new SerializedObject(component)
                };
                animationMoveTargets.Add(target);
            }

            AnimationBeatField field =
                ResolveAnimationBeatField(target.Serialized, evt);
            if (field == null)
                continue;

            field.StartBeat = field.Beat.floatValue;
            field.StartEndBeat = field.EndBeat != null
                ? field.EndBeat.floatValue
                : field.StartBeat;
            animMoveMinBeat = Mathf.Min(animMoveMinBeat, field.StartBeat);
            target.Fields.Add(field);
        }

        if (animationMoveTargets.Count > 0)
        {
            var undoObjects = new Object[animationMoveTargets.Count];
            for (int i = 0; i < animationMoveTargets.Count; i++)
                undoObjects[i] = animationMoveTargets[i].Component;

            Undo.RegisterCompleteObjectUndo(undoObjects, "Move Animation Events");
        }
    }

    private void UpdateAnimationMoveDrag(Event e, Rect laneRect)
    {
        float mouseBeat = ScreenXToBeat(e.mousePosition.x, laneRect);
        float delta = mouseBeat - animMoveMouseStartBeat;
        if (!(e.control || e.command) && snap > 0f)
            delta = Mathf.Round(delta / snap) * snap;
        delta = Mathf.Max(delta, -animMoveMinBeat);

        foreach (AnimationComponentTarget target in animationMoveTargets)
        {
            foreach (AnimationBeatField field in target.Fields)
            {
                field.Beat.floatValue = Mathf.Max(0f, field.StartBeat + delta);
                if (field.EndBeat != null)
                {
                    field.EndBeat.floatValue =
                        Mathf.Max(0f, field.StartEndBeat + delta);
                }
            }

            target.Serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target.Component);
        }

        animationEventsDirty = true;
    }

    private void EndAnimationMoveDrag()
    {
        foreach (AnimationComponentTarget target in animationMoveTargets)
        {
            if (target.Component == null)
                continue;

            EditorUtility.SetDirty(target.Component);
            if (PrefabUtility.IsPartOfPrefabInstance(target.Component))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    target.Component);
            }
        }

        DisposeAnimationMoveTargets();
        animationEventsDirty = true;
        RequestRepaint();
        SceneView.RepaintAll();
    }

    private void DisposeAnimationMoveTargets()
    {
        foreach (AnimationComponentTarget target in animationMoveTargets)
            target.Serialized?.Dispose();

        animationMoveTargets.Clear();
    }

    private void CancelAnimationDrag()
    {
        if (animMoveDragged)
            EndAnimationMoveDrag();
        else
            DisposeAnimationMoveTargets();

        animDragActive = false;
        animDragged = false;
        animMoveCandidate = false;
        animMoveDragged = false;
        animPendingClickHit = null;
    }

    private static Component ResolveAnimationComponent(StageAnimationEvent evt)
    {
        Object owner = evt.owner;
        if (owner == null)
            return null;

        if (owner is GameObject gameObject)
        {
            switch (evt.kind)
            {
                case StageAnimationEventKind.Fly:
                    return gameObject.GetComponent<StageFlyAnimation>();
                case StageAnimationEventKind.Fade:
                    return gameObject.GetComponent<StageDisplayAnimation>();
                case StageAnimationEventKind.LayerVisibility:
                    return gameObject.GetComponent<StageSceneController>();
                default:
                    return null;
            }
        }

        return owner as Component;
    }

    private static AnimationBeatField ResolveAnimationBeatField(
        SerializedObject serialized,
        StageAnimationEvent evt)
    {
        switch (evt.kind)
        {
            case StageAnimationEventKind.Fly:
                if (evt.label == "飞入→飞出")
                    return CreateBeatField(serialized, "flyInEndBeat", "flyOutStartBeat");
                if (evt.label == "飞入")
                    return CreateBeatField(serialized, "flyInEndBeat", null);
                if (evt.label == "飞出")
                    return CreateBeatField(serialized, "flyOutStartBeat", null);
                return null;

            case StageAnimationEventKind.Fade:
                if (evt.label == "淡入→淡出")
                    return CreateBeatField(serialized, "fadeInEndBeat", "fadeOutStartBeat");
                if (evt.label == "淡入")
                    return CreateBeatField(serialized, "fadeInEndBeat", null);
                if (evt.label == "淡出")
                    return CreateBeatField(serialized, "fadeOutStartBeat", null);
                return null;

            case StageAnimationEventKind.LayerVisibility:
            {
                int index = FindVisibilityEventIndex(serialized, evt);
                if (index < 0)
                    return null;

                SerializedProperty beat = serialized
                    .FindProperty("visibilityEvents")
                    .GetArrayElementAtIndex(index)
                    .FindPropertyRelative("beat");
                return beat != null
                    ? new AnimationBeatField { Beat = beat }
                    : null;
            }

            default:
                return null;
        }
    }

    private static AnimationBeatField CreateBeatField(
        SerializedObject serialized,
        string beatPath,
        string endBeatPath)
    {
        SerializedProperty beat = serialized.FindProperty(beatPath);
        if (beat == null)
            return null;

        return new AnimationBeatField
        {
            Beat = beat,
            EndBeat = string.IsNullOrEmpty(endBeatPath)
                ? null
                : serialized.FindProperty(endBeatPath)
        };
    }

    private static int FindVisibilityEventIndex(
        SerializedObject serialized,
        StageAnimationEvent evt)
    {
        SerializedProperty array = serialized.FindProperty("visibilityEvents");
        if (array == null || !array.isArray)
            return -1;

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            string layerId = element.FindPropertyRelative("layerId")?.stringValue;
            float beat = element.FindPropertyRelative("beat")?.floatValue ?? 0f;
            bool visible = element.FindPropertyRelative("visible")?.boolValue ?? true;

            string label = $"显隐 {layerId}→{(visible ? "显示" : "隐藏")}";
            if (string.Equals(label, evt.label, System.StringComparison.Ordinal) &&
                Mathf.Approximately(beat, evt.beat))
            {
                return i;
            }
        }

        return -1;
    }

    // ---- 场景选中同步 ----

    private void SyncAnimationSelectionFromUnity()
    {
        List<StageAnimationEvent> events =
            GetAnimationEventsForObjects(Selection.objects);
        ReplaceAnimationSelection(events);
        ScrollToAnimationSelection(events);
    }

    private void ReplaceAnimationSelection(IEnumerable<StageAnimationEvent> events)
    {
        animationSelection.Clear();
        foreach (StageAnimationEvent evt in events)
            AddAnimationSelection(evt);
    }

    private void ScrollToAnimationSelection(List<StageAnimationEvent> events)
    {
        if (events == null || events.Count == 0)
            return;

        float minBeat = float.MaxValue;
        foreach (StageAnimationEvent evt in events)
            minBeat = Mathf.Min(minBeat, evt.beat);

        if (minBeat == float.MaxValue)
            return;

        float viewBeats = Mathf.Max(1f, position.width) / pixelsPerBeat;
        if (minBeat < scrollBeat || minBeat > scrollBeat + viewBeats * 0.8f)
        {
            scrollBeat = Mathf.Max(MinScrollBeat, minBeat - viewBeats * 0.2f);
        }
    }

    private List<StageAnimationEvent> GetAnimationEventsForObjects(
        Object[] selectedObjects)
    {
        var result = new List<StageAnimationEvent>();
        if (selectedObjects == null || selectedObjects.Length == 0)
            return result;

        // 1. 解析选中的 GameObject（Component 也归到其 GameObject）。
        var ownerObjects = new HashSet<GameObject>();
        foreach (Object selected in selectedObjects)
        {
            if (selected == null)
                continue;

            GameObject go = selected as GameObject;
            if (go == null && selected is Component component)
                go = component.gameObject;
            if (go != null)
                ownerObjects.Add(go);
        }

        // 2. 查找这些 GameObject 在场景控制器里绑定到的图层（显隐事件用）。
        var visibilityKeys = new HashSet<VisibilityBindingKey>();
        CollectVisibilityBindingsForObjects(ownerObjects, visibilityKeys);

        // 3. 匹配：owner 直接命中，或图层显隐事件命中。
        List<StageAnimationEvent> events = GetAnimationEvents();
        foreach (StageAnimationEvent evt in events)
        {
            if (evt.owner is GameObject ownerGO && ownerObjects.Contains(ownerGO))
            {
                result.Add(evt);
                continue;
            }

            if (evt.kind == StageAnimationEventKind.LayerVisibility &&
                evt.owner is GameObject controllerGO)
            {
                bool matched = false;
                foreach (VisibilityBindingKey key in visibilityKeys)
                {
                    if (key.ControllerId == controllerGO.GetInstanceID() &&
                        evt.label.StartsWith(
                            $"显隐 {key.LayerId}→",
                            System.StringComparison.Ordinal))
                    {
                        matched = true;
                        break;
                    }
                }

                if (matched)
                    result.Add(evt);
            }
        }

        return result;
    }

    private void CollectVisibilityBindingsForObjects(
        HashSet<GameObject> ownerObjects,
        HashSet<VisibilityBindingKey> visibilityKeys)
    {
        Scene scene = targetPlayer.gameObject.scene;
        if (!scene.IsValid())
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            if (root == null)
                continue;

            StageSceneController[] controllers =
                root.GetComponentsInChildren<StageSceneController>(true);
            foreach (StageSceneController controller in controllers)
            {
                if (controller == null)
                    continue;

                var so = new SerializedObject(controller);
                SerializedProperty bindings = so.FindProperty("bindings");
                if (bindings == null || !bindings.isArray)
                {
                    so.Dispose();
                    continue;
                }

                for (int i = 0; i < bindings.arraySize; i++)
                {
                    SerializedProperty binding = bindings.GetArrayElementAtIndex(i);
                    string layerId = binding.FindPropertyRelative("id")?.stringValue;
                    if (string.IsNullOrEmpty(layerId))
                        continue;

                    GameObject boundGO = ResolveBindingGameObject(binding);
                    if (boundGO != null && ownerObjects.Contains(boundGO))
                    {
                        visibilityKeys.Add(new VisibilityBindingKey(
                            controller.gameObject.GetInstanceID(),
                            layerId));
                    }
                }

                so.Dispose();
            }
        }
    }

    private static GameObject ResolveBindingGameObject(SerializedProperty binding)
    {
        Component component = null;

        SerializedProperty tilemapProp = binding.FindPropertyRelative("tilemap");
        if (tilemapProp != null)
            component = tilemapProp.objectReferenceValue as Component;

        if (component == null)
        {
            SerializedProperty rendererProp = binding.FindPropertyRelative("renderer");
            if (rendererProp != null)
                component = rendererProp.objectReferenceValue as Component;
        }

        if (component == null)
        {
            SerializedProperty colliderProp = binding.FindPropertyRelative("collider");
            if (colliderProp != null)
                component = colliderProp.objectReferenceValue as Component;
        }

        return component != null ? component.gameObject : null;
    }
}
