using Google.Protobuf.WellKnownTypes;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public List<ScriptData> scriptDatas;
    public int scriptIndex = 0;
    public ScriptData data;
    public int eneryValue;//体力值
    public Dictionary<string, int> favorabilityDict;//好感度字典
    public Dictionary<int, Action<object>> eventDict;
    public GameObject hitPointGo;
    public Dictionary<int, Action<object>> itemEffectDict;
    public string currentStoryName;//当前剧情的文件名(不含扩展名),存档时记录
    public string currentBGName;//当前背景图名(Sprites/BG/ 下),存档时记录
    public string currentMusicName;//当前BGM名(AudioClips/Music/ 下),存档时记录
    public string currentSurroundingName;//当前环境音名(AudioClips/Surrounding/ 下),存档时记录
    private SaveInfo loadSave;//读档数据,Awake取走后保留给Start做环境/背包/数值恢复
    public bool storyEnded;//剧情已到结局（结局事件触发后置位，自动播放/存档等据此收手）

    private void Awake()
    {
        Instance = this;
        ArchivePanel.ResetCrossSceneState();   // 剧情场景入口：清掉上一个场景遗留的强制存档/继承目标
        DialogueHistoryPanel.ClearLog();       // 对话回顾只记当前这一场：新开局/读档都从零开始记
        // 读档：用存档里的剧情文件名与索引；新游戏：默认剧情从第 0 句开始
        // pendingLoad 先存到 loadSave，Start 里的环境/背包/数值恢复还要用，用完即清
        loadSave = SaveSystem.pendingLoad;
        SaveSystem.ClearPendingLoad();
        string storyName = loadSave != null ? loadSave.storyName : SaveSystem.DEFAULT_STORY_NAME;
        int startIndex = loadSave != null ? loadSave.scriptIndex : 0;
        // 存档里文件名可能是空的（历史坏档 / 写入方漏填）：回退默认剧情，绝不让整场景白屏
        if (string.IsNullOrEmpty(storyName))
        {
            Debug.LogWarning("存档里没有剧情文件名(storyName 为空)，已回退到默认剧情 " + SaveSystem.DEFAULT_STORY_NAME);
            storyName = SaveSystem.DEFAULT_STORY_NAME;
        }
        UnityEngine.TextAsset jsonFile = Resources.Load<UnityEngine.TextAsset>("StoryData/" + storyName);
        if (jsonFile == null)
        {
            Debug.LogError("找不到剧情数据文件,请确认 Assets/Resources/StoryData/" + storyName + ".json 文件存在");
            // 再兜一层：指定剧情不存在时退到默认剧情，避免"白屏 + 整个场景不工作"
            if (storyName != SaveSystem.DEFAULT_STORY_NAME)
            {
                storyName = SaveSystem.DEFAULT_STORY_NAME;
                jsonFile = Resources.Load<UnityEngine.TextAsset>("StoryData/" + storyName);
            }
        }
        if (jsonFile == null) return;
        ScriptDataList dataList = JsonUtility.FromJson<ScriptDataList>(jsonFile.text);
        scriptDatas = dataList.scriptDatas;
        currentStoryName = storyName;
        // 存档索引越界保护（存档文件被改坏时兜底从第 0 句开始）
        scriptIndex = (startIndex >= 0 && startIndex < scriptDatas.Count) ? startIndex : 0;
        eneryValue = 50;
        favorabilityDict = new Dictionary<string, int>()
        {
            { "Player",0 },
            { "白莶", 50 }
        };
        eventDict = new Dictionary<int, Action<object>>()
        {
            { 1, StartHitPointEvent }
            //{ 2, StartFreeActivitiesEvent }
        };
        itemEffectDict = new Dictionary<int, Action<object>>()
        {
            {1, UseRecoverEnergyItem},
            {3, UseRecoverEnergyItem}
        };
    }
    private void Start()
    {
        if (loadSave != null)
        {
            // 读档：先恢复"画面+声音"环境（背景/BGM/环境音/遮罩淡出），再恢复背包与数值
            RestoreLoadedEnvironment(loadSave);
            // 立绘快照：存档时连"当前显示的立绘（图/位置/翻转/显隐）"一起存了，这里原样还原。
            // 不还原的话，读档落在"男主或旁白说话"的句子上就不会调用 ShowCharacter → 女主立绘消失。
            if (loadSave.characters != null)
            {
                UIManager.Instance.ApplyCharacterSnapshots(loadSave.characters);
            }
            if (loadSave.items != null)
            {
                // 新格式存档：按快照精确恢复背包、体力、好感度
                UIManager.Instance.RestoreBag(loadSave.items);
                eneryValue = loadSave.eneryValue;
                UIManager.Instance.UpdateEnergyValue(eneryValue);
                favorabilityDict = new Dictionary<string, int>();
                if (loadSave.favorability != null)
                {
                    for (int i = 0; i < loadSave.favorability.Count; i++)
                    {
                        favorabilityDict[loadSave.favorability[i].name] = loadSave.favorability[i].value;
                    }
                }
            }
            else
            {
                // 旧格式存档（没有背包快照）：背包从空开始
                UIManager.Instance.ClearBag();
            }
        }
        else
        {
            // 新游戏：背包从空开始（设计定稿：新档不送物品）
            UIManager.Instance.ClearBag();
        }
        WireSceneButtons();
        Canvas sceneCanvas = FindObjectOfType<Canvas>(true);
        if (sceneCanvas != null)
        {
            // 首次操作提示延后到进入场景 13 秒（原 3 秒 + 10 秒）：先让玩家看一眼开场再弹
            HintPanel.ShowIfFirstTime(sceneCanvas.transform, "Img_Tip", "HintShown_PlotPlayer",
                "按下右上角按钮，以显示或隐藏其余左侧副按钮。\n左侧副按钮从上到下依次是背包、存档、声音、主菜单、自动播放和对话回顾。", 13f);
        }
        HandleData();
    }
    /// <summary>
    /// 场景里新增的通用按钮自动接线（不需要在 Inspector 配引用）：
    /// Btn_Home=返回主菜单（时间地图/剧情播放器都有），Btn_Auto=自动播放开关。
    /// </summary>
    private void WireSceneButtons()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b.name == "Btn_Home")
            {
                b.onClick.AddListener(GoMainMenu);
            }
            else if (b.name == "Btn_Auto")
            {
                if (b.GetComponent<AutoPlayController>() == null)
                {
                    b.gameObject.AddComponent<AutoPlayController>();
                }
            }
        }
    }

    private void GoMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
    /// <summary>
    /// 读档后恢复"画面+声音"环境：背景图、环境音、BGM，最后淡出遮罩。
    /// 注意：遮罩淡出必须直调 DoShowOrHideUITeen 且 ifLoadNext=false，
    /// 不能走 ShowOrHideMask（它会在淡出结束时自动 LoadNextScript，把读档后第一句跳掉）。
    /// </summary>
    private void RestoreLoadedEnvironment(SaveInfo save)
    {
        if (save == null) return;
        if (!string.IsNullOrEmpty(save.bgSpriteName))
        {
            currentBGName = save.bgSpriteName;
            UIManager.Instance.SetBGImageSprite(save.bgSpriteName);
        }
        if (!string.IsNullOrEmpty(save.surroundingName))
        {
            currentSurroundingName = save.surroundingName;
            AudioSourceManager.Instance.PlaySurrounding(save.surroundingName);
        }
        if (!string.IsNullOrEmpty(save.musicName))
        {
            currentMusicName = save.musicName;
            AudioSourceManager.Instance.PlayMusic(save.musicName);
        }
        UIManager.Instance.DoShowOrHideUITeen(false, false, 1.5f, UIManager.Instance.mask.gameObject);
    }
    /// <summary>收集当前全部可存档状态（剧情进度+环境+背包+数值），供存档面板写入</summary>
    public SaveInfo BuildSaveInfo()
    {
        SaveInfo info = new SaveInfo
        {
            storyName = currentStoryName,
            scriptIndex = scriptIndex,
            bgSpriteName = currentBGName,
            musicName = currentMusicName,
            surroundingName = currentSurroundingName,
            eneryValue = eneryValue,
            favorability = new List<FavorSaveData>(),
            items = UIManager.Instance.GetBagSnapshot(),
            characters = UIManager.Instance.GetCharacterSnapshots()
        };
        foreach (KeyValuePair<string, int> kv in favorabilityDict)
        {
            info.favorability.Add(new FavorSaveData { name = kv.Key, value = kv.Value });
        }
        return info;
    }
    /// <summary>从当前 scriptIndex 播放一条剧情（自由对话流程也需要从外部触发，故为 public）</summary>
    public void HandleData()
    {
        if (scriptIndex >= scriptDatas.Count)
        {
            Debug.Log("游戏结束");
            return;
        }
        data = scriptDatas[scriptIndex];
        AutoPlayController.MarkLineShown();   // 自动播放的计时基准：每处理一条剧情都刷新
        PlaySound(data.soundType);
        if (data.loadType == 1)
        {
            //加载背景
            currentBGName = data.spriteName;
            UIManager.Instance.SetBGImageSprite(data.spriteName);
            LoadNextScript();
        }
        else if (data.loadType == 2)
        {
            HandleCharacter();
        }
        else if (data.loadType == 3)
        {
            //加载事件
            switch (data.eventID)
            {
                case 1:
                    //显示选择UI
                    UIManager.Instance.ShowChoiceUI(data.eventData, GetChoiceContent(data.eventData), SetScriptIndex);
                    break;
                case 2:
                    //跳转剧本
                    SetScriptIndex(0);
                    break;
                case 3:
                    //显示隐藏遮罩
                    UIManager.Instance.ShowOrHideMask(System.Convert.ToBoolean(data.eventData));
                    break;
                case 4:
                    eventDict[data.eventData](null);
                    break;
                case 5:
                    HandleCharacter(true);
                    UIManager.Instance.ShowOrHideCharacter(System.Convert.ToBoolean(data.eventData), data.characterID);
                    break;
                case 6:
                    //获得物品（eventData = 物品id），数量固定加 1
                    UIManager.Instance.AddItemByID(data.eventData, 1);
                    LoadNextScript();
                    break;
                case 7:
                    //结局事件：剧情到此为止，强制弹存档面板，存完自动进时间地图
                    storyEnded = true;
                    UIManager.Instance.btnLoadNext.interactable = false;
                    UIManager.Instance.talkLineGo.SetActive(false);
                    if (ArchivePanel.Instance != null)
                    {
                        ArchivePanel.Instance.ForceOpenForEnding();
                    }
                    else
                    {
                        Debug.LogError("结局事件：场景里没有 ArchivePanel，无法弹出强制存档面板");
                    }
                    break;
                default: break;
            }
        }
        else if (data.loadType == 4)
        {
            // 视频开场：播完自动进下一条剧情
            PreloadResources();
            VideoManager.Instance.PlayVideo(data.spriteName, () => LoadNextScript());
        }
    }
    public void LoadNextScript(int addNum = 1)
    {
        //Debug.Log("加载下一条剧情数据");
        scriptIndex += addNum;
        // 自由对话临时段播完后回收插入的条目（EndSegmentIfPassed 返回移除数量，回退指针）
        scriptIndex -= AIFreeDialogueFlow.EndSegmentIfPassed(scriptIndex);
        HandleData();
    }
    public void PlaySound(int soundType)
    {
        switch (soundType)
        {
            case 1:
                // 语音也按剧情分目录：AudioClips/Dialogue/<角色>/<剧情>/<序号>
                AudioSourceManager.Instance.PlayDialogue(
                    ResourcePaths.DialogueAudioPath(currentStoryName, data.name, data.soundPath)); break;
            case 2:
                AudioSourceManager.Instance.PlaySound(data.soundPath);
                break;
            case 3:
                currentMusicName = data.soundPath;
                AudioSourceManager.Instance.PlayMusic(data.soundPath);
                break;
            case 4:
                currentSurroundingName = data.soundPath;
                AudioSourceManager.Instance.PlaySurrounding(data.soundPath);
                break;
            default:
                break;
        }
    }
    public void ChangeEnergyValue(int value = 0)
    {
        //播放音效并改变体力值
        if (value != 0)
        {
            AudioSourceManager.Instance.PlaySound("Energy");
        }
        eneryValue += value;
        if (eneryValue > 100)
        {
            eneryValue = 100;
        }
        else if (eneryValue < 0)
        {
            eneryValue = 0;
        }
        UIManager.Instance.UpdateEnergyValue(eneryValue);
    }

    public void ChangeFavorabilityValue(int value = 0, string name = null)
    {
        //播放音效并改变好感度值
        if (value != 0)
        {
            AudioSourceManager.Instance.PlaySound("Favor");
        }
        favorabilityDict[name] += value;
        if (favorabilityDict[name] > 100)
        {
            favorabilityDict[name] = 100;
        }
        else if (favorabilityDict[name] < 0)
        {
            favorabilityDict[name] = 0;
        }
        UIManager.Instance.UpdateFavorabilityValue(favorabilityDict[name], name);
    }
    public void HandleCharacter(bool showCharacterOnly = false)
    {
        if (data.name != null && data.name != "旁白" && data.name != "苏回")
        {
            //加载人物
            UIManager.Instance.ShowCharacter(data.name, data.characterID, data.emotion);
            //设置人物位置
            UIManager.Instance.SetCharacetrPos(data.characterPos, data.ifRotate, data.characterID);
        }
        //显示对话框
        if (!showCharacterOnly)
        {
            UIManager.Instance.UpdateTalkLineText(data.name, data.dialogueContent);
            if (data.energyValue != 0)
            {
                ChangeEnergyValue(data.energyValue);
            }
            if (data.favorability != 0)
            {
                ChangeFavorabilityValue(data.favorability, data.name);
            }
        }
    }
    /// <summary>延迟多少秒后执行LoadNextScript</summary>
    //IEnumerator DelayNextScript(float waitSeconds)
    //{
    //    yield return new WaitForSeconds(waitSeconds);
    //    LoadNextScript();
    //}
    public string[] GetChoiceContent(int num)
    {
        string[] choiceContent = new string[num];
        for (int i = 1; i <= num; i++)
        {
            choiceContent[i - 1] = scriptDatas[scriptIndex + i].dialogueContent;
        }
        return choiceContent;
    }
    public void SetScriptIndex(object index)
    {
        int target = (int)index;
        // 空白选项（自由对话入口）交给 AI 流程处理，这里不跳转
        if (target > 0 && AIFreeDialogueFlow.TryStartFromOption(target)) return;
        //越界保护:scriptIndex + target 必须落在剧本列表范围内,否则直接忽略
        if (scriptIndex + target < 0 || scriptIndex + target >= scriptDatas.Count)
        {
            Debug.LogWarning("SetScriptIndex 索引越界, scriptIndex=" + scriptIndex + ", index=" + target);
            return;
        }
        if (target == scriptDatas[scriptIndex].eventData)
        {
            for (int i = 0; i < target - 1; i++)
            {
                Button btn = UIManager.Instance.choiceUIGos[i].GetComponent<Button>();
                btn.interactable = true;
            }
        }
        else if (target != 0)
        {
            Button btn = UIManager.Instance.choiceUIGos[target - 1].GetComponent<Button>();
            btn.interactable = false;
        }
        for (int i = 0; i < scriptDatas.Count; i++)
        {
            // scriptID > 0 才参与跳转：没写 scriptID 的条目解析出来是 0，
            // eventData=0 时若不排除会误跳到索引 0（剧情从头开始）
            if (scriptDatas[i].scriptID > 0
                && scriptDatas[scriptIndex + target].eventData == scriptDatas[i].scriptID)
            {
                // 若这次跳转发生在自由对话临时段之后，先回收插入的条目再校正目标下标
                int removed = AIFreeDialogueFlow.EndSegmentIfPassed(i);
                i -= removed;
                scriptIndex = i;
                HandleData();
                break;
            }
        }
    }
    public void StartHitPointEvent(object src)
    {
        UIManager.Instance.imgCharacter.gameObject.SetActive(false);
        UIManager.Instance.talkLineGo.SetActive(false);
        hitPointGo.SetActive(true);
    }
    public void UseRecoverEnergyItem(object src)
    {
        //改变体力的物品
        ChangeEnergyValue((int)src);
    }
    //public void StartFreeActivitiesEvent(object src)
    //{
    //    UIManager.Instance.talkLineGo.SetActive(false);
    //    freeActivitiesGo.SetActive(true);
    //}
    /// <summary>
    /// 视频播放期间后台预加载开场资源，避免视频播完瞬间同步加载大文件卡顿。
    /// 只处理大资源：背景图 + 音乐/环境音/音效；对话语音是小文件，播放时才加载也不卡。
    /// </summary>
    private void PreloadResources()
    {
        // 只预加载视频后面 10 条剧情（覆盖整个雨夜开场），不把整本剧本资源都装进内存
        int maxCount = Mathf.Min(scriptIndex + 1 + 10, scriptDatas.Count);
        for (int i = scriptIndex + 1; i < maxCount; i++)
        {
            ScriptData sd = scriptDatas[i];
            // 背景与音频必须**各自独立**判断：早期写成 if / else if，
            // 于是"同一条既换背景又放音乐"时只预加载了背景，音乐被跳过——
            // 开场的雨雨雨.mp3(7.4MB)、雨声.mp3(11.6MB) 从来没被预加载过，
            // 视频一结束就在主线程同步加载，于是接正片时卡零点几秒。
            if (sd.loadType == 1 && !string.IsNullOrEmpty(sd.spriteName))
            {
                // 新结构在剧情子目录，旧结构在根目录：两条都发一次异步请求，
                // 不存在的那条不会真的加载（成本为零），保证哪种结构都能预热到。
                Resources.LoadAsync<Sprite>(ResourcePaths.BGSpritePath(currentStoryName, sd.spriteName));
                Resources.LoadAsync<Sprite>("Sprites/BG/" + sd.spriteName);
            }
            if (sd.soundType == 2 && !string.IsNullOrEmpty(sd.soundPath))
            {
                Resources.LoadAsync<AudioClip>("AudioClips/Sound/" + sd.soundPath);
            }
            else if (sd.soundType == 3 && !string.IsNullOrEmpty(sd.soundPath))
            {
                Resources.LoadAsync<AudioClip>("AudioClips/Music/" + sd.soundPath);
            }
            else if (sd.soundType == 4 && !string.IsNullOrEmpty(sd.soundPath))
            {
                Resources.LoadAsync<AudioClip>("AudioClips/Surrounding/" + sd.soundPath);
            }
        }
    }
}
