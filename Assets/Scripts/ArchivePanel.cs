using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum ArchiveMode
{
    Save,   // 剧情播放器场景：点槽弹出"存档"按钮，写盘
    Load,    // 主菜单场景：点槽弹出"读档"按钮，进入剧情场景
    Inherit  // 时间地图场景：点槽弹出"继承进入"按钮，继承背包/体力/好感度后进入新剧情
}

/// <summary>
/// 存档面板逻辑。两个场景各挂一份：PlotPlayer 用 Save 模式，MainMenu 用 Load 模式。
/// 面板每次打开都重新扫描存档文件夹并刷新槽位（槽位不保存状态，全部由磁盘文件驱动）。
/// 弹出小按钮沿用背包"使用按钮"的机制：全屏透明拦截层 + 槽位上方的小按钮，
/// 点拦截层（=点其他地方）小按钮消失。
/// </summary>
public class ArchivePanel : MonoBehaviour
{
    public static ArchivePanel Instance { get; private set; }
    // ---- 静态跨场景数据 ----
    /// <summary>结局强制存档：存完自动跳转 ExitSceneName（由剧情"结局事件"置位）</summary>
    public static bool ForceSaveAndExit { get; private set; }
    public static string ExitSceneName = "TimeMap";
    /// <summary>Inherit 模式：要进入的新剧情文件名（由 TimeMapManager 置位）</summary>
    public static string PendingStoryFile;
    /// <summary>Inherit 模式：入场券物品 id（0=无要求，由 TimeMapManager 置位）</summary>
    public static int PendingRequiredItemId;
    private bool bound;   // 按钮是否已绑定（惰性：字段就绪才绑，弹窗前强制重绑，防止静态标志泄漏导致漏绑）
    public ArchiveMode mode;
    public GameObject panelGo;       // Panel_Archive
    public Transform slotContainer;  // Panel_Archive 下的 Img_BG（槽位容器，带 GridLayoutGroup）
    public GameObject blockerGo;     // 全屏透明拦截层：点它 = 点其他地方，收起小按钮
    public GameObject actionBtnGo;   // 弹出的小按钮（槽位上方：Btn_SaveArchive / Btn_ReadArchive）
    public Text actionBtnText;       // 上方小按钮上的文字（运行时改成"存档"/"读档"）
    public GameObject extraBtnGo;    // 第二个小按钮（槽位下方：剧情场景=读档，主菜单=删除）
    public Text extraBtnText;        // 下方小按钮上的文字（运行时改成"读档"/"删除"）
    /// <summary>运行时创建的按钮（TimeMap 场景）：弹出/收起小按钮时要自己控制显隐（不是拦截层的子物体）</summary>
    public bool runtimeManagedButtons;
    private bool open;
    private int currentSlotId = -1;  // 当前弹出小按钮对应的槽位，-1 = 没弹出
    public bool IsOpen { get { return open; } }

    private void Awake()
    {
        Instance = this;
        EnsureBound();   // 场景里挂载的面板：字段已就绪，直接绑定
        // 跨场景静态量不在这里复位：TimeMap 场景的面板是运行时挂在"初始未激活"的
        // Panel_Archive 上，Awake 会被推迟到面板第一次被打开那一刻——而那一刻
        // TimeMapManager 刚写完 PendingStoryFile，一复位就把继承目标清空，于是读档
        // 进 PlotPlayer 时 storyName 为空 → 整个场景白屏。复位改由三个场景的入口脚本
        // （GameManager / MainMenuUI / TimeMapManager 的 Awake/Start）调 ResetCrossSceneState()。
    }
    /// <summary>
    /// 复位跨场景静态状态（遗留的强制存档/继承目标会导致面板关不掉、按钮点了没反应，
    /// 表现为"卡死"）。由场景入口脚本在进入场景时调用一次；**不要**放进 Awake，
    /// 原因见 Awake 注释（TimeMap 的面板 Awake 会晚到"面板第一次打开"时）。
    /// </summary>
    public static void ResetCrossSceneState()
    {
        ForceSaveAndExit = false;
        PendingStoryFile = null;
        PendingRequiredItemId = 0;
    }
    /// <summary>
    /// 按钮绑定（惰性 + 幂等）：字段就绪才绑；force=true 时强制重绑。
    /// 不再用静态标志控制——静态标志跨场景泄漏会导致某个场景的 Awake 跳过绑定，
    /// 按钮从此点了没反应。每次弹窗也会强制重绑一遍，保证监听器一定存在。
    /// </summary>
    private void EnsureBound(bool force = false)
    {
        if (bound && !force) return;
        if (blockerGo != null) BindButton(blockerGo, HidePopup);
        if (actionBtnGo != null) BindButton(actionBtnGo, DoAction);
        if (extraBtnGo != null) BindButton(extraBtnGo, DoExtraAction);
        bound = true;
    }

