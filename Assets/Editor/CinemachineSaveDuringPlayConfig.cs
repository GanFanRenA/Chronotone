using UnityEditor;
using Unity.Cinemachine.Editor;

/// <summary>
/// 关闭 Cinemachine 的 “Save During Play” 功能：
/// 退出 Play Mode 时不再弹出“是否保存 CinemachineCamera 设置”对话框，
/// 运行期间对相机 / 噪声等组件做的修改会被丢弃，默认不写回场景。
/// </summary>
[InitializeOnLoad]
public static class CinemachineSaveDuringPlayConfig
{
    static CinemachineSaveDuringPlayConfig()
    {
        // SaveDuringPlay.Enabled 是 Cinemachine 的全局开关（EditorPrefs 键 SaveDuringPlay_Enabled）。
        // 设为 false 后，OnPlayStateChanged 不再收集 / 恢复运行期间的修改，也就不会再弹保存对话框。
        SaveDuringPlay.Enabled = false;
    }
}
