using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// AI 自由对话流程控制器（静态类，不依赖场景挂载）。
/// 流程：AI 选项被点 -> 弹输入面板 -> 玩家说话 -> 调 AI -> 把"玩家台词+女主回复"
/// 临时插入剧情列表播放 -> 播完自动回收 -> 剧情回到主线。
/// 插入是临时的：只在内存里，不写剧本文件；剧情指针离开临时段后立即移除，
/// 所以存读档/跳转查找都按原始索引走，不受影响。
/// </summary>
public static class AIFreeDialogueFlow
{
    public static bool IsInAISegment { get; private set; }   // 正在播放自由对话临时段（期间禁止存档）
    public static string PendingPlayerLine { get; private set; }

    private static int landingIndex = -1;              // 落地句（女主回复插槽）在剧本列表里的下标
    private static int segmentInsertStart = -1;         // 临时条目插入位置
    private static int segmentInsertLen = 0;            // 临时条目数量
    private static readonly List<string> conversationHistory = new List<string>();   // 本会话自由对话记录（仅内存）

    /// <summary>
    /// 清空自由对话的全部会话状态。在剧情/主菜单/时间地图场景进入时调用，
    /// 防止上一个剧情里中断的自由对话（如播放中途按 Home 离开）把 IsInAISegment 留在 true，
    /// 导致存档面板的 AI 拦截分支误触发（主菜单没有 UIManager 还会 NRE）。
    /// </summary>
    public static void ResetSession()
    {
        IsInAISegment = false;
        PendingPlayerLine = null;
        landingIndex = -1;
        segmentInsertStart = -1;
        segmentInsertLen = 0;
        conversationHistory.Clear();
    }
    /// <summary>选项被点击时调用：判定为自由对话入口则弹出输入面板并吃掉这次点击</summary>
    public static bool TryStartFromOption(int optionIndex)
    {
        AIFreeDialogueConfig.EnsureLoaded();
        if (!AIFreeDialogueConfig.Data.enabled) return false;

        GameManager gm = GameManager.Instance;
        if (gm == null || optionIndex <= 0) return false;
        int entryIndex = gm.scriptIndex + optionIndex;
        if (entryIndex < 0 || entryIndex >= gm.scriptDatas.Count) return false;

        // 落地句：与普通选项跳转规则一致（选项的 eventData -> 剧本里的 scriptID）
        // 注意 scriptID 必须 >0：JsonUtility 解析时"没有 scriptID 字段"的条目默认是 0，
        // 若 eventData=0 不排除会误命中索引 0（剧情开头），导致误判成 AI 入口、插入后从头播放。
        landingIndex = gm.scriptDatas.FindIndex(sd => sd.scriptID > 0 && sd.scriptID == gm.scriptDatas[entryIndex].eventData);
        if (landingIndex < 0) return false;

        // 识别为自由对话入口的两种方式（满足其一）：
        //  1) 选项文本以 aiOptionPrefix 开头（如 "@AI……"）；
        //  2) 落地句文本留空 —— 落地句是"AI 回复插槽"的特征。
        //     这样选项上写"请输入你想说的话"之类的默认文字也不会影响识别。
        string optionText = gm.scriptDatas[entryIndex].dialogueContent;
        string prefix = AIFreeDialogueConfig.Data.aiOptionPrefix;
        bool prefixed = !string.IsNullOrEmpty(prefix)
            && optionText != null && optionText.TrimStart().StartsWith(prefix);
        bool landingEmpty = string.IsNullOrEmpty(gm.scriptDatas[landingIndex].dialogueContent);
        if (!prefixed && !landingEmpty) return false;

        PendingPlayerLine = null;
        AIDialoguePanel.EnsureInstance()?.ShowInput();
        return true;
    }

    /// <summary>玩家点"说出这句话"：体力足够则记录台词、切加载态并发请求（重试也走这里）</summary>
    public static void SendPlayerLine(string line)
    {
        GameManager gm = GameManager.Instance;
        if (gm.eneryValue <= 0)
        {
            UIManager.Instance.ShowToastText("体力不足，先休息一下吧");
            return;
        }
        PendingPlayerLine = line;
        AIDialoguePanel.Instance.ShowLoading();
        AIDialoguePanel.Instance.SendRequest(line);
    }

    /// <summary>取消/放弃本次对话（清掉待发台词，回到选项）</summary>
    public static void OnDialogueAbandoned()
    {
        PendingPlayerLine = null;
    }

