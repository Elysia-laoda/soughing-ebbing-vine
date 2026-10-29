using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    public Image imgBG;
    public Image imgCharacter;
    public Image imgCharacter2;
    public Button btnLoadNext;
    public Text textName;//人物名字
    public Text textTalkLine;//对话内容
    public GameObject talkLineGo;//对话框父对象
    public Transform[] characterPosTrans;//人物位置
    public Text textEnergyValue;//体力值文本
    public GameObject imgEnergy;//体力值UI对象
    public Text textFavorValue;//好感度值文本
    public GameObject imgFavorability;//好感度UI对象
    public GameObject empChoiceUIGo;//选择选项框UI父对象
    public GameObject[] choiceUIGos;//选择选项框UI子对象
    public Text[] textChoiceUIs;//选择选项文本
    private bool toNextScene;//是否跳转到下一场景
    //private bool showMask;//是否显示遮罩
    public Image mask;//遮罩图片
    //private Image ImageTween;//角色遮罩
    private List<UIInofo> imageTweenList;
    public GameObject bagPanelGo;//背包UI父对象
    public bool openBag;
    private List<ItemInfo> itemTemplates;
    public Transform imgBagBG;
    private Dictionary<int, GameObject> itemBtnGosDict;
    public GameObject tipInfoGo;//详细信息栏
    public Text textTipInfo;
    public GameObject useBtnBlockerGo;   // Blocker_Use
    public GameObject btnUseGo;          // Btn_Use使用按钮
    private ItemInfo currentUseItem;     // 当前目标物品
    private ItemInfo currentHoverItem;
    public GameObject toastGo;     // Toast_Item（场景里拖引用）
    public Image imgToastIcon;     // Img_ToastIcon
    public Text textToast;         // Text_Toast
    private Coroutine toastCoroutine;   // 计时器句柄，用来"重新计时"
    public GameObject[] functionBtnGos;//菜单按钮管理的功能按钮组(目前只有背包按钮)
    private bool functionBtnsOpen;//功能按钮是否处于展开状态
    // 立绘状态快照用：记录每个立绘槽最近一次用过的资源路径/位置/翻转（下标 0/1 = characterID 0/1）
    private readonly string[] charSpritePaths = new string[2];
    private readonly int[] charPosIds = new int[2];
    private readonly bool[] charRotates = new bool[2];
    // 立绘刷新过渡：已出现的立绘先淡出到 75%、在最暗处换图、再淡回 100%
    // （每半程 CharRefreshHalfSeconds = 0.2 秒，一次刷新共 0.4 秒）
    private readonly Coroutine[] charRefreshRoutines = new Coroutine[2];
    private const float CharRefreshHalfSeconds = 0.2f;         // 半程时长；一次刷新 = 两程 = 0.4 秒
    private const float CharRefreshDimAlpha = 0.75f;           // 最暗时保留的不透明度（只淡出 25%）
    private const float CharRefreshVisibleThreshold = 0.5f;    // 判定"已出现立绘"的门槛，与过渡幅度无关
    private void Awake()
    {
        Instance = this;
        imageTweenList = new List<UIInofo>();
        itemTemplates = ItemCatalog.Items;
        itemBtnGosDict = new Dictionary<int, GameObject>();
    }
    public void AddItem(ItemInfo itemInfo, int addCount = 1, bool showToast = true)
    {
        // 路径1：背包里已有这种物品 → 只加数量，不新建按钮（原 bug 顺手修掉）
        if (itemBtnGosDict.ContainsKey(itemInfo.id))
        {
            ItemUI ui = itemBtnGosDict[itemInfo.id].GetComponent<ItemUI>();
            ui.itemInfo.count += addCount;
            RefreshTipIfShowing(ui.itemInfo);
            if (showToast) ShowItemToast(ui.itemInfo, "得到了");   // 需求4的提示框，这里先留口子
            return;
        }

        // 路径2：新物品 → 深拷贝模板后新建按钮
        ItemInfo newItem = new ItemInfo
        {
            name = itemInfo.name,
            id = itemInfo.id,
            instruction = itemInfo.instruction,
            src = itemInfo.src,
            count = addCount
        };
        Button btnItem = Instantiate(Resources.Load<GameObject>("Prefabs/Items/Btn_Item")).GetComponent<Button>();
        btnItem.transform.SetParent(imgBagBG);
        btnItem.GetComponent<Image>().sprite = Resources.Load<Sprite>("Sprites/Items/" + newItem.id);
        btnItem.GetComponent<ItemUI>().itemInfo = newItem;   // 绑定的是副本，不是模板
        itemBtnGosDict.Add(newItem.id, btnItem.gameObject);
        if (showToast) ShowItemToast(newItem, "得到了");
    }
    /// <summary>新游戏/旧格式存档：背包从空开始（设计定稿：新档不再送默认物品），并销毁旧背包按钮</summary>
    public void ClearBag()
    {
        foreach (var kv in itemBtnGosDict)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        itemBtnGosDict.Clear();
    }
    /// <summary>读档：按存档快照精确重建背包（物品种类与数量还原）</summary>
    public void RestoreBag(List<ItemSaveData> items)
    {
        foreach (var kv in itemBtnGosDict)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        itemBtnGosDict.Clear();
        if (items == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            ItemSaveData sd = items[i];
            ItemInfo template = itemTemplates.Find(t => t.id == sd.id);
            if (template == null)
            {
                Debug.LogWarning("存档里有未知物品id: " + sd.id);
                continue;
            }
            AddItem(template, sd.count, false);
        }
    }
    /// <summary>存档：导出当前背包快照（物品 id + 数量）</summary>
    public List<ItemSaveData> GetBagSnapshot()
    {
        List<ItemSaveData> list = new List<ItemSaveData>();
        foreach (var kv in itemBtnGosDict)
        {
            ItemUI ui = kv.Value.GetComponent<ItemUI>();
            list.Add(new ItemSaveData { id = ui.itemInfo.id, count = ui.itemInfo.count });
        }
        return list;
    }
    public void AddItemByID(int id, int count = 1)
    {
        ItemInfo template = itemTemplates.Find(t => t.id == id);
        if (template == null)
        {
            Debug.LogWarning("没有这个物品: " + id);
            return;
        }
        AddItem(template, count);
    }
    public void RemoveItem(int id)
    {
        //从背包UI中移除物品，即从字典中删除对应的物品数据
        if (itemBtnGosDict.ContainsKey(id))
        {
            Destroy(itemBtnGosDict[id]);
            itemBtnGosDict.Remove(id);
        }
    }
    public void SetBGImageSprite(string spriteName)
    {
        //设置背景图片（资源区按剧情分目录：Sprites/BG/<剧情>/<图名>，旧结构自动兜底）
        imgBG.sprite = ResourcePaths.LoadBG(CurrentStory(), spriteName);
    }
    /// <summary>当前剧情名（资源按剧情分目录）；没有 GameManager 时返回空串，走旧结构兜底</summary>
    private static string CurrentStory()
    {
        return GameManager.Instance != null ? GameManager.Instance.currentStoryName : "";
    }
    /// <summary>
    /// 按 "角色名/表情" 组合串载入立绘（自动带上当前剧情子目录）。
    /// 表情里带 @ 时表示跨剧情强引用，例如 "白莶/@Tutorial/normal"。
    /// </summary>
    private static Sprite LoadCharacterSprite(string combined)
    {
        if (string.IsNullOrEmpty(combined)) return null;
        int cut = combined.IndexOf('/');
        if (cut <= 0) return null;
        string charName = combined.Substring(0, cut);
        string emotion = combined.Substring(cut + 1);
        return ResourcePaths.LoadCharacter(CurrentStory(), charName, emotion);
    }
    public void ShowCharacter(string name, int characterID = 0, string emotion = "normal")
    {
        //显示对话框、人物与名字
        if (string.IsNullOrEmpty(emotion)) emotion = "normal";
        btnLoadNext.interactable = true;
        talkLineGo.SetActive(false);
        empChoiceUIGo.SetActive(false);
        textName.text = name;
        int slot = characterID == 0 ? 0 : 1;
        Image img = characterID == 0 ? imgCharacter : imgCharacter2;
        if (img == null) return;
        string spritePath = name + "/" + emotion;
        string previousPath = charSpritePaths[slot];   // 上一次这一槽用的图
        charSpritePaths[slot] = spritePath;            // 记进快照，读档时原样还原
        // 判断这一槽"已出现立绘"还是"未出现立绘"：
        //   已出现 = 当前可见（不透明度超过门槛）**且这次真的是在换图**（角色或表情变了）。
        // 同图重复刷新（例如连续两句同表情）不过渡，否则每句台词都要眨一下。
        // 想让"每次刷新都眨"就去掉 spriteChanged 这个条件。
        bool spriteChanged = previousPath != spritePath;
        bool alreadyShown = img.color.a > CharRefreshVisibleThreshold && spriteChanged;
        img.gameObject.SetActive(true);
        if (alreadyShown)
        {
            // 已出现：淡出到 75%（只暗 25%）→ 最暗处换图 → 淡回 100%（共 0.4 秒）
            StartCharRefreshTransition(img, slot, spritePath);
        }
        else
        {
            // 未出现（首次出场 / 当前不可见）：不做过场，直接换图 + 补淡入
            StopCharRefresh(slot);   // 万一上一槽的过渡还在跑（例如正好淡到 50% 时又刷新），先停掉它
            img.sprite = LoadCharacterSprite(spritePath);
            FadeInCharacterIfInvisible(img);
        }
    }
    /// <summary>
    /// 立绘在场景里初始透明度为 0，正常靠剧情 eventID 5 淡入。
    /// 若角色当前仍完全透明（读档直接落到对话句、或角色被隐藏后重新开口），自动淡入补上；
    /// 已经可见或正在淡入时跳过，不干扰剧情里的显隐控制。
    /// </summary>
    private void FadeInCharacterIfInvisible(Image charImage)
    {
        if (charImage.color.a > 0.01f) return;
        DoShowOrHideUITeen(true, false, 1.5f, charImage.gameObject);
    }
    /// <summary>
    /// 立绘刷新过渡：从当前不透明度淡出到 50%，在最暗处换图，再淡回 100%
    /// （每半程 0.2 秒，一次刷新共 0.4 秒）。只用于"已经在场的立绘"。
    /// </summary>
    private void StartCharRefreshTransition(Image img, int slot, string spritePath)
    {
        if (slot < 0 || slot >= charRefreshRoutines.Length) return;
        if (charRefreshRoutines[slot] != null) StopCoroutine(charRefreshRoutines[slot]);   // 同一槽位上一次过渡作废
        CancelTweenOn(img.gameObject);   // 别和淡入淡出 tween 抢同一个 alpha
        charRefreshRoutines[slot] = StartCoroutine(CharRefreshRoutine(img, slot, spritePath));
    }
    /// <summary>停掉某个立绘槽正在跑的刷新过渡（显隐切换、读档还原、换角色前调用）</summary>
    private void StopCharRefresh(int slot)
    {
        if (slot < 0 || slot >= charRefreshRoutines.Length) return;
        if (charRefreshRoutines[slot] != null)
        {
            StopCoroutine(charRefreshRoutines[slot]);
            charRefreshRoutines[slot] = null;
        }
    }
    private IEnumerator CharRefreshRoutine(Image img, int slot, string spritePath)
    {
        float from = img.color.a;
        float t = 0f;
        while (t < CharRefreshHalfSeconds)   // 半程一：淡出到 50%
        {
            t += Time.deltaTime;
            SetCharacterAlpha(img, Mathf.Lerp(from, CharRefreshDimAlpha, t / CharRefreshHalfSeconds));
            yield return null;
        }
        SetCharacterAlpha(img, CharRefreshDimAlpha);// 最暗点：保留 75% 不透明度
        // 换图藏在这段最暗的位置，看不到硬切
        Sprite sp = LoadCharacterSprite(spritePath);
        if (sp != null) img.sprite = sp;
        t = 0f;
        while (t < CharRefreshHalfSeconds)   // 半程二：淡回 100%
        {
            t += Time.deltaTime;
            SetCharacterAlpha(img, Mathf.Lerp(CharRefreshDimAlpha, 1f, t / CharRefreshHalfSeconds));
            yield return null;
        }
        SetCharacterAlpha(img, 1f);
        // 不在这里清 charRefreshRoutines[slot]：可能已经有新的过渡接管了，
        // 留着旧句柄最多是一次多余的 StopCoroutine（对已结束的协程无副作用）。
    }
    private static void SetCharacterAlpha(Image img, float alpha)
    {
        Color c = img.color;
        img.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
    }
    public void UpdateTalkLineText(string name, string dialogueContent)
    {
        talkLineGo.SetActive(true);
        // 无论角色是否有立绘（旁白/苏回等无立绘角色不走 ShowCharacter），
        // 显示对话时都要收掉选项框并恢复"继续"按钮，否则选项后接无立绘台词会卡住
        empChoiceUIGo.SetActive(false);
        btnLoadNext.interactable = true;
        //更新对话框文本
        textName.text = name;
        textTalkLine.text = dialogueContent;
        DialogueHistoryPanel.Record(name, dialogueContent);   // 记进"对话回顾"（只记实际播出去的）
    }
    public void SetCharacetrPos(int posID, bool ifRotate = false, int characterID = 0)
    {
        if (posID == 0) return;
        int slot = characterID == 0 ? 0 : 1;
        charPosIds[slot] = posID;      // 记进快照
        charRotates[slot] = ifRotate;
        if (characterID == 0)
        {
            SetPos(posID, imgCharacter, ifRotate);
        }
        else
        {
            SetPos(posID, imgCharacter2, ifRotate);
        }
    }
    public void SetPos(int posID, Image imgTargetCharacter, bool ifRotate = false)
    {
        if (posID == 0) return;
        imgTargetCharacter.transform.localPosition = characterPosTrans[posID - 1].localPosition;
        if (ifRotate)
        {
            imgTargetCharacter.transform.eulerAngles = new Vector3(0, 180, 0);
        }
        else
        {
            imgTargetCharacter.transform.eulerAngles = Vector3.zero;
        }
    }
    public void UpdateEnergyValue(int value = 0)
    {
        //更新体力值
        DoShowOrHideUITeen(true, false, 0.5f, imgEnergy);
        textEnergyValue.text = value.ToString();
        StartCoroutine(DelayHideUI(imgEnergy, 2f));
    }
    public void UpdateFavorabilityValue(int value = 0, string name = null)
    {
        //更新好感度值
        DoShowOrHideUITeen(true, false, 0.5f, imgFavorability);
        textFavorValue.text = name + "\n" + value.ToString();
        StartCoroutine(DelayHideUI(imgFavorability, 2f));
    }
    public void ShowChoiceUI(int choicesNum, string[] choiceContent, UnityEngine.Events.UnityAction<object> unityAction)
    {
        //显示选择UI
        DoShowOrHideUITeen(true, false, 0.5f, imgEnergy);
        btnLoadNext.interactable = false;
        empChoiceUIGo.SetActive(true);
        for (int i = 0; i < choiceUIGos.Length; i++)
        {
            choiceUIGos[i].SetActive(false);
        }
        for (int i = 0; i < choicesNum; i++)
        {
            choiceUIGos[i].SetActive(true);
            textChoiceUIs[i].text = choiceContent[i];
            int src = i;
            choiceUIGos[i].GetComponent<Button>().onClick.RemoveAllListeners();
            choiceUIGos[i].GetComponent<Button>().onClick.AddListener(() => { unityAction(src + 1); });
        }
    }
    public void ShowOrHideMask(bool show)
    {
        DoShowOrHideUITeen(show, true, 2, mask.gameObject);
    }
    public void ShowOrHideCharacter(bool show, int characterID)
    {
        StopCharRefresh(characterID == 0 ? 0 : 1);   // 显隐由 tween 接管，先停掉刷新过渡
        Image characterImage = characterID == 0 ? imgCharacter : imgCharacter2;
        DoShowOrHideUITeen(show, true, 1.5f, characterImage.gameObject);
    }
    /// <summary>
    /// 对一组 GameObject 做淡入淡出。
    /// 透明度通道自动解析：自身 CanvasGroup -> 自身 Graphic(Image/Text等) -> 子物体有 Graphic 时动态补 CanvasGroup。
    /// 淡入开始会保证对象 SetActive(true)；淡出结束会 SetActive(false) 真正关闭对象。
    /// 没有任何可淡化组件的对象退化为直接切换激活状态。
    /// </summary>
    public void DoShowOrHideUITeen(bool show, bool ifLoadNext, float interval, params GameObject[] objs)
    {
        toNextScene = true;
        float percent = show ? 0f : 1f;
        for (int i = 0; i < objs.Length; i++)
        {
            GameObject go = objs[i];
            if (go == null) continue;
            //淡入时先保证对象激活，否则透明度动画不可见
            if (show) go.SetActive(true);

            UIInofo info = new UIInofo { show = show, go = go, ifLoadNext = ifLoadNext, lerpSpeed = interval > 0 ? 1 / interval : 0f, percent = percent };
            //依次寻找可驱动的透明度通道
            info.canvasGroup = go.GetComponent<CanvasGroup>();
            if (info.canvasGroup == null)
            {
                info.graphic = go.GetComponent<Graphic>();
                if (info.graphic == null && go.GetComponentInChildren<Graphic>(true) != null)
                {
                    info.canvasGroup = go.AddComponent<CanvasGroup>();
                }
            }

            if (info.canvasGroup == null && info.graphic == null)
            {
                //对象没有可淡化的渲染组件，无法做透明度动画，直接切换激活状态
                go.SetActive(show);
                FinishTween(info);
                continue;
            }

            //立即套用初始透明度，避免首帧闪一下
            SetAlpha(info, percent);
            imageTweenList.Add(info);
        }
    }
    //保留旧版 Image 重载，兼容已有调用方
    public void DoShowOrHideUITeen(bool show, bool ifLoadNext, float interval, params Image[] images)
    {
        GameObject[] gos = new GameObject[images.Length];
        for (int i = 0; i < images.Length; i++) gos[i] = images[i].gameObject;
        DoShowOrHideUITeen(show, ifLoadNext, interval, gos);
    }
    private void ShowUI(UIInofo info)
    {
        info.percent += info.lerpSpeed * Time.deltaTime;
        SetAlpha(info, info.percent);
        if (info.percent >= 0.95f)
        {
            SetAlpha(info, 1f);
            FinishTween(info);
        }
    }
    private void HideUI(UIInofo info)
    {
        info.percent -= info.lerpSpeed * Time.deltaTime;
        SetAlpha(info, info.percent);
        if (info.percent <= 0.05f)
        {
            SetAlpha(info, 0f);
            //淡出完成后真正关闭对象，避免只透明不消失
            info.go.SetActive(false);
            FinishTween(info);
        }
    }
    private void FinishTween(UIInofo info)
    {
        if (info.ifLoadNext)
        {
            GameManager.Instance.LoadNextScript();
        }
        imageTweenList.Remove(info);
        if (imageTweenList.Count <= 0) toNextScene = false;
    }
    private void SetAlpha(UIInofo info, float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (info.canvasGroup != null)
        {
            info.canvasGroup.alpha = alpha;
            info.canvasGroup.interactable = alpha >= 0.95f;
            info.canvasGroup.blocksRaycasts = alpha >= 0.95f;
        }
        else if (info.graphic != null)
        {
            Color c = info.graphic.color;
            info.graphic.color = new Color(c.r, c.g, c.b, alpha);
        }
    }
    private void Update()
    {
        if (!toNextScene) return;
        //倒序遍历，避免移除元素时索引错位跳过下一个 tween
        for (int i = imageTweenList.Count - 1; i >= 0; i--)
        {
            UIInofo info = imageTweenList[i];
            if (info.show)
            {
                ShowUI(info);
            }
            else
            {
                HideUI(info);
            }
        }
    }
    public void OpenOrCloseBag()
    {
        //打开或关闭背包UI
        openBag = !openBag;
        bagPanelGo.SetActive(openBag);
    }
    IEnumerator DelayHideUI(GameObject obj, float waitSeconds)
    {
        yield return new WaitForSeconds(waitSeconds);
        DoShowOrHideUITeen(false, false, 2, obj);
    }
    public void ShowTipInfo(ItemInfo itemInfo, Vector3 itemPos)
    {
        currentHoverItem = itemInfo;
        tipInfoGo.SetActive(true);
        tipInfoGo.transform.position = itemPos;
        textTipInfo.text = itemInfo.name + "\n" + itemInfo.instruction + "\n数量：" + itemInfo.count;
    }
    public void HideTipInfo()
    {
        currentHoverItem = null;
        tipInfoGo.SetActive(false);
    }
    public void ShowUseBtn(ItemInfo itemInfo, Vector3 itemPos)
    {
        HideTipInfo();                       // 按钮会盖住介绍栏，先收起
        currentUseItem = itemInfo;
        useBtnBlockerGo.SetActive(true);
        // 物品格子是 200x200，按钮高度约 40，向上偏移 ~120 像素到物品上方
        btnUseGo.transform.position = itemPos + new Vector3(0, 130, 0);
    }

    public void HideUseBtn()
    {
        useBtnBlockerGo.SetActive(false);
        currentUseItem = null;
    }
    public void UseCurrentItem()
    {
        if (currentUseItem == null) return;
        ItemInfo item = currentUseItem;
        GameManager.Instance.itemEffectDict[item.id](item.src);  // 执行效果（如回体力）
        item.count--;                                            // 需求4：数量减一
        if (item.count <= 0)
        {
            RemoveItem(item.id);                                 // 归零 → 销毁按钮（沿用现有方法）
        }
        else
        {
            RefreshTipIfShowing(item);
        }
        ShowItemToast(item, "使用了");                           // 需求4：使用提示框
        HideUseBtn();
    }
    public void RefreshTipIfShowing(ItemInfo itemInfo)
    {
        if (currentHoverItem == itemInfo)
        {
            textTipInfo.text = itemInfo.name + "\n" + itemInfo.instruction + "\n数量：" + itemInfo.count;
        }
    }
    public void ShowItemToast(ItemInfo itemInfo, string verb)
    {
        //0. 有图标提示：图标可见
        imgToastIcon.gameObject.SetActive(true);
        //1. 关键清理：取消这个提示框上还在进行的任何淡入/淡出动画，防止两个动画抢alpha打架
        CancelTweenOn(toastGo);
        //2. 取消上一个还没走完的"延迟隐藏"协程，让计时重新开始
        if (toastCoroutine != null) StopCoroutine(toastCoroutine);

        //3. 换图标、换文字
        imgToastIcon.sprite = Resources.Load<Sprite>("Sprites/Items/" + itemInfo.id);
        textToast.text = verb + "「" + itemInfo.name + "」";

        //4. 淡入显示（方法内部会自动 SetActive(true)，不需要手动开）
        DoShowOrHideUITeen(true, false, 0.3f, toastGo);

        //5. 1.5秒后淡出
        toastCoroutine = StartCoroutine(DelayHideToast());
    }

    IEnumerator DelayHideToast()
    {
        yield return new WaitForSeconds(1.5f);
        DoShowOrHideUITeen(false, false, 0.3f, toastGo);
        toastCoroutine = null;
    }
    /// <summary>纯文字提示框（无图标），复用 Toast_Item 的淡入淡出机制</summary>
    public void ShowToastText(string msg)
    {
        CancelTweenOn(toastGo);
        if (toastCoroutine != null) StopCoroutine(toastCoroutine);

        imgToastIcon.gameObject.SetActive(false);   // 纯文字：隐藏图标
        textToast.text = msg;

        DoShowOrHideUITeen(true, false, 0.3f, toastGo);
        toastCoroutine = StartCoroutine(DelayHideToast());
    }
    private void CancelTweenOn(GameObject go)
    {
        //倒序遍历，把目标物体的所有在播动画从列表中移除
        for (int i = imageTweenList.Count - 1; i >= 0; i--)
        {
            if (imageTweenList[i].go == go) imageTweenList.RemoveAt(i);
        }
    }
    /// <summary>存档：导出立绘状态快照（下标 0/1 = characterID 0/1），供读档原样还原</summary>
    public List<CharacterSnapshot> GetCharacterSnapshots()
    {
        List<CharacterSnapshot> list = new List<CharacterSnapshot>();
        for (int i = 0; i < 2; i++)
        {
            Image img = i == 0 ? imgCharacter : imgCharacter2;
            list.Add(new CharacterSnapshot
            {
                shown = img != null && img.gameObject.activeSelf && img.color.a > 0.01f,
                spritePath = charSpritePaths[i],
                posId = charPosIds[i],
                rotate = charRotates[i]
            });
        }
        return list;
    }
    /// <summary>
    /// 读档：按快照还原立绘（显隐/图/位置/翻转），直接给终值、不播动画。
    /// 这样即使读档落在"男主或旁白说话"的句子上（那一句不会调用 ShowCharacter），女主立绘也还在。
    /// </summary>
    public void ApplyCharacterSnapshots(List<CharacterSnapshot> snaps)
    {
        if (snaps == null) return;
        StopCharRefresh(0);   // 读档直接给终值，不要和刷新过渡打架
        StopCharRefresh(1);
        for (int i = 0; i < 2 && i < snaps.Count; i++)
        {
            CharacterSnapshot s = snaps[i];
            if (s == null) continue;
            Image img = i == 0 ? imgCharacter : imgCharacter2;
            if (img == null) continue;
            if (!s.shown || string.IsNullOrEmpty(s.spritePath))
            {
                // 存档时这个槽没显示：还原成透明 + 关闭（与场景初始状态一致）
                Color c0 = img.color;
                img.color = new Color(c0.r, c0.g, c0.b, 0f);
                img.gameObject.SetActive(false);
                continue;
            }
            Sprite sp = LoadCharacterSprite(s.spritePath);
            if (sp == null)
            {
                Debug.LogWarning("读档还原立绘失败，找不到 " + s.spritePath + "（剧情=" + CurrentStory() + "）");
                continue;
            }
            img.sprite = sp;
            Color c = img.color;
            img.color = new Color(c.r, c.g, c.b, 1f);
            img.gameObject.SetActive(true);
            charSpritePaths[i] = s.spritePath;
            charPosIds[i] = s.posId;
            charRotates[i] = s.rotate;
            if (s.posId > 0 && characterPosTrans != null && s.posId - 1 < characterPosTrans.Length
                && characterPosTrans[s.posId - 1] != null)
            {
                img.transform.localPosition = characterPosTrans[s.posId - 1].localPosition;
            }
            img.transform.eulerAngles = s.rotate ? new Vector3(0f, 180f, 0f) : Vector3.zero;
        }
    }
    public void ToggleFunctionBtns()
    {
        //切换功能按钮组(背包等)的整体显示与隐藏,挂在 Btn_Menu 的 onClick 上
        functionBtnsOpen = !functionBtnsOpen;
        for (int i = 0; i < functionBtnGos.Length; i++)
        {
            if (functionBtnGos[i] != null)
            {
                functionBtnGos[i].SetActive(functionBtnsOpen);
            }
        }
        //收起时把背包相关的弹层一起关掉,防止留下看不见的透明拦截层挡住后续点击
        if (!functionBtnsOpen)
        {
            if (openBag)
            {
                OpenOrCloseBag();
            }
            HideUseBtn();
            HideTipInfo();
            if (ArchivePanel.Instance != null)
            {
                ArchivePanel.Instance.Hide();
            }
            if (VolumeSettingsPanel.Instance != null)
            {
                VolumeSettingsPanel.Instance.Hide();
            }
            if (DialogueHistoryPanel.Instance != null)
            {
                DialogueHistoryPanel.Instance.Hide();
            }
        }
    }
}
public class UIInofo
{
    public bool show;
    public GameObject go;
    public CanvasGroup canvasGroup;
    public Graphic graphic;
    public bool ifLoadNext;
    public float percent;
    public float lerpSpeed;
}
public class ItemInfo
{
    public string name;
    public int id;
    public string instruction;
    public object src;
    public int count;   // 当前持有数量
}

