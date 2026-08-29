using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 主菜单按钮：开始新游戏 / 退出游戏。挂到 MainMenu 场景的 Canvas 上。
/// "继续"按钮（Btn_Archive）由 ArchivePanel 负责，这里不处理。
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    public Button btnNewGame;   // "开始跳跃"按钮
    public Button btnQuit;      // "退出"按钮

    private void Awake()
    {
        btnNewGame.onClick.RemoveAllListeners();
        btnNewGame.onClick.AddListener(() =>
        {
            // 开始跳跃：进时间地图选剧情（教程剧情也在里面，从"从新进入"开始）
            SaveSystem.ClearPendingLoad();
            SceneManager.LoadScene("TimeMap");
        });
        btnQuit.onClick.RemoveAllListeners();
        btnQuit.onClick.AddListener(() =>
        {
            // 编辑器里 Application.Quit() 无效（只在打包后的游戏里生效），
            // 所以编辑器环境改用"退出播放模式"，方便测试
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });
    }
    private void Start()
    {
        // 主菜单背景音乐：一千万种可能，循环播放（PlayMusic 默认 loop=true）
        AudioSourceManager.Instance?.PlayMusic("一千万种可能");
        // 首次进入提示（看过后不再弹，PlayerPrefs 记忆）
        HintPanel.ShowIfFirstTime(transform, "Img_Tip", "HintShown_MainMenu",
            "按下开始跳跃键，以进入新的旅程；\n按下读档键，以回到上次被打断的旅程。");
    }
}
