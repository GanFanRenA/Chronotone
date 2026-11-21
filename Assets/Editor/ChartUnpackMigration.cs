using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一次性迁移：把场景里的 Chart 预制体实例解包成普通场景对象。
/// ChartPlayer / StageVfxDriver 上的 clip、bpm、vfxEvents、brain/perlin/player、bakedData
/// 等关卡级数据之后直接序列化在场景里，不再作为预制体 override，避免场景文件膨胀。
/// </summary>
public static class ChartUnpackMigration
{
    private const string ChartPrefabPath = "Assets/Prefabs/Scene/Chart.prefab";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/Music/Glaxiation.unity",
        "Assets/Scenes/Music/HotAbnormal(Remix).unity",
    };

    [MenuItem("Window/Rhythm/迁移：Chart 解包为场景对象")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("请先退出 Play 模式再执行迁移。");
            return;
        }

        // 先保存当前打开的场景，避免切场景时丢失未保存改动。
        if (EditorSceneManager.GetActiveScene().isDirty)
            EditorSceneManager.SaveOpenScenes();

        int unpacked = 0;
        foreach (string path in ScenePaths)
        {
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"Chart 解包：跳过不存在的场景 {path}");
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            var chartRoots = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null)
                    continue;

                if (string.Equals(
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root),
                        ChartPrefabPath,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    chartRoots.Add(root);
                }
            }

            if (chartRoots.Count == 0)
            {
                Debug.Log($"Chart 解包：场景 {scene.name} 没有 Chart 预制体实例，跳过。");
                continue;
            }

            foreach (GameObject root in chartRoots)
            {
                // 先把旧的内联烘焙数组搬到独立资产，避免解包时把 1.6MB 数据抄进场景。
                ChartPlayer player = root.GetComponentInChildren<ChartPlayer>(true);
                if (player != null && player.HasLegacyBakedData)
                    player.MigrateLegacyBakedData();

                PrefabUtility.UnpackPrefabInstance(
                    root,
                    PrefabUnpackMode.Completely,
                    InteractionMode.UserAction);
                unpacked++;
                Debug.Log($"Chart 解包：{scene.name} 的 “{root.name}” 已变成普通场景对象。", root);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // 两个场景都解包后再删预制体，避免留下悬空引用。
        if (AssetDatabase.LoadAssetAtPath<Object>(ChartPrefabPath) != null)
        {
            AssetDatabase.DeleteAsset(ChartPrefabPath);
            Debug.Log($"Chart 解包：已删除 {ChartPrefabPath}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Chart 解包迁移完成，共解包 {unpacked} 个实例。");
    }
}