    /// <summary>AI 回复就绪：把回复填进落地句、玩家台词插到它前面，扣体力，然后开始播放临时段</summary>
    public static void CommitDialogue(string replyText, string replyEmotion, int favorDelta)
    {
        GameManager gm = GameManager.Instance;
        AIConfigData cfg = AIFreeDialogueConfig.Data;

        conversationHistory.Add(cfg.playerName + "：" + PendingPlayerLine);
        conversationHistory.Add(cfg.heroineName + "：" + replyText);

        // 1) 女主回复填进落地句：保留 scriptID/立绘位置等设定，改写文字、好感度；无语音设计，清掉语音
        ScriptData slot = gm.scriptDatas[landingIndex];
        slot.dialogueContent = replyText;
        // 表情：AI 给了就用 AI 的（已过白名单校验）；没给就保留剧本里的默认表情（如 wearsad）当兜底
        if (!string.IsNullOrEmpty(replyEmotion)) slot.emotion = replyEmotion;
        // 好感度增量可正可负（负 = 这次回应让她失望），幅度上限 favorabilityMaxDelta
        int maxDelta = Mathf.Max(1, cfg.favorabilityMaxDelta);
        slot.favorability = Mathf.Clamp(favorDelta, -maxDelta, maxDelta);
        slot.soundType = 0;
        slot.soundPath = "";

        // 2) 玩家台词作为男主的一条台词，插在落地句前面（无预录语音，静音播放）
        ScriptData playerLine = new ScriptData
        {
            loadType = 2,
            name = cfg.playerName,
            dialogueContent = PendingPlayerLine,
            characterID = 0,
            characterPos = 0,
            ifRotate = false,
            soundType = 0,
            soundPath = "",
            energyValue = 0,
            favorability = 0,
            eventID = 0,
            eventData = 0,
            scriptID = -1,     // -1：不参与任何跳转查找
            emotion = ""
        };
        gm.scriptDatas.Insert(landingIndex, playerLine);
        segmentInsertStart = landingIndex;
        segmentInsertLen = 1;
        IsInAISegment = true;

        // 3) 自由对话代价：扣体力（成功回复才算数）
        gm.ChangeEnergyValue(-cfg.energyCost);

        gm.scriptIndex = landingIndex;
        gm.HandleData();
    }

    /// <summary>
    /// 剧情指针越过临时段末尾时回收插入的条目，返回移除数量（跳转路径用它校正目标下标）。
    /// 由 GameManager.LoadNextScript / SetScriptIndex 在推进剧情前调用，本故事里触发点是
    /// 落地句播完、进入下一条（跳回主线）的瞬间。
    /// </summary>
    public static int EndSegmentIfPassed(int currentIndex)
    {
        if (!IsInAISegment) return 0;
        int segEnd = segmentInsertStart + segmentInsertLen;   // 最后一条临时条目的下标
        if (currentIndex <= segEnd) return 0;
        GameManager.Instance.scriptDatas.RemoveRange(segmentInsertStart, segmentInsertLen);
        IsInAISegment = false;
        return segmentInsertLen;
    }

    /// <summary>构建 DeepSeek 的 system/user 两条消息（模板 + 注入器组装）</summary>
    public static string[] BuildPromptMessages(string playerLine)
    {
        AIPromptTemplateData tpl = AIFreeDialogueConfig.Template;
        AIConfigData cfg = AIFreeDialogueConfig.Data;
        GameManager gm = GameManager.Instance;

        // 注册全部动态占位符（新增动态段 = 这里加一行 + 模板里加一个 {{xxx}}）
        AIPromptInjector.Register("heroineName", () => cfg.heroineName);
        AIPromptInjector.Register("playerName", () => cfg.playerName);
        AIPromptInjector.Register("background", () => tpl.backgroundSetting);
        AIPromptInjector.Register("heroineSetting", () => tpl.heroineSetting);
        AIPromptInjector.Register("outputRules", () => tpl.outputRules);
        AIPromptInjector.Register("favorabilityMax", () => cfg.favorabilityMaxDelta.ToString());
        AIPromptInjector.Register("emotionList", () => string.Join("/", cfg.emotionList));
        AIPromptInjector.Register("progress", () => "剧本索引 " + gm.scriptIndex + "（共 " + gm.scriptDatas.Count + " 条）");
        AIPromptInjector.Register("story", () => BuildStoryText(gm.scriptDatas, cfg.maxStoryChars));
        AIPromptInjector.Register("favorability",
            () => cfg.heroineName + "：" + (gm.favorabilityDict.TryGetValue(cfg.heroineName, out int v) ? v : cfg.favorabilityInitial));
        AIPromptInjector.Register("history", () => string.Join("\n", conversationHistory));
        AIPromptInjector.Register("playerLine", () => playerLine);
        // 历史段：没有对话记录时整段不出现
        AIPromptInjector.Register("historySection",
            () => conversationHistory.Count > 0 ? AIPromptInjector.Inject(tpl.historySectionTemplate) : "");

        string system = AIPromptInjector.Inject(tpl.systemTemplate);
        string user = AIPromptInjector.Inject(tpl.userTemplate);
        return new string[] { system, user };
    }

