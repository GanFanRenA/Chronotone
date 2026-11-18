#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把旧 NodeType/NodeTrigger 数据一次性迁移成动作 + 重力 + 自动 + 显示。
/// 迁移结果会写回场景（Prefab 实例 override）和 ChartSO / NodeAppearanceSet 资产。
/// 迁移完成后需要再执行一次 “Bake Chart + Track” 重建轨道。
/// </summary>
public static class NodeDataMigration
{
    [MenuItem("Tools/Chronotone/Migrate Node Data (v3)")]
    private static void MigrateNodeData()
    {
        int playerCount = 0;
        foreach (ChartPlayer player in Object.FindObjectsByType<ChartPlayer>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (player == null)
                continue;

            player.EnsureNodeDataMigrated();
            EditorUtility.SetDirty(player);
            playerCount++;
        }

        int chartCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ChartSO"))
        {
            ChartSO chart = AssetDatabase.LoadAssetAtPath<ChartSO>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (chart == null)
                continue;

            chart.EnsureMigrated();
            EditorUtility.SetDirty(chart);
            chartCount++;
        }

        int appearanceCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:NodeAppearanceSet"))
        {
            NodeAppearanceSet appearanceSet =
                AssetDatabase.LoadAssetAtPath<NodeAppearanceSet>(
                    AssetDatabase.GUIDToAssetPath(guid));
            if (appearanceSet == null)
                continue;

            appearanceSet.EnsureMigrated();
            EditorUtility.SetDirty(appearanceSet);
            appearanceCount++;
        }

        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"节点数据迁移完成：{playerCount} 个 ChartPlayer、{chartCount} 个 ChartSO、" +
            $"{appearanceCount} 个 NodeAppearanceSet。\n" +
            "请再点一次 ChartPlayer 的 “Bake Chart + Track”，或直接进入 Play 让轨道自动重烘。");
    }
}
#endif
