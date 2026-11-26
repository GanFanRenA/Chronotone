using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ChartTimelineWindow
{
    private const float SegmentLaneHeight = 26f;
    private const float SegmentBarVerticalPadding = 4f;
    private const float SegmentHitThreshold = 5f;
    private const string SegmentRenameControlId = "ChartTimeline.SegmentRename";

    private static readonly Color SegmentBarColor =
        new Color(0.32f, 0.52f, 0.82f, 0.45f);
    private static readonly Color SegmentSelectedColor =
        new Color(0.5f, 0.78f, 1f, 0.75f);

    private int selectedSegmentIndex = -1;

    private bool segmentDragging;
    private bool segmentDragChanged;
    private bool segmentDragRecorded;
    private float segmentDragMouseStartBeat;
    private float segmentDragStartStartBeat;
    private float segmentDragStartEndBeat;
    private float segmentDragMinBeat;
    private float segmentDragAppliedDelta;

    private readonly List<IBeatMappable> segmentMoveSources = new List<IBeatMappable>();
    private readonly List<BeatAnchor> segmentDragAnchors = new List<BeatAnchor>();
    private readonly List<Component> segmentDragObjects = new List<Component>();

    private int renamingSegmentIndex = -1;
    private string renameBuffer = string.Empty;
    private bool segmentRenameWasFocused;

    // ---- 数据 ----

    private List<RhythmSegment> GetSegments()
    {
        return targetPlayer != null ? targetPlayer.RhythmSegmentsEditable : null;
    }

    private void ValidateSegmentSelection()
    {
        List<RhythmSegment> segments = GetSegments();
        if (selectedSegmentIndex < 0)
            return;

        if (segments == null || selectedSegmentIndex >= segments.Count)
            selectedSegmentIndex = -1;
    }

    // ---- 绘制 ----

    private void DrawSegmentLane(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.15f));
        List<RhythmSegment> segments = GetSegments();

        if (segments == null || segments.Count == 0)
        {
            GUI.Label(
                new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height),
                "节奏段：G 用红轴→蓝轴创建 · 拖动移动 · 双击改名 · Del 删除",
                EditorStyles.miniLabel);
            return;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            RhythmSegment segment = segments[i];
            if (segment == null)
                continue;

            float startX = rect.x + (segment.startBeat - scrollBeat) * pixelsPerBeat;
            float endX = rect.x + (segment.endBeat - scrollBeat) * pixelsPerBeat;
            float left = Mathf.Min(startX, endX);
            float right = Mathf.Max(startX, endX);
            if (right < -10f || left > rect.width + 10f)
                continue;

            Rect barRect = new Rect(
                left,
                rect.y + SegmentBarVerticalPadding,
                Mathf.Max(1f, right - left),
                rect.height - SegmentBarVerticalPadding * 2f);

            bool selected = i == selectedSegmentIndex;
            EditorGUI.DrawRect(barRect, selected ? SegmentSelectedColor : SegmentBarColor);
            if (selected)
                DrawSelectionOutline(barRect);

            if (i == renamingSegmentIndex)
            {
                DrawSegmentRenameField(barRect, segment);
            }
            else if (barRect.width > 16f)
            {
                GUI.Label(
                    new Rect(barRect.x + 3f, barRect.y, barRect.width - 6f, barRect.height),
                    segment.name,
                    EditorStyles.miniLabel);
            }
        }
    }

    private void DrawSegmentRenameField(Rect barRect, RhythmSegment segment)
    {
        GUI.SetNextControlName(SegmentRenameControlId);

        renameBuffer = EditorGUI.TextField(barRect, renameBuffer);

        bool focused = GUI.GetNameOfFocusedControl() == SegmentRenameControlId;
        if (!focused && !segmentRenameWasFocused)
        {
            GUI.FocusControl(SegmentRenameControlId);
            focused = true;
        }

        if (segmentRenameWasFocused && !focused)
            CommitSegmentRename();

        segmentRenameWasFocused = focused;
    }

    // ---- 交互 ----

    private void HandleSegmentInput(Rect laneRect)
    {
        Event e = Event.current;
        List<RhythmSegment> segments = GetSegments();

        // 重命名期间：Enter 提交 / Escape 取消，其余交给文本框。
        if (renamingSegmentIndex >= 0)
        {
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                CommitSegmentRename();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelSegmentRename();
                e.Use();
            }
            return;
        }

        if (HandleTimelineWheel(e, laneRect))
            return;

        if (segments == null || segments.Count == 0)
            return;

        bool rightClick =
            e.type == EventType.ContextClick ||
            (e.type == EventType.MouseDown && e.button == 1);
        if (rightClick && laneRect.Contains(e.mousePosition))
        {
            ShowSegmentContextMenu(laneRect, e.mousePosition, segments);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 &&
            laneRect.Contains(e.mousePosition))
        {
            RhythmSegment hit = FindSegmentAt(e.mousePosition.x, laneRect, segments);

            if (e.clickCount == 2 && hit != null)
            {
                selectedSegmentIndex = segments.IndexOf(hit);
                BeginSegmentRename(hit);
                e.Use();
                RequestRepaint();
                return;
            }

            if (hit != null)
            {
                selectedSegmentIndex = segments.IndexOf(hit);
                BeginSegmentDrag(e, laneRect, hit);
            }
            else
            {
                selectedSegmentIndex = -1;
            }

            e.Use();
            RequestRepaint();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0 && segmentDragging)
        {
            UpdateSegmentDrag(e, laneRect);
            e.Use();
            RequestRepaint();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0 && segmentDragging)
        {
            EndSegmentDrag();
            e.Use();
            RequestRepaint();
            return;
        }
    }

    private void ShowSegmentContextMenu(
        Rect laneRect,
        Vector2 mouse,
        List<RhythmSegment> segments)
    {
        RhythmSegment hit = FindSegmentAt(mouse.x, laneRect, segments);
        int index = hit != null ? segments.IndexOf(hit) : -1;

        var menu = new GenericMenu();
        if (index >= 0)
        {
            menu.AddItem(new GUIContent("重命名"), false, () =>
            {
                selectedSegmentIndex = index;
                BeginSegmentRename(segments[index]);
            });
            menu.AddItem(
                new GUIContent("删除节奏段"),
                false,
                () => DeleteSegmentAt(index));
            menu.AddSeparator(string.Empty);
        }
        menu.AddItem(new GUIContent("用红轴→蓝轴创建节奏段"), false, CreateSegmentFromLoop);
        menu.ShowAsContext();
    }

    private RhythmSegment FindSegmentAt(
        float mouseX,
        Rect laneRect,
        List<RhythmSegment> segments)
    {
        RhythmSegment best = null;
        float bestDistance = SegmentHitThreshold;

        foreach (RhythmSegment segment in segments)
        {
            if (segment == null)
                continue;

            float startX = laneRect.x + (segment.startBeat - scrollBeat) * pixelsPerBeat;
            float endX = laneRect.x + (segment.endBeat - scrollBeat) * pixelsPerBeat;
            float left = Mathf.Min(startX, endX);
            float right = Mathf.Max(startX, endX);

            if (mouseX >= left && mouseX <= right)
                return segment;

            float distance = Mathf.Min(
                Mathf.Abs(left - mouseX),
                Mathf.Abs(right - mouseX));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = segment;
            }
        }

        return best;
    }

    // ---- 创建 / 删除 ----

    private void CreateSegmentFromLoop()
    {
        if (targetPlayer == null)
            return;

        if (!loopEnabled || loopEndBeat <= playStartBeat)
        {
            ShowNotification(
                new GUIContent("请先在标尺上左键设红轴起点、右键设蓝轴终点"));
            return;
        }

        List<RhythmSegment> segments = GetSegments();
        if (segments == null)
            return;

        Undo.RecordObject(targetPlayer, "Create Rhythm Segment");
        var segment = new RhythmSegment
        {
            name = $"段落{segments.Count + 1}",
            startBeat = playStartBeat,
            endBeat = loopEndBeat
        };
        segments.Add(segment);
        EditorUtility.SetDirty(targetPlayer);

        selectedSegmentIndex = segments.Count - 1;
        RequestRepaint();
    }

    private void DeleteSegmentAt(int index)
    {
        List<RhythmSegment> segments = GetSegments();
        if (segments == null || index < 0 || index >= segments.Count)
            return;

        Undo.RecordObject(targetPlayer, "Delete Rhythm Segment");
        segments.RemoveAt(index);
        EditorUtility.SetDirty(targetPlayer);

        if (selectedSegmentIndex == index)
            selectedSegmentIndex = -1;
        else if (selectedSegmentIndex > index)
            selectedSegmentIndex--;

        CancelSegmentRename();
        RequestRepaint();
    }

    private void DeleteSelectedSegment()
    {
        if (selectedSegmentIndex >= 0)
            DeleteSegmentAt(selectedSegmentIndex);
    }

    // ---- 拖动移动 ----

    private void BeginSegmentDrag(Event e, Rect laneRect, RhythmSegment segment)
    {
        segmentDragging = true;
        segmentDragChanged = false;
        segmentDragRecorded = false;
        segmentDragMouseStartBeat = ScreenXToBeat(e.mousePosition.x, laneRect);
        segmentDragStartStartBeat = segment.startBeat;
        segmentDragStartEndBeat = segment.endBeat;
        segmentDragMinBeat = segment.startBeat;
        segmentDragAppliedDelta = 0f;

        // 快照：原区间内所有「主拍点」落点都收集起来，拖动时按固定成员平移，避免边界漂移。
        segmentDragAnchors.Clear();
        segmentDragObjects.Clear();
        CollectSceneBeatMappables(segmentMoveSources);

        foreach (IBeatMappable source in segmentMoveSources)
        {
            var anchors = new List<BeatAnchor>();
            source.CollectBeatAnchors(anchors);

            bool any = false;
            foreach (BeatAnchor anchor in anchors)
            {
                if (anchor.Beat >= segmentDragStartStartBeat &&
                    anchor.Beat < segmentDragStartEndBeat)
                {
                    segmentDragAnchors.Add(anchor);
                    any = true;
                }
            }

            if (any && source is Component component)
                segmentDragObjects.Add(component);
        }
    }

    private void UpdateSegmentDrag(Event e, Rect laneRect)
    {
        if (!segmentDragging || targetPlayer == null)
            return;

        float mouseBeat = ScreenXToBeat(e.mousePosition.x, laneRect);
        float delta = mouseBeat - segmentDragMouseStartBeat;
        if (!(e.control || e.command) && snap > 0f)
            delta = Mathf.Round(delta / snap) * snap;
        delta = Mathf.Max(delta, -segmentDragMinBeat);

        if (Mathf.Approximately(delta, segmentDragAppliedDelta))
            return;

        if (!segmentDragRecorded)
        {
            RecordSegmentMoveUndo();
            segmentDragRecorded = true;
        }

        float step = delta - segmentDragAppliedDelta;

        List<RhythmSegment> segments = GetSegments();
        if (segments != null && selectedSegmentIndex >= 0 &&
            selectedSegmentIndex < segments.Count)
        {
            RhythmSegment segment = segments[selectedSegmentIndex];
            segment.startBeat = segmentDragStartStartBeat + delta;
            segment.endBeat = segmentDragStartEndBeat + delta;
        }

        foreach (BeatAnchor anchor in segmentDragAnchors)
            anchor.Offset(step);

        segmentDragAppliedDelta = delta;
        segmentDragChanged = true;
        targetPlayer.MarkDirty();
    }

    private void EndSegmentDrag()
    {
        segmentDragging = false;

        if (segmentDragChanged)
        {
            if (targetPlayer != null && segmentDragAppliedDelta != 0f)
            {
                // 节点 beat 变化后，从最早受影响拍点重摆节点 X 坐标。
                targetPlayer.UpdatePos(
                    Mathf.Max(0f, segmentDragStartStartBeat + segmentDragAppliedDelta));
                EditorUtility.SetDirty(targetPlayer);
            }

            // 非节点动画组件（淡入淡出/飞入飞出/显隐）的字段改动要落盘并记录 prefab override。
            foreach (Component component in segmentDragObjects)
            {
                if (component == null || component == targetPlayer)
                    continue;

                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }

            InvalidateMarkers();
            InvalidateAnimationEvents();
        }
        else
        {
            RequestRepaint();
        }

        segmentDragAnchors.Clear();
        segmentDragObjects.Clear();
        segmentDragChanged = false;
        segmentDragRecorded = false;
        segmentDragAppliedDelta = 0f;
    }

    private void RecordSegmentMoveUndo()
    {
        var objects = new List<Object>(segmentDragObjects.Count + 2);
        if (targetPlayer != null)
            objects.Add(targetPlayer);

        foreach (Component component in segmentDragObjects)
        {
            if (component == null || component == targetPlayer)
                continue;

            objects.Add(component);

            if (component is NodeMarker marker && marker.transform != null)
                objects.Add(marker.transform);
        }

        Undo.RegisterCompleteObjectUndo(objects.ToArray(), "Move Rhythm Segment");
    }

    private void CollectSceneBeatMappables(List<IBeatMappable> result)
    {
        result.Clear();
        if (targetPlayer == null)
            return;

        Scene scene = targetPlayer.gameObject.scene;
        if (!scene.IsValid())
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            if (root == null)
                continue;

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IBeatMappable mappable)
                    result.Add(mappable);
            }
        }
    }

    // ---- 重命名 ----

    private void BeginSegmentRename(RhythmSegment segment)
    {
        if (segment == null || selectedSegmentIndex < 0)
            return;

        renamingSegmentIndex = selectedSegmentIndex;
        renameBuffer = segment.name;
        segmentRenameWasFocused = false;
        RequestRepaint();
    }

    private void CommitSegmentRename()
    {
        if (renamingSegmentIndex < 0)
            return;

        List<RhythmSegment> segments = GetSegments();
        if (segments != null && renamingSegmentIndex >= 0 &&
            renamingSegmentIndex < segments.Count)
        {
            string name = string.IsNullOrEmpty(renameBuffer) ? "段落" : renameBuffer;
            if (segments[renamingSegmentIndex].name != name)
            {
                Undo.RecordObject(targetPlayer, "Rename Rhythm Segment");
                segments[renamingSegmentIndex].name = name;
                EditorUtility.SetDirty(targetPlayer);
            }
        }

        renamingSegmentIndex = -1;
        renameBuffer = string.Empty;
        segmentRenameWasFocused = false;
        RequestRepaint();
    }

    private void CancelSegmentRename()
    {
        renamingSegmentIndex = -1;
        renameBuffer = string.Empty;
        segmentRenameWasFocused = false;
        RequestRepaint();
    }
}