/// <summary>
/// 物品总目录（静态，不依赖场景）。UIManager 的模板列表从这里取，
/// 时间地图（无 UIManager 的场景）查物品名/默认背包时也用这里。
/// </summary>
public static class ItemCatalog
{
    private static List<ItemInfo> _items;

    public static List<ItemInfo> Items
    {
        get
        {
            if (_items == null)
            {
                _items = new List<ItemInfo>()
                {
                    new ItemInfo(){name = "老冰棍", id = 1, instruction = "一种传统的冰棍。食用后可以恢复体力", src = 10},
                    new ItemInfo(){name = "小熊", id = 2, instruction = "很可爱的玩偶，可以送给喜欢的人。使之增加好感度", src = 10},
                    new ItemInfo(){name = "奶", id = 3, instruction = "很好喝！欸？保质期是……永久？食用后可以恢复体力", src = 10},
                    new ItemInfo(){name = "旧车票", id = 4, instruction = "预示着新故事的启程，可代价又会是什么？跳跃时间的消耗品", src = 0},
                };
            }
            return _items;
        }
    }

    /// <summary>物品显示名（未知 id 返回"未知物品"）</summary>
    public static string GetName(int id)
    {
        ItemInfo it = Items.Find(t => t.id == id);
        return it != null ? it.name : "未知物品";
    }

    /// <summary>"从新进入"的默认背包是否包含该物品（新档背包为空，所以恒为 false）</summary>
    public static bool HasInDefaultBag(int id)
    {
        return false;   // 新档背包是空的，入场券物品只能从剧情里获得
    }
}