    /// <summary>
    /// 调试探针（排查"按钮点了没反应"用，确认后删除）：
    /// F9 = 打印鼠标当前位置下所有被射线命中的 UI 对象（从上到下）；
    /// F10 = 打印 EventSystem 状态。卡死/无反应时把鼠标放在按钮上按 F9/F10。
    /// </summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            EventSystem es = EventSystem.current;
            if (es == null) { Debug.Log("[UITest] EventSystem.current 为 null"); return; }
            var ped = new PointerEventData(es);
            ped.position = Input.mousePosition;
            var results = new List<RaycastResult>();
            es.RaycastAll(ped, results);
            var sb = new System.Text.StringBuilder();
            sb.Append("[UITest] 鼠标下命中 ").Append(results.Count).Append(" 个:");
            for (int i = 0; i < results.Count; i++)
            {
                Transform p = results[i].gameObject.transform.parent;
                sb.Append("\n  [").Append(i).Append("] ").Append(results[i].gameObject.name)
                    .Append(" <- ").Append(p != null ? p.name : "root");
            }
            Debug.Log(sb.ToString());
        }
        else if (Input.GetKeyDown(KeyCode.F10))
        {
            EventSystem es = EventSystem.current;
            Debug.Log("[UITest] EventSystem=" + (es != null ? es.name : "null")
                + " active=" + (es != null && es.isActiveAndEnabled)
                + " module=" + (es != null && es.currentInputModule != null ? es.currentInputModule.name : "null"));
        }
        else if (Input.GetKeyDown(KeyCode.F11))
        {
            // 直接调用鼠标下第一个 Button 的 onClick（绕过事件系统，测试监听器是否真的绑上了）
            var ped = new PointerEventData(EventSystem.current);
            ped.position = Input.mousePosition;
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, results);
            for (int i = 0; i < results.Count; i++)
            {
                Button b = results[i].gameObject.GetComponentInParent<Button>();
                if (b == null) continue;
                CanvasGroup cg = b.GetComponentInParent<CanvasGroup>();
                Debug.Log("[UITest] F11 鼠标下按钮=" + b.name
                    + " interactable=" + b.interactable
                    + " IsInteractable=" + b.IsInteractable()
                    + " canvasGroup=" + (cg != null ? cg.gameObject.name + "(i=" + cg.interactable + ",b=" + cg.blocksRaycasts + ")" : "无")
                    + " → 直接调用 onClick");
                b.onClick.Invoke();
                return;
            }
            Debug.Log("[UITest] 鼠标下没有 Button");
        }
    }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 运行时组件（TimeMap 场景）初始化：绑定按钮 + 清空槽位容器里的占位网格。
    /// 调用前必须先把 panelGo/slotContainer/blockerGo/actionBtnGo/actionBtnText 赋值好。
    /// </summary>
    public void Init()
    {
        EnsureBound(true);   // 字段刚赋值完，强制绑定
        if (slotContainer != null)
        {
            for (int i = slotContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(slotContainer.GetChild(i).gameObject);
            }
        }
    }

    /// <summary>结局强制存档：打开面板并禁止关闭，存完自动跳转 ExitSceneName</summary>
    public void ForceOpenForEnding()
    {
        ForceSaveAndExit = true;
        open = true;
        RefreshSlots();
        if (panelGo != null) panelGo.SetActive(true);
        if (actionBtnText != null) actionBtnText.text = "存档";
    }

    private void BindButton(GameObject go, UnityAction action)
    {
        if (go == null)
        {
            Debug.LogError("ArchivePanel: 有按钮引用没拖（blockerGo/actionBtnGo），请检查场景绑定", this);
            return;
        }
        Button btn = go.GetComponent<Button>();
        if (btn == null)
        {
            Debug.LogError("ArchivePanel: " + go.name + " 上没有 Button 组件", this);
            return;
        }
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    /// <summary>开关存档面板，挂到 Btn_Archive 的 onClick 上；结局强制存档期间不可关闭</summary>
    public void OpenOrCloseArchive()
    {
        if (ForceSaveAndExit && open) return;   // 结局强制：不许关
        open = !open;
        if (open)
        {
            RefreshSlots();   // 每次打开都重新扫描存档文件
        }
        else
        {
            HidePopup();
        }
        if (panelGo != null) panelGo.SetActive(open);
    }

    /// <summary>整体收起面板与小按钮（菜单按钮收起时由 UIManager 调用）</summary>
    public void Hide()
    {
        if (ForceSaveAndExit && open) return;   // 结局强制：不许关
        if (open) OpenOrCloseArchive();
        else HidePopup();
    }

    /// <summary>扫描存档文件夹并刷新所有槽位显示（复用已有槽位对象，运行中不销毁重建）</summary>
    private void RefreshSlots()
    {
        if (slotContainer == null)
        {
            Debug.LogError("ArchivePanel: 没拖 slotContainer（Panel_Archive 下的 Img_BG）", this);
            return;
        }
        // 复用已有槽位：运行中不 Destroy 对象。
        int existing = slotContainer.childCount;
        for (int slotId = 1; slotId <= SaveSystem.SLOT_COUNT; slotId++)
        {
            ArchiveSlotUI slotUI = slotId <= existing
                ? slotContainer.GetChild(slotId - 1).GetComponent<ArchiveSlotUI>() : null;
            if (slotUI == null)
            {
                slotUI = CreateSlot(slotId);   // 缺哪个建哪个
            }
            slotUI.RefreshText();              // 重新从磁盘读存档并刷新文字
        }
        // 槽位总数调小时清理多余的槽（SLOT_COUNT 不变时走不到这里）
        for (int i = slotContainer.childCount - 1; i >= SaveSystem.SLOT_COUNT; i--)
        {
            Destroy(slotContainer.GetChild(i).gameObject);
        }
        HidePopup();
    }

    private ArchiveSlotUI CreateSlot(int slotId)
    {
        // 槽位 = 底图 + 文字 + 按钮，代码生成，底图与背包格子用同一张 Grid
        GameObject go = new GameObject("ArchiveSlot_" + slotId);
        go.transform.SetParent(slotContainer, false);

        Image img = go.AddComponent<Image>();
        img.sprite = Resources.Load<Sprite>("Sprites/UI/Grid");

        go.AddComponent<Button>();

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        Text text = textGo.AddComponent<Text>();
        text.font = Resources.Load<Font>("Fonts/楷体_GB2312");
        if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 26;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        RectTransform textRT = textGo.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8, 8);
        textRT.offsetMax = new Vector2(-8, -8);

        ArchiveSlotUI slotUI = go.AddComponent<ArchiveSlotUI>();
        slotUI.slotId = slotId;
        slotUI.textInfo = text;
        return slotUI;
    }

    /// <summary>槽位被点击：按模式与占用情况决定是否弹出小按钮</summary>
    public void OnSlotClicked(ArchiveSlotUI slot)
    {
        if (slot == null) return;
        if (mode == ArchiveMode.Save)
        {
            // 保存模式：空白槽可存，已占用槽也可存（点它 = 覆盖旧档）
            ShowPopup(slot);
        }
        else if (slot.occupied)
        {
            // 读档模式：只有已有存档的槽才弹按钮，空槽无反应
            ShowPopup(slot);
        }
    }

    private void ShowPopup(ArchiveSlotUI slot)
    {
        currentSlotId = slot.slotId;
        EnsureBound(true);   // 每次弹窗强制重绑，防止任何原因导致监听器缺失
        if (actionBtnText != null)
        {
            actionBtnText.text = mode == ArchiveMode.Save ? "存档" : mode == ArchiveMode.Inherit ? "继承进入" : "读档";
        }
        if (extraBtnText != null)
        {
            extraBtnText.text = mode == ArchiveMode.Save ? "读档" : "删除";
        }
        if (blockerGo != null)
        {
            blockerGo.SetActive(true);
            // 兜底：拦截层强制挂到 Canvas 直属并置顶，不依赖场景里嵌在哪个层级
            // （不同场景/新旧版本里 Blocker 可能被嵌在任意位置，嵌深了按钮就会被盖住点不到）
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && blockerGo.transform.parent != canvas.transform)
            {
                blockerGo.transform.SetParent(canvas.transform, true);
            }
            blockerGo.transform.SetAsLastSibling();
            RectTransform brt = blockerGo.GetComponent<RectTransform>();
            if (brt != null)
            {
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;
            }
        }
        if (runtimeManagedButtons && actionBtnGo != null) actionBtnGo.SetActive(true);   // 运行时按钮自己控制显隐
        if (actionBtnGo != null)
        {
            // 弹出按钮：不在拦截层下就挪进去（跟着拦截层一起置顶/隐藏），再提到最上层
            if (blockerGo != null && actionBtnGo.transform.parent != blockerGo.transform)
            {
                actionBtnGo.transform.SetParent(blockerGo.transform, true);
            }
            actionBtnGo.transform.SetAsLastSibling();
        }
        // 结局强制存档期间不需要"读档"副按钮；Inherit 模式（TimeMap）也没有副按钮
        if (extraBtnGo != null)
        {
            if (blockerGo != null && extraBtnGo.transform.parent != blockerGo.transform)
            {
                extraBtnGo.transform.SetParent(blockerGo.transform, true);
            }
            extraBtnGo.SetActive(!ForceSaveAndExit && mode != ArchiveMode.Inherit);
        }
        // 槽位格子 200x200：上方按钮向上偏移 130 像素，下方按钮向下偏移 130 像素，形成对称
        Vector3 slotPos = slot.transform.position;
        if (actionBtnGo != null)
        {
            actionBtnGo.transform.position = slotPos + new Vector3(0, 130, 0);
        }
        if (extraBtnGo != null)
        {
            extraBtnGo.transform.position = slotPos + new Vector3(0, -130, 0);
        }
    }

    private void HidePopup()
    {
        if (blockerGo != null) blockerGo.SetActive(false);
        if (runtimeManagedButtons && actionBtnGo != null) actionBtnGo.SetActive(false);   // 运行时按钮自己控制显隐
        currentSlotId = -1;
    }

    private void DoAction()
    {
        if (currentSlotId < 0) return;
        if (mode == ArchiveMode.Save)
        {
            if (GameManager.Instance == null)
            {
                Debug.LogError("ArchivePanel: 保存模式需要 GameManager，但当前场景没有", this);
                return;
            }
            if (AIFreeDialogueFlow.IsInAISegment)
            {
                // 自由对话临时段不在剧本文件里，存档会记到错位索引，先拦住
                UIManager.Instance.ShowToastText("自由对话进行中，先说完这轮再存档吧");
                HidePopup();
                return;
            }
            SaveInfo info = GameManager.Instance.BuildSaveInfo();
            if (ForceSaveAndExit)
            {
                // 结局强制存档：索引记 0（以后读这个档 = 从头再体验该世界线），存完直接进时间地图
                info.scriptIndex = 0;
            }
            SaveSystem.SaveSlot(currentSlotId, info);
            RefreshSlots();   // 刷新槽位显示，HidePopup 由 RefreshSlots 完成
            if (ForceSaveAndExit)
            {
                ForceSaveAndExit = false;
                SceneManager.LoadScene(ExitSceneName);
            }
        }
        else if (mode == ArchiveMode.Inherit)
        {
            if (string.IsNullOrEmpty(PendingStoryFile))
            {
                // 目标剧情文件名丢了就绝不进场景：否则 GameManager 找不到剧情文件 → 白屏
                Debug.LogError("ArchivePanel: 继承进入缺少目标剧情文件名（PendingStoryFile 为空），已取消进入", this);
                HidePopup();
                if (TimeMapManager.Instance != null)
                {
                    TimeMapManager.Instance.ShowMessage("继承目标剧情缺失，无法进入（请重开一次地图再试）");
                }
                return;
            }
            SaveInfo info = SaveSystem.LoadSlot(currentSlotId);
            if (info == null)
            {
                HidePopup();
                return;
            }
            // 入场券检查：继承的背包里必须有所需物品
            if (PendingRequiredItemId > 0 && !SaveHasItem(info, PendingRequiredItemId))
            {
                HidePopup();
                if (TimeMapManager.Instance != null)
                {
                    TimeMapManager.Instance.ShowMessage("这个存档的背包里没有「"
                        + ItemCatalog.GetName(PendingRequiredItemId) + "」，无法进入");
                }
                return;
            }
            // 继承：只带走背包/体力/好感度，环境与进度清掉，以新剧情第 0 条开始
            info.storyName = PendingStoryFile;
            info.scriptIndex = 0;
            info.bgSpriteName = null;
            info.musicName = null;
            info.surroundingName = null;
            info.saveTime = null;
            SaveSystem.pendingLoad = info;
            SceneManager.LoadScene("PlotPlayer");
        }
        else
        {
            SaveInfo info = SaveSystem.LoadSlot(currentSlotId);
            if (info == null)
            {
                HidePopup();
                return;
            }
            if (AIFreeDialogueFlow.IsInAISegment)
            {
                // 自由对话临时段不在剧本文件里，存档会记到错位索引，先拦住
                UIManager.Instance.ShowToastText("自由对话进行中，先说完这轮再存档吧");
                HidePopup();
                return;
            }
            SaveSystem.pendingLoad = info;   // 交给剧情场景的 GameManager 读取
            SceneManager.LoadScene("PlotPlayer");
        }
    }
    /// <summary>
    /// 第二个小按钮的点击：剧情场景=读档（槽位下方），主菜单=删除（槽位下方）。
    /// 剧情场景空白槽位时读档没反应；主菜单空白槽位不会弹按钮。
    /// </summary>
    private void DoExtraAction()
    {
        if (currentSlotId < 0) return;
        if (ForceSaveAndExit) return;   // 结局强制存档期间不提供读档
        if (mode == ArchiveMode.Save)
        {
            // 剧情场景：读档。空白槽位（无存档）直接没反应
            SaveInfo info = SaveSystem.LoadSlot(currentSlotId);
            if (info == null) return;
            if (AIFreeDialogueFlow.IsInAISegment)
            {
                UIManager.Instance.ShowToastText("自由对话进行中，先说完这轮再读档吧");
                HidePopup();
                return;
            }
            SaveSystem.pendingLoad = info;
            SceneManager.LoadScene("PlotPlayer");
        }
        else
        {
            // 主菜单：删除该槽位的存档文件
            SaveSystem.DeleteSlot(currentSlotId);
            RefreshSlots();   // 刷新槽位显示（该槽变"空"），HidePopup 由 RefreshSlots 完成
        }
    }
    private static bool SaveHasItem(SaveInfo info, int itemId)
    {
        if (info.items == null) return false;
        for (int i = 0; i < info.items.Count; i++)
        {
            if (info.items[i].id == itemId) return true;
        }
        return false;
    }
}
