using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 播放模式诊断工具（仅编辑器用，放 Editor 文件夹下不会打进游戏）。
/// 进入/退出播放模式时打印当前选中对象与场景里的损坏对象，
/// 用于定位 GameObjectInspector 报 NullReference / SerializedObjectNotCreatable 的根因：
/// 这类报错通常是 Inspector 正在显示一个已销毁的对象（如上一次游玩生成的运行时槽位/物品）。
/// </summary>
[InitializeOnLoad]
public static class PlayModeDiagnostics
{
    static PlayModeDiagnostics()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Debug.Log("[诊断] ===== 进入播放模式,场景: " + SceneManager.GetActiveScene().name + " =====");
            LogSelection("进入播放模式时选中:");
            ScanSceneForMissingScripts();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            // 退出播放模式:运行时对象此时已被销毁,若它们还被选中,正是 Inspector 报错的来源
            LogSelection("退出播放模式时选中:");
            ClearDeadSelection();
        }
    }

    private static void LogSelection(string title)
    {
        Object[] objs = Selection.objects;
        if (objs == null || objs.Length == 0)
        {
            Debug.Log("[诊断] " + title + " (无)");
            return;
        }
        Debug.Log("[诊断] " + title);
        foreach (Object o in objs)
        {
            if (o == null)
            {
                Debug.LogWarning("[诊断]   <null> —— 选中的是已销毁的运行时对象!这就是 Inspector 报错来源," +
                                 "解锁 Inspector(右上角锁图标)或改选任意正常对象即可消除报错");
            }
            else
            {
                Debug.Log("[诊断]   " + o.name + " (" + o.GetType().Name + ")");
            }
        }
    }

    /// <summary>选中对象里有死对象时自动清除选择,避免 Inspector 持续报错</summary>
    private static void ClearDeadSelection()
    {
        bool hasNull = false;
        foreach (Object o in Selection.objects)
        {
            if (o == null) { hasNull = true; break; }
        }
        if (hasNull)
        {
            Selection.objects = new Object[0];
            Selection.activeObject = null;
            Debug.Log("[诊断] 已自动清除包含已销毁对象的选择");
        }
    }

    private static void ScanSceneForMissingScripts()
    {
        int missingCount = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                missingCount += ScanTree(root);
            }
        }
        if (missingCount == 0)
        {
            Debug.Log("[诊断] 场景扫描完成:没有发现缺组件(Missing Script)的对象");
        }
    }

    private static int ScanTree(GameObject go)
    {
        int count = 0;
        // 缺组件的对象在 GetComponents 结果里表现为 null 元素
        Component[] comps = go.GetComponents<Component>();
        foreach (Component c in comps)
        {
            if (c == null)
            {
                Debug.LogWarning("[诊断] 缺组件(Missing Script)对象: " + go.name, go);
                count++;
            }
        }
        for (int i = 0; i < go.transform.childCount; i++)
        {
            count += ScanTree(go.transform.GetChild(i).gameObject);
        }
        return count;
    }
}
