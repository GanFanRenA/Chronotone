using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private void ImportBeatsFromCsv()
    {
        if (targetPlayer == null || targetPlayer.chart == null) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "无法导入",
                "请在退出 Play 模式后再导入 CSV。",
                "确定");
            return;
        }

        string path = EditorUtility.OpenFilePanel("导入 Beat CSV", string.Empty, "csv");
        if (string.IsNullOrEmpty(path)) return;

        if (!TryReadBeatCsv(path, out List<float> beats, out string error))
        {
            EditorUtility.DisplayDialog("CSV 导入失败", error, "确定");
            return;
        }

        NodeMarker prefab = targetPlayer.nodePrefab != null
            ? targetPlayer.nodePrefab
            : nodePrefab;
        if (prefab == null)
            prefab = AssetDatabase.LoadAssetAtPath<NodeMarker>(NodePrefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog(
                "CSV 导入失败",
                $"无法加载 Node Prefab：{NodePrefabPath}",
                "确定");
            return;
        }

        Transform root = targetPlayer.scanRoot != null
            ? targetPlayer.scanRoot
            : targetPlayer.transform;
        NodeMarker[] existingMarkers =
            root.GetComponentsInChildren<NodeMarker>(false);
        if (TryFindDuplicateImportedBeat(
                beats,
                existingMarkers,
                out float duplicateBeat,
                out string conflictDescription))
        {
            EditorUtility.DisplayDialog(
                "CSV 导入失败",
                conflictDescription +
                $"\nBeat {duplicateBeat:0.###} 已存在或重复，禁止双压。",
                "确定");
            return;
        }

        int createdCount = CreateImportedBeatNodes(beats, prefab);
        Debug.Log(
            $"Chart Timeline: 已从 CSV 导入 {createdCount} 个 beat：" +
            $"{Path.GetFileName(path)}");
        ShowNotification(new GUIContent($"已导入 {createdCount} 个 beat"));
    }

    private static bool TryReadBeatCsv(
        string path,
        out List<float> beats,
        out string error)
    {
        beats = new List<float>();
        error = string.Empty;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            int lineNumber = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                string value = line.Trim();
                if (value.Length == 0) continue;

                if (value.Length >= 2 &&
                    value[0] == '"' &&
                    value[value.Length - 1] == '"')
                {
                    value = value.Substring(1, value.Length - 2).Trim();
                }

                bool parsed =
                    float.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float beat) ||
                    float.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out beat);

                if (!parsed)
                {
                    error = $"第 {lineNumber} 行不是有效数字：{value}";
                    return false;
                }
                if (float.IsNaN(beat) || float.IsInfinity(beat))
                {
                    error = $"第 {lineNumber} 行不是有限数字：{value}";
                    return false;
                }
                if (beat < 0f)
                {
                    error = $"第 {lineNumber} 行的 beat 不能为负数：{value}";
                    return false;
                }

                beats.Add(beat);
            }
        }
        catch (Exception exception)
        {
            error = $"无法读取文件：\n{exception.Message}";
            return false;
        }

        if (beats.Count == 0)
        {
            error = "CSV 中没有可导入的数字。每行填写一个 beat 值，空行会被忽略。";
            return false;
        }

        return true;
    }

    private static bool TryFindDuplicateImportedBeat(
        List<float> beats,
        NodeMarker[] existingMarkers,
        out float duplicateBeat,
        out string conflictDescription)
    {
        duplicateBeat = 0f;
        conflictDescription = string.Empty;

        float[] sortedBeats = beats.OrderBy(beat => beat).ToArray();
        for (int i = 1; i < sortedBeats.Length; i++)
        {
            if (Mathf.Abs(sortedBeats[i] - sortedBeats[i - 1]) >
                DuplicateBeatEpsilon)
            {
                continue;
            }

            duplicateBeat = sortedBeats[i];
            conflictDescription = "CSV 内包含相同 Beat。";
            return true;
        }

        NodeMarker[] sortedMarkers = existingMarkers
            .Where(marker => marker != null)
            .OrderBy(marker => marker.beat)
            .ToArray();
        int markerIndex = 0;
        foreach (float beat in sortedBeats)
        {
            while (markerIndex < sortedMarkers.Length &&
                   sortedMarkers[markerIndex].beat <
                   beat - DuplicateBeatEpsilon)
            {
                markerIndex++;
            }

            if (markerIndex < sortedMarkers.Length &&
                Mathf.Abs(sortedMarkers[markerIndex].beat - beat) <=
                DuplicateBeatEpsilon)
            {
                duplicateBeat = beat;
                conflictDescription = "当前谱面中已经有相同 Beat 的节点。";
                return true;
            }
        }

        return false;
    }

    private int CreateImportedBeatNodes(List<float> beats, NodeMarker prefab)
    {
        Transform root = targetPlayer.scanRoot != null
            ? targetPlayer.scanRoot
            : targetPlayer.transform;

        NodeMarker[] existingMarkers = root.GetComponentsInChildren<NodeMarker>(false);
        var nextOrderByBeat = new Dictionary<float, int>();
        foreach (NodeMarker marker in existingMarkers)
        {
            if (marker == null) continue;
            nextOrderByBeat.TryGetValue(marker.beat, out int nextOrder);
            nextOrderByBeat[marker.beat] = Mathf.Max(nextOrder, marker.order + 1);
        }

        List<float> orderedBeats = beats
            .Select((beat, sourceIndex) => new { beat, sourceIndex })
            .OrderBy(item => item.beat)
            .ThenBy(item => item.sourceIndex)
            .Select(item => item.beat)
            .ToList();

        NodeMarker[] sortedExisting = existingMarkers
            .Where(marker => marker != null)
            .OrderBy(marker => marker.beat)
            .ThenBy(marker => marker.order)
            .ToArray();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName($"Import {orderedBeats.Count} Beats From CSV");

        var importedMarkers = new List<NodeMarker>(orderedBeats.Count);
        int existingCursor = 0;
        NodeMarker previous = null;
        foreach (float beat in orderedBeats)
        {
            while (existingCursor < sortedExisting.Length &&
                   sortedExisting[existingCursor].beat <= beat)
            {
                previous = sortedExisting[existingCursor];
                existingCursor++;
            }

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab.gameObject,
                root);
            if (go == null)
            {
                Debug.LogError($"Chart Timeline 无法实例化 Node Prefab：{prefab.name}");
                break;
            }

            Undo.RegisterCreatedObjectUndo(go, "Import Beat From CSV");

            NodeMarker marker = go.GetComponent<NodeMarker>();
            marker.beat = beat;
            nextOrderByBeat.TryGetValue(beat, out int order);
            marker.order = order;
            nextOrderByBeat[beat] = order + 1;
            marker.mode = previous != null ? previous.mode : prefab.mode;
            marker.ApplyAppearance(targetPlayer.ResolveNodeAppearanceSet());

            Vector3 position = previous != null ? previous.transform.position : root.position;
            position.x = root.position.x;
            marker.transform.position = position;

            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker.transform);
            EditorUtility.SetDirty(marker);

            importedMarkers.Add(marker);
            previous = marker;
        }

        if (orderedBeats.Count > 0)
            targetPlayer.UpdatePos(orderedBeats[0]);

        Undo.CollapseUndoOperations(undoGroup);

        targetPlayer.MarkDirty();
        EditorUtility.SetDirty(targetPlayer);
        InvalidateMarkerCache();

        ReplaceSelection(importedMarkers);
        SyncSelectionToUnity();
        SceneView.RepaintAll();
        RequestRepaint();

        return importedMarkers.Count;
    }
}
