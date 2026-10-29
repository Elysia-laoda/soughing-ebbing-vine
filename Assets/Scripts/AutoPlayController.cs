using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全局"自动播放"开关（挂在剧情播放器场景的 Btn_Auto 上，也可由 GameManager 运行时挂载）。
/// 开启后：每条对话台词显示时，等"语音播完 + 至少停留 minHoldSeconds"就自动切下一条。
/// 监测 audio 活性 = 轮询 AudioSourceManager.dialogueAudio.isPlaying（PlayOneShot 播完即 false）；
/// 没有语音的台词（旁白/苏回缺音频）isPlaying 一直是 false，靠最短停留时间兜底，不会闪切。
/// 自动推进的防守：选项面板/自由对话面板/存档面板打开时、剧情已结局时不自动推进。
/// </summary>
public class AutoPlayController : MonoBehaviour
{
    public static bool Enabled { get; private set; }

    public float minHoldSeconds = 1.2f;   // 最短停留时间（无语音台词也至少停留这么久）
    public Color onColor = new Color(1f, 0.85f, 0.45f);   // 开启高亮色
    public Color offColor = new Color(0.85f, 0.85f, 0.85f);

    private static float lastLineShownTime = -999f;

    private Button btn;
    private Graphic highlight;   // 用按钮自身 Image 做开关高亮

    /// <summary>剧情每处理一条就调用一次，作为"当前台词开始显示"的计时基准（由 GameManager.HandleData 调用）</summary>
    public static void MarkLineShown()
    {
        lastLineShownTime = Time.time;
    }

    private void Awake()
    {
        btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(Toggle);
        }
        highlight = GetComponent<Graphic>();
        ApplyHighlight();
    }

    private void Toggle()
    {
        Enabled = !Enabled;
        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        if (highlight != null) highlight.color = Enabled ? onColor : offColor;
    }

    private void Update()
    {
        if (!Enabled) return;
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.storyEnded) return;   // 结局事件后收手
        UIManager ui = UIManager.Instance;
        if (ui == null) return;
        if (!ui.talkLineGo.activeSelf) return;             // 对话框没显示（背景/事件/视频中）
        if (ui.empChoiceUIGo.activeSelf) return;           // 选项面板开着：等玩家选
        if (AIDialoguePanel.Instance != null && AIDialoguePanel.Instance.gameObject.activeSelf) return;  // AI自由对话面板开着
        if (ArchivePanel.Instance != null && ArchivePanel.Instance.IsOpen) return;   // 存档面板开着
        if (DialogueHistoryPanel.Instance != null && DialogueHistoryPanel.Instance.IsOpen) return;   // 对话回顾开着：先看完再推进
        if (Time.time - lastLineShownTime < minHoldSeconds) return;
        if (AudioSourceManager.Instance != null
            && AudioSourceManager.Instance.dialogueAudio != null
            && AudioSourceManager.Instance.dialogueAudio.isPlaying) return;   // 语音还没播完
        gm.LoadNextScript();
    }
}

