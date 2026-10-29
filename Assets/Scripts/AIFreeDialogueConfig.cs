using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 自由对话的配置入口。配置文件统一放在 Assets/Resources/AIFreeDialogue/ 下：
///  - AIFreeDialogueConfig.json：服务参数（模型/密钥/温度/好感度/体力/表情库等）
///  - PromptTemplate.json：提示词模板与文案（可注入占位符见 AIPromptInjector）
/// 改完保存即可，无需重编译。API 密钥以"XOR+Base64"混淆形式存放，源码不出现任何凭据字面量。
/// 注意：客户端游戏里密钥只能做到"不被人一眼抄走"，无法真正保密（反编译可还原）。
/// </summary>
public static class AIFreeDialogueConfig
{
    private const byte XOR_MASK = 0x5A;   // 与混淆时用的掩码一致
    private const string CONFIG_PATH = "AIFreeDialogue/AIFreeDialogueConfig";
    private const string TEMPLATE_PATH = "AIFreeDialogue/PromptTemplate";

    private static AIConfigData _data;
    private static AIPromptTemplateData _template;

    public static AIConfigData Data
    {
        get
        {
            EnsureLoaded();
            return _data;
        }
    }

    public static AIPromptTemplateData Template
    {
        get
        {
            EnsureLoaded();
            return _template;
        }
    }

    /// <summary>从 Resources 加载配置文件；文件缺失或损坏时退回代码内置默认值</summary>
    public static void EnsureLoaded()
    {
        if (_data != null) return;
        _data = new AIConfigData();
        _template = new AIPromptTemplateData();
        TextAsset ta = Resources.Load<TextAsset>(CONFIG_PATH);
        if (ta != null)
        {
            try
            {
                _data = JsonUtility.FromJson<AIConfigData>(ta.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("AI 自由对话配置文件解析失败，使用内置默认配置：" + e.Message);
            }
        }
        else
        {
            Debug.LogWarning("找不到 Resources/" + CONFIG_PATH + ".json，AI 自由对话使用代码内置默认配置");
        }
        TextAsset tt = Resources.Load<TextAsset>(TEMPLATE_PATH);
        if (tt != null)
        {
            try
            {
                _template = JsonUtility.FromJson<AIPromptTemplateData>(tt.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("提示词模板解析失败，使用内置默认模板：" + e.Message);
            }
        }
        // 表情库兜底：配置里没填时用内置清单（对应 Sprites/Characters/<女主名>/ 下的表情图）
        if (_data.emotionList == null || _data.emotionList.Count == 0)
        {
            _data.emotionList = new List<string> { "normal", "angry", "cold", "nosee", "smell", "wearnormal", "wearcold", "wearsad", "wearsmell" };
        }
    }

    /// <summary>还原 API 密钥（仅在运行时内存中使用，不落盘）。配置里没填则返回空串</summary>
    public static string GetApiKey()
    {
        string obfuscated = Data.apiKeyObfuscated;
        if (string.IsNullOrEmpty(obfuscated)) return "";
        try
        {
            byte[] bytes = Convert.FromBase64String(obfuscated);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= XOR_MASK;
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e)
        {
            Debug.LogWarning("API 密钥解码失败（检查 apiKeyObfuscated 是否被改坏）：" + e.Message);
            return "";
        }
    }

    /// <summary>生成混淆串的工具方法（编辑器里临时调用一次即可）</summary>
    public static string Obfuscate(string plainKey)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(plainKey);
        for (int i = 0; i < bytes.Length; i++) bytes[i] ^= XOR_MASK;
        return Convert.ToBase64String(bytes);
    }
}

/// <summary>服务配置字段（对应 AIFreeDialogueConfig.json）</summary>
[Serializable]
public class AIConfigData
{
    public bool enabled = true;                       // 总开关，false 时空选项按普通选项处理
    public string endpoint = "https://api.deepseek.com/chat/completions";
    public string model = "deepseek-v4-flash";        // 官方模型名：deepseek-v4-pro / deepseek-v4-flash
    public bool thinkingDisabled = true;              // 关闭模型内部思考（实测 reasoning_tokens 归零、延迟从3秒降到1秒）
    public bool jsonMode = true;                      // 强制 JSON 输出（response_format json_object，要求提示词里出现"JSON"字样）
    public float temperature = 0.9f;                  // 越高回复越放飞
    public int maxTokens = 400;                       // 回复 token 上限
    public int requestTimeoutSeconds = 60;            // 请求超时（秒）
    public int maxStoryChars = 20000;                 // 发给 AI 的剧本全文上限，超出时保留最新段
    public string playerName = "苏回";                // 男主名，玩家台词会以他的身份播一条剧情
    public string heroineName = "白莶";               // AI 扮演的女主名（剧本里的落地句必须是这个名字）
    public string aiOptionPrefix = "@AI";             // 选项文本以该前缀开头也算自由对话入口（落地句文本留空同样算）
    public int favorabilityInitial = 50;              // 女主初始好感度（存档里没有该角色时用这个值兜底）
    public int favorabilityMaxDelta = 10;             // AI 单次回复的好感度变化幅度上限（钳制 -该值~+该值，负数=好感度下降）
    public int energyCost = 10;                       // 每次成功自由对话消耗的体力
    public List<string> emotionList;                  // 女主表情库白名单（对应 Sprites/Characters/<女主名>/ 下的表情图）
    public string fallbackEmotion = "wearnormal";     // 表情白名单校验失败时的回退表情（必须在 emotionList 里）
    public string apiKeyObfuscated = "";              // 密钥混淆串（明文 XOR 0x5A 后 Base64），见 Obfuscate()
}

/// <summary>提示词模板字段（对应 PromptTemplate.json），{{xxx}} 占位符由 AIPromptInjector 替换</summary>
[Serializable]
public class AIPromptTemplateData
{
    public string systemTemplate =
        "你是剧情游戏《簌洄之莶》的女主「{{heroineName}}」。请完全代入她，用她的口吻、性格和语气说话。\n" +
        "\n【背景设定】\n{{background}}\n" +
        "\n【女主设定】\n{{heroineSetting}}\n" +
        "\n【当前剧情进度】\n{{progress}}\n" +
        "\n【剧本全文（对话与旁白）】\n{{story}}\n" +
        "\n【当前好感度】\n{{favorability}}{{historySection}}\n" +
        "\n【输出规则】\n{{outputRules}}";
    public string historySectionTemplate = "\n\n【此前自由对话记录】\n{{history}}";
    public string userTemplate = "【{{playerName}}（玩家）刚才对你说】\n{{playerLine}}";
    public string backgroundSetting = "";
    public string heroineSetting = "";
    public string outputRules = "";
}