    /// <summary>把剧本里所有"对话与旁白"条目整理成带序号的文本（只含对话和旁白，超出上限保留最新段）</summary>
    private static string BuildStoryText(List<ScriptData> list, int maxChars)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            ScriptData sd = list[i];
            if (sd.loadType != 2 || string.IsNullOrEmpty(sd.dialogueContent)) continue;
            sb.Append('[').Append(i).Append("] ");
            sb.Append(string.IsNullOrEmpty(sd.name) ? "？" : sd.name).Append('：');
            sb.Append(sd.dialogueContent).Append('\n');
        }
        string text = sb.ToString();
        if (text.Length <= maxChars) return text;
        text = text.Substring(text.Length - maxChars);   // 保留最新段（离当前剧情更近）
        int cut = text.IndexOf('\n');
        return cut >= 0 ? text.Substring(cut + 1) : text;
    }

    /// <summary>解析 AI 输出：提取 text / emotion / favorability。解析失败时 text 为空</summary>
    public static void ParseReply(string raw, out string text, out string emotion, out int favor)
    {
        text = "";
        emotion = "";
        favor = 0;
        if (string.IsNullOrEmpty(raw)) return;

        // 容错提取：优先取 [ ... ]（数组），否则取 { ... }（单个对象）
        string json = raw;
        int s = raw.IndexOf('[');
        int e = raw.LastIndexOf(']');
        if (s < 0 || e <= s)
        {
            s = raw.IndexOf('{');
            e = raw.LastIndexOf('}');
        }
        if (s >= 0 && e > s) json = raw.Substring(s, e - s + 1);

        List<string> texts = new List<string>();
        int maxDelta = Mathf.Max(1, AIFreeDialogueConfig.Data.favorabilityMaxDelta);
        foreach (Match m in Regex.Matches(json, @"\{[^{}]*\}"))
        {
            string obj = m.Value;
            Match tm = Regex.Match(obj, "\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!tm.Success) continue;
            string t = Unescape(tm.Groups[1].Value).Trim();
            if (t.Length == 0) continue;
            texts.Add(t.Length > 200 ? t.Substring(0, 200) : t);
            // 好感度：容忍小数与带引号（模型偶尔输出 0.5 / "3"，旧正则会把 0.5 截成 0 → 看起来"偏小"）
            Match fm = Regex.Match(obj, "\"favorability\"\\s*:\\s*\"?(-?\\d+(?:\\.\\d+)?)\"?");
            if (fm.Success && float.TryParse(fm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fRaw))
            {
                favor += Mathf.Clamp(Mathf.RoundToInt(fRaw), -maxDelta, maxDelta);
            }
            if (string.IsNullOrEmpty(emotion))
            {
                Match em = Regex.Match(obj, "\"emotion\"\\s*:\\s*\"?([A-Za-z]*)\"?");
                if (em.Success) emotion = em.Groups[1].Value.Trim().ToLowerInvariant();   // 大小写/引号容错
            }
            if (texts.Count >= 2) break;
        }

        if (texts.Count == 0)
        {
            // 没解析出有效 text：输出本身是 JSON 形状（比如 text 为空）视为无效，
            // 否则退化成"整段原文就是台词"（去掉可能的代码围栏）
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return;
            text = trimmed.Trim('`', ' ', '\n');
            if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase)) text = text.Substring(4).Trim();
            return;
        }
        text = string.Join("\n", texts);
        favor = Mathf.Clamp(favor, -maxDelta, maxDelta);
        // 表情白名单校验：AI 给的表情不在女主表情库里则退回配置的 fallbackEmotion
        // （默认 wearnormal —— 本段她穿的是 wear 那套，退回 normal 会换错装扮）
        if (!string.IsNullOrEmpty(emotion)
            && AIFreeDialogueConfig.Data.emotionList != null
            && !AIFreeDialogueConfig.Data.emotionList.Contains(emotion))
        {
            string fb = FallbackEmotion();
            Debug.LogWarning("AI 输出了表情库外表情: " + emotion + "，已退回 " + fb);
            emotion = fb;
        }
    }
    /// <summary>表情白名单校验失败时的回退表情（读配置 fallbackEmotion，缺省 wearnormal）</summary>
    private static string FallbackEmotion()
    {
        string fb = AIFreeDialogueConfig.Data.fallbackEmotion;
        return string.IsNullOrEmpty(fb) ? "wearnormal" : fb;
    }
    private static string Unescape(string s)
    {
        return s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
