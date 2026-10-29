using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 对话回顾（backlog）面板：把"已经播出去的对话/旁白"按顺序列出来，供玩家回看。
///
/// 挂法：脚本挂在**场景里处于激活状态**的物体上（推荐 Canvas），面板本体（Panel_History）
/// 在场景里初始保持未激活，由本脚本开关——不要挂在未激活的面板上，否则 Awake 会被推迟到
/// 面板第一次打开的那一刻才执行（TimeMap 的 ArchivePanel 就是踩过这个坑）。
///
/// 数据来源：UIManager.UpdateTalkLineText 每播一条就调 Record()，所以只记"实际播过的"，
/// 剧本里没走到的分支不会出现。记录只在内存里（进新剧情/读档时清空），不进存档。
/// </summary>
public class DialogueHistoryPanel : MonoBehaviour
{
    public static DialogueHistoryPanel Instance { get; private set; }

    /// <summary>回顾记录（"名字：台词"，按播放顺序）。静态：面板没激活也能记录</summary>
    private static readonly List<string> lines = new List<string>();
    private const int MAX_LINES = 300;   // 上限，超出丢最早的

    public GameObject panelGo;      // Panel_History（场景里初始未激活）
    public Text contentText;        // Text_History（滚动区里的正文）
    public ScrollRect scrollRect;   // ScrollArea 上的 ScrollRect
    public Button closeBtn;         // Btn_Close

    private bool open;
    public bool IsOpen { get { return open; } }

    private void Awake()
    {
        Instance = this;
        if (closeBtn != null)
        {
            closeBtn.onClick.RemoveAllListeners();
            closeBtn.onClick.AddListener(Close);
        }
        if (panelGo != null) panelGo.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============ 记录（由 UIManager 每播一条调用） ============

    /// <summary>记一条已经播出去的对话/旁白（空文本自动跳过）</summary>
    public static void Record(string name, string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        lines.Add(string.IsNullOrEmpty(name) ? content : name + "：" + content);
        if (lines.Count > MAX_LINES)
        {
            lines.RemoveRange(0, lines.Count - MAX_LINES);
        }
        if (Instance != null && Instance.open) Instance.RefreshText();   // 开着时实时更新
    }

    /// <summary>清空记录（进入剧情场景时由 GameManager 调用）</summary>
    public static void ClearLog()
    {
        lines.Clear();
    }

    // ============ 开关（挂到 Btn_History 的 onClick） ============

    public void OpenOrCloseHistory()
    {
        if (open) Close(); else Open();
    }

    public void Open()
    {
        if (panelGo == null) return;
        open = true;
        RefreshText();
        panelGo.SetActive(true);
        panelGo.transform.SetAsLastSibling();   // 盖在其他 UI 之上，保证读得到
        StopAllCoroutines();
        StartCoroutine(ScrollToBottomNextFrame());
    }

    public void Close()
    {
        open = false;
        if (panelGo != null) panelGo.SetActive(false);
    }

    /// <summary>整体收起（菜单按钮收起时由 UIManager 调用）</summary>
    public void Hide()
    {
        if (open) Close();
    }

    // ============ 内部 ============

    private void RefreshText()
    {
        if (contentText == null) return;
        if (lines.Count == 0)
        {
            contentText.text = "（这场剧情还没有播过对话）";
            return;
        }
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]).Append('\n').Append('\n');
        }
        contentText.text = sb.ToString();
    }

    /// <summary>
    /// 打开后滚到最底部（最新一句）。ContentSizeFitter 要等一帧才算出高度，所以等一帧再滚。
    /// ScrollRect 的 Content 没在 Inspector 里拖时，这里自动用 contentText 补上；补不上就安静跳过，
    /// 绝不因为没配全而抛 UnassignedReferenceException（那会让整个打开流程中断）。
    /// </summary>
    private IEnumerator ScrollToBottomNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (scrollRect == null) yield break;
        try
        {
            if (scrollRect.content == null && contentText != null)
            {
                scrollRect.content = contentText.rectTransform;   // 兜底：没拖 Content 就自己补上
            }
            if (scrollRect.content == null)
            {
                Debug.LogWarning("DialogueHistoryPanel: ScrollRect 没有 Content，本次不自动滚到底"
                    + "（检查 ScrollArea 的 ScrollRect → Content 是否拖了 Text_History）");
                yield break;
            }
            scrollRect.verticalNormalizedPosition = 0f;   // 0 = 底部
        }
        catch (System.Exception e)
        {
            // ScrollRect 没配全时 Unity 抛的是 UnassignedReferenceException，这里吞掉并给出定位提示
            Debug.LogWarning("DialogueHistoryPanel: 自动滚到底失败（检查 ScrollArea 的 ScrollRect → Content / Viewport）：" + e.Message);
        }
    }
}
