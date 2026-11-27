using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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
    private float segmentDragStartBeat;
    private float segmentDragMinBeat;
    private float segmentDragAppliedDelta;
    private RhythmSegment segmentDragSegment;
    private readonly List<NodeMarker> segmentDragNodes = new List<NodeMarker>();
    private readonly List<StageVfxEvent> segmentDragVfx = new List<StageVfxEvent>();

    private int renamingSegmentIndex = -1;
    private string renameBuffer = string.Empty;
    private bool segmentRenameWasFocused;

    // ---- 数据 ----

    private List<RhythmSegment> GetSegments()
    {
        return targetPlayer != null ? targetPlayer.RhythmSegmentsEditable : null;
    }

    private List<SegmentTemplate> GetTemplates()
    {
        return targetPlayer != null ? targetPlayer.SegmentTemplatesEditable : null;
    }

    private SegmentTemplate FindTemplate(string id)
    {
        List<SegmentTemplate> templates = GetTemplates();
        if (templates == null || string.IsNullOrEmpty(id))
            return null;

        foreach (SegmentTemplate template in templates)
        {
            if (template != null && template.id == id)
                return template;
        }

        return null;
    }

    private RhythmSegment FindInstance(string instanceId)
    {
        List<RhythmSegment> segments = GetSegments();
        if (segments == null)
            return null;

        foreach (RhythmSegment segment in segments)
        {
            if (segment != null && segment.instanceId == instanceId)
                return segment;
        }

        return null;
    }

    private float SegmentEndBeat(RhythmSegment segment)
    {
        SegmentTemplate template = FindTemplate(segment.templateId);
        return template != null ? segment.startBeat + template.durationBeats : segment.startBeat;
    }

    private string SegmentDisplayName(RhythmSegment segment)
    {
        SegmentTemplate template = FindTemplate(segment.templateId);
        return template != null ? template.name : "段";
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
                "节奏段：G 用红轴→蓝轴建段 · 右键复制模板 · 拖动移动 · 双击改名 · Del 删除",
                EditorStyles.miniLabel);
            return;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            RhythmSegment segment = segments[i];
            if (segment == null)
                continue;

            float endBeat = SegmentEndBeat(segment);
            float startX = rect.x + (segment.startBeat - scrollBeat) * pixelsPerBeat;
            float endX = rect.x + (endBeat - scrollBeat) * pixelsPerBeat;
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
                DrawSegmentRenameField(barRect);
            }
            else if (barRect.width > 16f)
            {
                GUI.Label(
                    new Rect(barRect.x + 3f, barRect.y, barRect.width - 6f, barRect.height),
                    SegmentDisplayName(segment),
                    EditorStyles.miniLabel);
            }
        }
    }

    private void DrawSegmentRenameField(Rect barRect)
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

        float beat = ScreenXToBeat(mouse.x, laneRect);
        if (snap > 0f)
            beat = Mathf.Round(beat / snap) * snap;
        beat = Mathf.Max(0f, beat);

        var menu = new GenericMenu();
        if (index >= 0 && hit != null)
        {
            menu.AddItem(new GUIContent("重命名"), false, () =>
            {
                selectedSegmentIndex = index;
                BeginSegmentRename(hit);
            });
            menu.AddItem(new GUIContent("删除实例"), false, () => DeleteInstanceAt(index));

            SegmentTemplate hitTemplate = FindTemplate(hit.templateId);
            if (hitTemplate != null)
            {
                menu.AddItem(
                    new GUIContent($"删除模板「{hitTemplate.name}」及所有实例"),
                    false,
                    () => DeleteTemplate(hitTemplate));
            }

            menu.AddSeparator(string.Empty);
        }

        List<SegmentTemplate> templates = GetTemplates();
        if (templates != null && templates.Count > 0)
        {
            menu.AddDisabledItem(new GUIContent($"复制模板到 beat {beat:0.###}"));
            foreach (SegmentTemplate template in templates)
            {
                if (template == null)
                    continue;

                menu.AddItem(
                    new GUIContent($"复制「{template.name}」"),
                    false,
                    () => CopyTemplateAt(template, beat));
            }

            menu.AddSeparator(string.Empty);
        }

        menu.AddItem(new GUIContent("用红轴→蓝轴新建段"), false, CreateSegmentFromLoop);
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

            float endBeat = SegmentEndBeat(segment);
            float startX = laneRect.x + (segment.startBeat - scrollBeat) * pixelsPerBeat;
            float endX = laneRect.x + (endBeat - scrollBeat) * pixelsPerBeat;
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

    // ---- 创建 / 复制 ----

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

        float start = playStartBeat;
        float end = loopEndBeat;

        CaptureFromRange(
            start,
            end,
            out List<SegmentTemplateNode> nodes,
            out List<SegmentTemplateVfx> vfx,
            out List<NodeMarker> capturedMarkers,
            out List<StageVfxEvent> capturedVfx);

        List<SegmentTemplate> templates = GetTemplates();
        List<RhythmSegment> segments = GetSegments();
        if (templates == null || segments == null)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Rhythm Segment");
        Undo.RecordObject(targetPlayer, "Create Rhythm Segment");

        var template = new SegmentTemplate
        {
            id = System.Guid.NewGuid().ToString("N"),
            name = $"段落{templates.Count + 1}",
            durationBeats = end - start,
            nodes = nodes,
            vfx = vfx
        };
        templates.Add(template);

        var segment = new RhythmSegment
        {
            templateId = template.id,
            instanceId = System.Guid.NewGuid().ToString("N"),
            startBeat = start
        };
        segments.Add(segment);

        // 把捕获到的元素打标为该实例的展开元素，无需删除重建。
        for (int i = 0; i < capturedMarkers.Count; i++)
        {
            NodeMarker marker = capturedMarkers[i];
            marker.segmentTemplateId = template.id;
            marker.segmentInstanceId = segment.instanceId;
            marker.segmentTemplateNodeIndex = i;
            EditorUtility.SetDirty(marker);
        }

        for (int i = 0; i < capturedVfx.Count; i++)
        {
            StageVfxEvent e = capturedVfx[i];
            e.segmentTemplateId = template.id;
            e.segmentInstanceId = segment.instanceId;
            e.segmentTemplateVfxIndex = i;
        }

        Undo.CollapseUndoOperations(undoGroup);

        targetPlayer.UpdatePos(start);
        targetPlayer.MarkDirty();
        EditorUtility.SetDirty(targetPlayer);
        InvalidateMarkers();

        selectedSegmentIndex = segments.Count - 1;
        RequestRepaint();
        SceneView.RepaintAll();
    }

    private void CopyTemplateAt(SegmentTemplate template, float beat)
    {
        if (template == null || targetPlayer == null)
            return;

        List<RhythmSegment> segments = GetSegments();
        if (segments == null)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Copy Rhythm Segment");
        Undo.RecordObject(targetPlayer, "Copy Rhythm Segment");

        var segment = new RhythmSegment
        {
            templateId = template.id,
            instanceId = System.Guid.NewGuid().ToString("N"),
            startBeat = beat
        };
        segments.Add(segment);

        for (int i = 0; i < template.nodes.Count; i++)
        {
            SegmentTemplateNode node = template.nodes[i];
            InstantiateTemplateNode(node, beat + node.relativeBeat, template.id, segment.instanceId, i);
        }

        for (int i = 0; i < template.vfx.Count; i++)
        {
            SegmentTemplateVfx vfx = template.vfx[i];
            AddTemplateVfx(vfx, beat + vfx.relativeBeat, template.id, segment.instanceId, i);
        }

        Undo.CollapseUndoOperations(undoGroup);

        targetPlayer.UpdatePos(beat);
        targetPlayer.MarkDirty();
        EditorUtility.SetDirty(targetPlayer);
        InvalidateMarkers();

        selectedSegmentIndex = segments.Count - 1;
        RequestRepaint();
        SceneView.RepaintAll();
    }

    private NodeMarker InstantiateTemplateNode(
        SegmentTemplateNode node,
        float beat,
        string templateId,
        string instanceId,
        int nodeIndex)
    {
        NodeMarker prefab = ResolveNodePrefab();
        if (prefab == null)
            return null;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, root);
        Undo.RegisterCreatedObjectUndo(go, "Expand Rhythm Segment");

        var marker = go.GetComponent<NodeMarker>();
        marker.beat = beat;
        marker.order = 0;
        marker.action = node.action;
        marker.gravity = node.gravity;
        marker.auto = node.auto;
        marker.visible = node.visible;
        marker.strength = node.strength;

        Vector3 position = root.position;
        position.x = root.position.x;
        position.y = node.positionY;
        marker.transform.position = position;

        marker.ApplyAppearance(targetPlayer.ResolveNodeAppearanceSet());
        marker.ApplyVisibility();

        marker.segmentTemplateId = templateId;
        marker.segmentInstanceId = instanceId;
        marker.segmentTemplateNodeIndex = nodeIndex;

        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
        PrefabUtility.RecordPrefabInstancePropertyModifications(marker.transform);
        EditorUtility.SetDirty(marker);

        return marker;
    }

    private void AddTemplateVfx(
        SegmentTemplateVfx vfx,
        float beat,
        string templateId,
        string instanceId,
        int vfxIndex)
    {
        var e = new StageVfxEvent
        {
            beat = beat,
            kind = StageVfxEventKind.ScreenShake,
            durationBeats = vfx.durationBeats,
            strength = vfx.strength,
            frequency = vfx.frequency,
            segmentTemplateId = templateId,
            segmentInstanceId = instanceId,
            segmentTemplateVfxIndex = vfxIndex
        };
        targetPlayer.VfxEventsEditable.Add(e);
    }

    private void CaptureFromRange(
        float start,
        float end,
        out List<SegmentTemplateNode> nodes,
        out List<SegmentTemplateVfx> vfx,
        out List<NodeMarker> capturedMarkers,
        out List<StageVfxEvent> capturedVfx)
    {
        nodes = new List<SegmentTemplateNode>();
        vfx = new List<SegmentTemplateVfx>();
        capturedMarkers = new List<NodeMarker>();
        capturedVfx = new List<StageVfxEvent>();

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;

        var freeMarkers = new List<NodeMarker>();
        foreach (NodeMarker marker in root.GetComponentsInChildren<NodeMarker>(false))
        {
            if (marker != null &&
                string.IsNullOrEmpty(marker.segmentTemplateId) &&
                marker.beat >= start && marker.beat < end)
            {
                freeMarkers.Add(marker);
            }
        }
        freeMarkers.Sort((a, b) => a.beat.CompareTo(b.beat));

        foreach (NodeMarker marker in freeMarkers)
        {
            nodes.Add(new SegmentTemplateNode
            {
                relativeBeat = marker.beat - start,
                action = marker.action,
                gravity = marker.gravity,
                auto = marker.auto,
                visible = marker.visible,
                strength = marker.strength,
                positionY = marker.transform.position.y
            });
            capturedMarkers.Add(marker);
        }

        if (targetPlayer.VfxEventsEditable != null)
        {
            var freeVfx = new List<StageVfxEvent>();
            foreach (StageVfxEvent e in targetPlayer.VfxEventsEditable)
            {
                if (e != null &&
                    string.IsNullOrEmpty(e.segmentTemplateId) &&
                    e.beat >= start && e.beat < end)
                {
                    freeVfx.Add(e);
                }
            }
            freeVfx.Sort((a, b) => a.beat.CompareTo(b.beat));

            foreach (StageVfxEvent e in freeVfx)
            {
                vfx.Add(new SegmentTemplateVfx
                {
                    relativeBeat = e.beat - start,
                    durationBeats = e.durationBeats,
                    strength = e.strength,
                    frequency = e.frequency
                });
                capturedVfx.Add(e);
            }
        }
    }

    // ---- 删除 ----

    private void DeleteInstanceAt(int index)
    {
        List<RhythmSegment> segments = GetSegments();
        if (segments == null || index < 0 || index >= segments.Count)
            return;

        RhythmSegment segment = segments[index];

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Delete Rhythm Segment");
        Undo.RecordObject(targetPlayer, "Delete Rhythm Segment");

        DestroyInstanceElements(segment);
        segments.RemoveAt(index);
        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.SetDirty(targetPlayer);
        targetPlayer.MarkDirty();

        if (selectedSegmentIndex == index)
            selectedSegmentIndex = -1;
        else if (selectedSegmentIndex > index)
            selectedSegmentIndex--;

        CancelSegmentRename();
        InvalidateMarkers();
        RequestRepaint();
    }

    private void DeleteSelectedSegment()
    {
        if (selectedSegmentIndex >= 0)
            DeleteInstanceAt(selectedSegmentIndex);
    }

    private void DeleteTemplate(SegmentTemplate template)
    {
        if (template == null || targetPlayer == null)
            return;

        List<SegmentTemplate> templates = GetTemplates();
        List<RhythmSegment> segments = GetSegments();
        if (templates == null || segments == null)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Delete Rhythm Segment Template");
        Undo.RecordObject(targetPlayer, "Delete Rhythm Segment Template");

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (segments[i] != null && segments[i].templateId == template.id)
                DestroyInstanceElements(segments[i]);
        }

        segments.RemoveAll(s => s != null && s.templateId == template.id);
        templates.Remove(template);
        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.SetDirty(targetPlayer);
        targetPlayer.MarkDirty();
        selectedSegmentIndex = -1;
        CancelSegmentRename();
        InvalidateMarkers();
        RequestRepaint();
    }

    private void DestroyInstanceElements(RhythmSegment segment)
    {
        if (segment == null || targetPlayer == null)
            return;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;

        var toDestroy = new List<NodeMarker>();
        foreach (NodeMarker marker in root.GetComponentsInChildren<NodeMarker>(false))
        {
            if (marker != null && marker.segmentInstanceId == segment.instanceId)
                toDestroy.Add(marker);
        }

        foreach (NodeMarker marker in toDestroy)
            Undo.DestroyObjectImmediate(marker.gameObject);

        if (targetPlayer.VfxEventsEditable != null)
        {
            for (int i = targetPlayer.VfxEventsEditable.Count - 1; i >= 0; i--)
            {
                StageVfxEvent e = targetPlayer.VfxEventsEditable[i];
                if (e != null && e.segmentInstanceId == segment.instanceId)
                    targetPlayer.VfxEventsEditable.RemoveAt(i);
            }
        }
    }

    // ---- 拖动移动 ----

    private void BeginSegmentDrag(Event e, Rect laneRect, RhythmSegment segment)
    {
        segmentDragging = true;
        segmentDragChanged = false;
        segmentDragRecorded = false;
        segmentDragMouseStartBeat = ScreenXToBeat(e.mousePosition.x, laneRect);
        segmentDragStartBeat = segment.startBeat;
        segmentDragMinBeat = segment.startBeat;
        segmentDragAppliedDelta = 0f;
        segmentDragSegment = segment;

        segmentDragNodes.Clear();
        segmentDragVfx.Clear();
        CollectInstanceElements(segment, segmentDragNodes, segmentDragVfx);
    }

    private void UpdateSegmentDrag(Event e, Rect laneRect)
    {
        if (!segmentDragging || segmentDragSegment == null || targetPlayer == null)
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

        segmentDragSegment.startBeat = segmentDragStartBeat + delta;

        foreach (NodeMarker marker in segmentDragNodes)
        {
            if (marker != null)
                marker.beat += step;
        }

        foreach (StageVfxEvent vfx in segmentDragVfx)
        {
            if (vfx != null)
                vfx.beat += step;
        }

        segmentDragAppliedDelta = delta;
        segmentDragChanged = true;
        targetPlayer.MarkDirty();
    }

    private void EndSegmentDrag()
    {
        segmentDragging = false;

        if (segmentDragChanged && targetPlayer != null)
        {
            targetPlayer.UpdatePos(
                Mathf.Max(0f, segmentDragStartBeat + segmentDragAppliedDelta));
            EditorUtility.SetDirty(targetPlayer);
            InvalidateMarkers();
        }
        else
        {
            RequestRepaint();
        }

        segmentDragNodes.Clear();
        segmentDragVfx.Clear();
        segmentDragSegment = null;
        segmentDragChanged = false;
        segmentDragRecorded = false;
        segmentDragAppliedDelta = 0f;
    }

    private void RecordSegmentMoveUndo()
    {
        var objects = new List<Object>();
        if (targetPlayer != null)
            objects.Add(targetPlayer);

        foreach (NodeMarker marker in segmentDragNodes)
        {
            if (marker == null)
                continue;

            objects.Add(marker);
            if (marker.transform != null)
                objects.Add(marker.transform);
        }

        Undo.RegisterCompleteObjectUndo(objects.ToArray(), "Move Rhythm Segment");
    }

    private void CollectInstanceElements(
        RhythmSegment segment,
        List<NodeMarker> nodes,
        List<StageVfxEvent> vfx)
    {
        nodes.Clear();
        vfx.Clear();
        if (segment == null || targetPlayer == null)
            return;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        foreach (NodeMarker marker in root.GetComponentsInChildren<NodeMarker>(false))
        {
            if (marker != null && marker.segmentInstanceId == segment.instanceId)
                nodes.Add(marker);
        }

        if (targetPlayer.VfxEventsEditable != null)
        {
            foreach (StageVfxEvent e in targetPlayer.VfxEventsEditable)
            {
                if (e != null && e.segmentInstanceId == segment.instanceId)
                    vfx.Add(e);
            }
        }
    }

    // ---- 编辑同步（改实例 → 改模板 → 同步其他实例） ----

    /// <summary>节点拖动结束后，把被拖动的生成节点同步回模板。</summary>
    private void SyncDraggedSegmentNodes()
    {
        foreach (NodeMarker marker in selection)
        {
            if (marker != null && !string.IsNullOrEmpty(marker.segmentTemplateId))
                SyncNodeToTemplate(marker);
        }
    }

    private void SyncNodeToTemplate(NodeMarker marker)
    {
        if (marker == null ||
            string.IsNullOrEmpty(marker.segmentTemplateId) ||
            marker.segmentTemplateNodeIndex < 0)
        {
            return;
        }

        SegmentTemplate template = FindTemplate(marker.segmentTemplateId);
        if (template == null || marker.segmentTemplateNodeIndex >= template.nodes.Count)
            return;

        RhythmSegment instance = FindInstance(marker.segmentInstanceId);
        if (instance == null)
            return;

        SegmentTemplateNode node = template.nodes[marker.segmentTemplateNodeIndex];
        node.relativeBeat = Mathf.Max(0f, marker.beat - instance.startBeat);
        node.action = marker.action;
        node.gravity = marker.gravity;
        node.strength = marker.strength;
        node.positionY = marker.transform.position.y;
        node.auto = marker.auto;
        node.visible = marker.visible;

        MirrorNodeToOtherInstances(template, instance, marker.segmentTemplateNodeIndex, node);

        EditorUtility.SetDirty(targetPlayer);
        targetPlayer.MarkDirty();
        targetPlayer.QueueUpdatePos(0f);
        InvalidateMarkers();
    }

    private void MirrorNodeToOtherInstances(
        SegmentTemplate template,
        RhythmSegment sourceInstance,
        int nodeIndex,
        SegmentTemplateNode node)
    {
        List<RhythmSegment> segments = GetSegments();
        if (segments == null)
            return;

        NodeAppearanceSet appearanceSet = targetPlayer.ResolveNodeAppearanceSet();

        foreach (RhythmSegment other in segments)
        {
            if (other == null ||
                other.templateId != template.id ||
                other.instanceId == sourceInstance.instanceId)
            {
                continue;
            }

            NodeMarker otherMarker = FindInstanceNode(other, nodeIndex);
            if (otherMarker == null)
                continue;

            otherMarker.beat = other.startBeat + node.relativeBeat;
            otherMarker.action = node.action;
            otherMarker.gravity = node.gravity;
            otherMarker.strength = node.strength;
            otherMarker.auto = node.auto;
            otherMarker.visible = node.visible;

            Vector3 position = otherMarker.transform.position;
            position.y = node.positionY;
            otherMarker.transform.position = position;

            otherMarker.ApplyAppearance(appearanceSet);
            otherMarker.ApplyVisibility();
            EditorUtility.SetDirty(otherMarker);
        }
    }

    private NodeMarker FindInstanceNode(RhythmSegment segment, int nodeIndex)
    {
        if (segment == null)
            return null;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        foreach (NodeMarker marker in root.GetComponentsInChildren<NodeMarker>(false))
        {
            if (marker != null &&
                marker.segmentInstanceId == segment.instanceId &&
                marker.segmentTemplateNodeIndex == nodeIndex)
            {
                return marker;
            }
        }

        return null;
    }

    private void SyncVfxToTemplate(StageVfxEvent vfx)
    {
        if (vfx == null ||
            string.IsNullOrEmpty(vfx.segmentTemplateId) ||
            vfx.segmentTemplateVfxIndex < 0)
        {
            return;
        }

        SegmentTemplate template = FindTemplate(vfx.segmentTemplateId);
        if (template == null || vfx.segmentTemplateVfxIndex >= template.vfx.Count)
            return;

        RhythmSegment instance = FindInstance(vfx.segmentInstanceId);
        if (instance == null)
            return;

        SegmentTemplateVfx def = template.vfx[vfx.segmentTemplateVfxIndex];
        def.relativeBeat = Mathf.Max(0f, vfx.beat - instance.startBeat);
        def.durationBeats = vfx.durationBeats;
        def.strength = vfx.strength;
        def.frequency = vfx.frequency;

        MirrorVfxToOtherInstances(template, instance, vfx.segmentTemplateVfxIndex, def);

        EditorUtility.SetDirty(targetPlayer);
        targetPlayer.MarkDirty();
        RequestRepaint();
    }

    private void MirrorVfxToOtherInstances(
        SegmentTemplate template,
        RhythmSegment sourceInstance,
        int vfxIndex,
        SegmentTemplateVfx def)
    {
        List<RhythmSegment> segments = GetSegments();
        if (segments == null || targetPlayer.VfxEventsEditable == null)
            return;

        foreach (RhythmSegment other in segments)
        {
            if (other == null ||
                other.templateId != template.id ||
                other.instanceId == sourceInstance.instanceId)
            {
                continue;
            }

            foreach (StageVfxEvent e in targetPlayer.VfxEventsEditable)
            {
                if (e == null ||
                    e.segmentInstanceId != other.instanceId ||
                    e.segmentTemplateVfxIndex != vfxIndex)
                {
                    continue;
                }

                e.beat = other.startBeat + def.relativeBeat;
                e.durationBeats = def.durationBeats;
                e.strength = def.strength;
                e.frequency = def.frequency;
            }
        }
    }

    // ---- 删除同步（删除生成元素 → 移除模板项 → 删除其他实例对应元素） ----

    private readonly struct SegmentNodeDeletion
    {
        public readonly string TemplateId;
        public readonly int NodeIndex;

        public SegmentNodeDeletion(string templateId, int nodeIndex)
        {
            TemplateId = templateId;
            NodeIndex = nodeIndex;
        }
    }

    /// <summary>删除生成节点后，把对应模板项移除并同步删除其他实例的对应节点。</summary>
    private void HandleDeletedSegmentNodes(IList<SegmentNodeDeletion> deletions)
    {
        if (deletions == null || targetPlayer == null)
            return;

        Undo.RecordObject(targetPlayer, "Delete Segment Nodes");

        var byTemplate = new Dictionary<string, List<int>>();
        foreach (SegmentNodeDeletion deletion in deletions)
        {
            if (string.IsNullOrEmpty(deletion.TemplateId) || deletion.NodeIndex < 0)
                continue;

            if (!byTemplate.TryGetValue(deletion.TemplateId, out List<int> list))
            {
                list = new List<int>();
                byTemplate[deletion.TemplateId] = list;
            }
            list.Add(deletion.NodeIndex);
        }

        foreach (KeyValuePair<string, List<int>> kv in byTemplate)
        {
            SegmentTemplate template = FindTemplate(kv.Key);
            if (template == null)
                continue;

            List<int> indices = kv.Value;
            indices.Sort();
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                int nodeIndex = indices[i];
                if (nodeIndex < 0 || nodeIndex >= template.nodes.Count)
                    continue;

                template.nodes.RemoveAt(nodeIndex);
                RemoveTemplateNodeFromInstances(template, nodeIndex);
            }
        }
    }

    private void RemoveTemplateNodeFromInstances(SegmentTemplate template, int removedNodeIndex)
    {
        List<RhythmSegment> segments = GetSegments();
        if (segments == null)
            return;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        NodeMarker[] allMarkers = root.GetComponentsInChildren<NodeMarker>(false);

        foreach (RhythmSegment segment in segments)
        {
            if (segment == null || segment.templateId != template.id)
                continue;

            NodeMarker toDestroy = null;
            foreach (NodeMarker marker in allMarkers)
            {
                if (marker == null)
                    continue;
                if (marker.segmentInstanceId == segment.instanceId &&
                    marker.segmentTemplateNodeIndex == removedNodeIndex)
                {
                    toDestroy = marker;
                    break;
                }
            }

            if (toDestroy != null)
                Undo.DestroyObjectImmediate(toDestroy.gameObject);

            foreach (NodeMarker marker in allMarkers)
            {
                if (marker == null)
                    continue;
                if (marker.segmentInstanceId == segment.instanceId &&
                    marker.segmentTemplateNodeIndex > removedNodeIndex)
                {
                    marker.segmentTemplateNodeIndex--;
                }
            }
        }
    }

    /// <summary>删除生成屏幕震动后，把对应模板项移除并同步删除其他实例的对应震动。</summary>
    private void HandleDeletedSegmentVfx(StageVfxEvent deletedVfx)
    {
        if (deletedVfx == null ||
            string.IsNullOrEmpty(deletedVfx.segmentTemplateId) ||
            deletedVfx.segmentTemplateVfxIndex < 0)
        {
            return;
        }

        Undo.RecordObject(targetPlayer, "Delete Segment VFX");

        SegmentTemplate template = FindTemplate(deletedVfx.segmentTemplateId);
        if (template == null || deletedVfx.segmentTemplateVfxIndex >= template.vfx.Count)
            return;

        int vfxIndex = deletedVfx.segmentTemplateVfxIndex;
        template.vfx.RemoveAt(vfxIndex);

        List<RhythmSegment> segments = GetSegments();
        if (segments == null || targetPlayer.VfxEventsEditable == null)
            return;

        foreach (RhythmSegment segment in segments)
        {
            if (segment == null || segment.templateId != template.id)
                continue;

            StageVfxEvent toRemove = null;
            foreach (StageVfxEvent e in targetPlayer.VfxEventsEditable)
            {
                if (e == null)
                    continue;
                if (e.segmentInstanceId == segment.instanceId &&
                    e.segmentTemplateVfxIndex == vfxIndex)
                {
                    toRemove = e;
                    break;
                }
            }

            if (toRemove != null)
                targetPlayer.VfxEventsEditable.Remove(toRemove);

            foreach (StageVfxEvent e in targetPlayer.VfxEventsEditable)
            {
                if (e == null)
                    continue;
                if (e.segmentInstanceId == segment.instanceId &&
                    e.segmentTemplateVfxIndex > vfxIndex)
                {
                    e.segmentTemplateVfxIndex--;
                }
            }
        }
    }

    // ---- 重命名 ----

    private void BeginSegmentRename(RhythmSegment segment)
    {
        if (segment == null || selectedSegmentIndex < 0)
            return;

        renamingSegmentIndex = selectedSegmentIndex;
        SegmentTemplate template = FindTemplate(segment.templateId);
        renameBuffer = template != null ? template.name : "段落";
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
            RhythmSegment segment = segments[renamingSegmentIndex];
            SegmentTemplate template = FindTemplate(segment.templateId);
            if (template != null)
            {
                string name = string.IsNullOrEmpty(renameBuffer) ? "段落" : renameBuffer;
                if (template.name != name)
                {
                    Undo.RecordObject(targetPlayer, "Rename Rhythm Segment");
                    template.name = name;
                    EditorUtility.SetDirty(targetPlayer);
                }
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

    // ---- 工具 ----

    private NodeMarker ResolveNodePrefab()
    {
        if (nodePrefab == null)
            nodePrefab = AssetDatabase.LoadAssetAtPath<NodeMarker>(NodePrefabPath);
        return nodePrefab;
    }
}
