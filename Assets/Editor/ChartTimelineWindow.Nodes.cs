using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private NodeMarker rangeSelectionAnchor;

    private void RequestRepaint()
    {
        if (windowVisible)
            Repaint();
    }

    private void InvalidateMarkerCache()
    {
        markersDirty = true;
        RequestRepaint();
        SceneView.RepaintAll();
    }

    private NodeMarker[] GetMarkers()
    {
        if (targetPlayer == null || targetPlayer.chart == null)
            return System.Array.Empty<NodeMarker>();

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        if (root != cachedMarkerRoot)
        {
            cachedMarkerRoot = root;
            markersDirty = true;
        }

        if (!markersDirty)
            return cachedMarkers;

        cachedMarkers = root.GetComponentsInChildren<NodeMarker>(false);
        System.Array.Sort(cachedMarkers, (a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0 ? byBeat : a.order.CompareTo(b.order);
        });
        markersDirty = false;

        var validMarkers = new HashSet<NodeMarker>(cachedMarkers);
        int previousCount = selection.Count;
        selection.RemoveAll(m => m == null || !validMarkers.Contains(m));
        RebuildSelectionLookup();
        if (selection.Count != previousCount)
            SyncSelectionToUnity();

        return cachedMarkers;
    }

    private bool IsSelected(NodeMarker marker) => selectionLookup.Contains(marker);

    private void AddToSelection(NodeMarker marker)
    {
        if (marker == null || !selectionLookup.Add(marker)) return;
        selection.Add(marker);
    }

    private void RemoveFromSelection(NodeMarker marker)
    {
        if (marker == null || !selectionLookup.Remove(marker)) return;
        selection.Remove(marker);
    }

    private void ClearSelection()
    {
        selection.Clear();
        selectionLookup.Clear();
    }

    private void ReplaceSelection(IEnumerable<NodeMarker> markers)
    {
        ClearSelection();
        foreach (NodeMarker marker in markers)
            AddToSelection(marker);
    }

    private void RebuildSelectionLookup()
    {
        selectionLookup.Clear();
        foreach (NodeMarker marker in selection)
            if (marker != null) selectionLookup.Add(marker);
    }

    private void OnHierarchyChanged() => InvalidateMarkerCache();

    private void OnUndoRedoPerformed() => InvalidateMarkerCache();

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        InvalidateMarkerCache();

        if (state == PlayModeStateChange.ExitingEditMode)
        {
            if (targetPlayer != null &&
                targetPlayer.chart != null &&
                targetPlayer.chart.bpmTimeline != null)
            {
                ChartPlayer.SetEditorPlayStartBeat(
                    targetPlayer,
                    playStartBeat);
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SelectChartOwnerAfterPlayMode();
        }
    }

    private void SelectChartOwnerAfterPlayMode()
    {
        ChartPlayer[] players = Object.FindObjectsByType<ChartPlayer>(
            FindObjectsSortMode.None);
        if (players.Length != 1)
        {
            Debug.LogWarning(
                $"退出 Play Mode 后找到 {players.Length} 个 ChartPlayer，无法确定 Chart 拥有者。");
            return;
        }

        targetPlayer = players[0];
        Selection.activeGameObject = players[0].gameObject;
    }

    private UndoPropertyModification[] OnPostprocessModifications(
        UndoPropertyModification[] modifications)
    {
        foreach (UndoPropertyModification modification in modifications)
        {
            Object target = modification.currentValue.target;
            if (target is NodeMarker ||
                target == targetPlayer ||
                (targetPlayer != null && target == targetPlayer.chart))
            {
                InvalidateMarkerCache();
                if (targetPlayer != null)
                {
                    targetPlayer.QueueUpdatePos(
                        target is NodeMarker marker ? marker.beat : 0f);
                }
                break;
            }
        }
        return modifications;
    }

    private void SelectAllNodes()
    {
        if (targetPlayer == null || targetPlayer.chart == null) return;

        ReplaceSelection(GetMarkers());
        SyncSelectionToUnity();
        RequestRepaint();
        SceneView.RepaintAll();
    }

    private void SelectRangeTo(NodeMarker target, NodeMarker[] markers)
    {
        int targetIndex = System.Array.IndexOf(markers, target);
        if (targetIndex < 0)
            return;

        NodeMarker anchor = ResolveRangeSelectionAnchor(markers);
        if (anchor == null)
        {
            rangeSelectionAnchor = target;
            ReplaceSelection(new[] { target });
            return;
        }

        int anchorIndex = System.Array.IndexOf(markers, anchor);
        if (anchorIndex < 0)
        {
            rangeSelectionAnchor = target;
            ReplaceSelection(new[] { target });
            return;
        }

        int firstIndex = Mathf.Min(anchorIndex, targetIndex);
        int lastIndex = Mathf.Max(anchorIndex, targetIndex);
        ClearSelection();
        for (int i = firstIndex; i <= lastIndex; i++)
            AddToSelection(markers[i]);
        rangeSelectionAnchor = anchor;
    }

    private NodeMarker ResolveRangeSelectionAnchor(NodeMarker[] markers)
    {
        if (rangeSelectionAnchor != null &&
            selectionLookup.Contains(rangeSelectionAnchor) &&
            System.Array.IndexOf(markers, rangeSelectionAnchor) >= 0)
        {
            return rangeSelectionAnchor;
        }

        foreach (NodeMarker marker in selection)
        {
            if (marker != null &&
                System.Array.IndexOf(markers, marker) >= 0)
            {
                return marker;
            }
        }

        return null;
    }

    private void CreateNodeAtPlayhead()
    {
        if (targetPlayer == null || targetPlayer.chart == null) return;

        float beat = Mathf.Max(0f, playheadBeat);
        if (snap > 0f)
            beat = Mathf.Round(beat / snap) * snap;

        CreateNodeAtBeat(beat, GetMarkers(), true);
    }

    private void CreateNodeAtBeat(float beat, NodeMarker[] markers, bool inheritPreviousMode)
    {
        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        NodeMarker previous = null;
        foreach (NodeMarker candidate in markers)
        {
            if (candidate == null) continue;
            if (candidate.beat > beat) break;
            previous = candidate;
        }

        if (nodePrefab == null)
            nodePrefab = AssetDatabase.LoadAssetAtPath<NodeMarker>(NodePrefabPath);
        if (nodePrefab == null)
        {
            Debug.LogError($"Chart Timeline 无法加载 Node Prefab：{NodePrefabPath}");
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(nodePrefab.gameObject, root);
        Undo.RegisterCreatedObjectUndo(go, "Create Node Marker");

        var marker = go.GetComponent<NodeMarker>();
        marker.beat = beat;
        marker.order = 0;
        marker.mode = inheritPreviousMode && previous != null
            ? previous.mode
            : nodePrefab.mode;
        marker.ApplyAppearance(targetPlayer.ResolveNodeAppearanceSet());

        Vector3 position = previous != null ? previous.transform.position : root.position;
        position.x = root.position.x;
        marker.transform.position = position;

        targetPlayer.UpdatePos(beat);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
        PrefabUtility.RecordPrefabInstancePropertyModifications(marker.transform);

        targetPlayer.MarkDirty();
        EditorUtility.SetDirty(targetPlayer);
        Selection.activeGameObject = go;
        InvalidateMarkerCache();
    }

    private void DeleteSelection()
    {
        if (targetPlayer == null || selection.Count == 0) return;

        NodeMarker[] selectedNodes = selection.Where(m => m != null).ToArray();
        if (selectedNodes.Length == 0) return;

        ClearSelection();
        SyncSelectionToUnity();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName($"Delete {selectedNodes.Length} Nodes");
        foreach (NodeMarker marker in selectedNodes)
            Undo.DestroyObjectImmediate(marker.gameObject);
        Undo.CollapseUndoOperations(undoGroup);

        targetPlayer.MarkDirty();
        InvalidateMarkerCache();
        RequestRepaint();
    }

    private void AdjustOrder(int delta)
    {
        if (targetPlayer == null || selection.Count == 0) return;
        Undo.RegisterCompleteObjectUndo(selection.Cast<Object>().ToArray(), "Adjust Order");
        foreach (var m in selection)
        {
            if (m == null) continue;
            m.order += delta;
            EditorUtility.SetDirty(m);
        }
        targetPlayer.MarkDirty();
        InvalidateMarkerCache();
        RequestRepaint();
    }

    private void SyncSelectionToUnity()
    {
        var gameObjects = selection
            .Where(m => m != null)
            .Select(m => (Object)m.gameObject)
            .ToArray();
        var unitySelection = new HashSet<Object>(Selection.objects);
        if (gameObjects.Length == unitySelection.Count &&
            gameObjects.All(unitySelection.Contains))
        {
            return;
        }

        Selection.objects = gameObjects;
    }

    private void OnSelectionChange()
    {
        if (targetPlayer == null) return;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        var newSelection = GetSelectedNodeMarkers()
            .Where(m => m != null && m.transform.IsChildOf(root))
            .Distinct()
            .ToList();
        if (!selectionLookup.SetEquals(newSelection))
            ReplaceSelection(newSelection);

        if (newSelection.Count > 0)
        {
            float minBeat = newSelection.Min(m => m.beat);
            float viewBeats = Mathf.Max(1f, position.width) / pixelsPerBeat;
            if (minBeat < scrollBeat || minBeat > scrollBeat + viewBeats * 0.8f)
                scrollBeat = Mathf.Max(
                    MinScrollBeat,
                    minBeat - viewBeats * 0.2f);
        }

        RequestRepaint();
        SceneView.RepaintAll();
    }

    private static IEnumerable<NodeMarker> GetSelectedNodeMarkers()
    {
        foreach (Object selected in Selection.objects)
        {
            if (selected is GameObject gameObject)
            {
                foreach (NodeMarker marker in
                         gameObject.GetComponentsInChildren<NodeMarker>(true))
                {
                    yield return marker;
                }
            }
            else if (selected is NodeMarker directlySelectedMarker)
            {
                yield return directlySelectedMarker;
            }
        }
    }
}
