using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// AI 自由对话面板（输入/加载/错误三态）。整个 UI 由代码动态构建，挂到场景 Canvas 下，
/// 无需在场景里配置任何对象，由 AIFreeDialogueFlow 懒创建。
/// 玩法：玩家在输入框写下想对女主说的话 -> 发送 -> 加载等待 -> 成功后把回复拼进剧情播放。
/// 布局约定：所有"区域"必须用 MakeRect 铺满父物体（锚点 0,0~1,1），否则默认 100x100 居中会把子元素挤在一起。
/// </summary>
public class AIDialoguePanel : MonoBehaviour
{
    public static AIDialoguePanel Instance { get; private set; }

    private enum Mode { Input, Loading, Error }

    private GameObject inputArea;
    private GameObject loadingArea;
    private GameObject errorArea;
    private InputField inputField;
    private Text loadingText;
    private Text errorText;

    private Coroutine requestRoutine;      // 请求协程句柄
    private UnityWebRequest currentRequest;// 当前请求（取消时 Abort）
    private string pendingLine;            // 当前请求的台词（失败重试时复用）
    private Coroutine loadingAnim;         // 加载动画协程句柄
    private string heroineName = "白莶";    // 女主名（来自配置，构建 UI 时赋值）

    public static AIDialoguePanel EnsureInstance()
    {
        if (Instance != null) return Instance;
        Canvas canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogError("AIDialoguePanel：场景里找不到 Canvas，无法创建自由对话面板");
            return null;
        }
        GameObject go = new GameObject("AIFreeDialoguePanel");
        go.transform.SetParent(canvas.transform, false);
        return go.AddComponent<AIDialoguePanel>();
    }

    private void Awake()
    {
        Instance = this;
        BuildUI();
        SetMode(Mode.Input);
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============ 对外接口 ============

    /// <summary>进入输入态（选项被点、面板第一次打开时调用）</summary>
    public void ShowInput()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();   // 盖在最上层
        pendingLine = null;
        if (inputField != null) inputField.text = "";
        SetMode(Mode.Input);
        StartCoroutine(FocusInputNextFrame());
    }

    /// <summary>切到加载态（发请求前由 AIFreeDialogueFlow 调用）</summary>
    public void ShowLoading()
    {
        SetMode(Mode.Loading);
    }

    /// <summary>切到加载态并发请求（重试也走这里）</summary>
    public void SendRequest(string line)
    {
        pendingLine = line;
        if (requestRoutine != null) StopCoroutine(requestRoutine);
        requestRoutine = StartCoroutine(RequestCoroutine(line));
    }

    /// <summary>隐藏面板并中止一切进行中的请求</summary>
    public void Hide()
    {
        StopLoadingAnim();
        if (requestRoutine != null) { StopCoroutine(requestRoutine); requestRoutine = null; }
        if (currentRequest != null) { currentRequest.Abort(); currentRequest = null; }
        gameObject.SetActive(false);
    }

    /// <summary>取消按钮：加载中=中止请求，输入/错误态=放弃对话回到选项</summary>
    public void OnCancelClicked()
    {
        AIFreeDialogueFlow.OnDialogueAbandoned();
        Hide();
    }

    // ============ 内部逻辑 ============

    private void SetMode(Mode mode)
    {
        inputArea.SetActive(mode == Mode.Input);
        loadingArea.SetActive(mode == Mode.Loading);
        errorArea.SetActive(mode == Mode.Error);
        if (mode == Mode.Loading) StartLoadingAnim();
        if (mode == Mode.Error) StopLoadingAnim();
    }

    private void ShowError(string msg)
    {
        errorText.text = "连接似乎断了……\n" + msg;
        SetMode(Mode.Error);
    }

    private void OnSendClicked()
    {
        string line = inputField != null ? inputField.text.Trim() : "";
        if (line.Length == 0)
        {
            UIManager.Instance.ShowToastText("先写点什么吧……");
            return;
        }
        AIFreeDialogueFlow.SendPlayerLine(line);
    }

    private void OnRetryClicked()
    {
        if (string.IsNullOrEmpty(pendingLine)) { ShowInput(); return; }
        AIFreeDialogueFlow.SendPlayerLine(pendingLine);
    }

    private IEnumerator FocusInputNextFrame()
    {
        yield return null;
        if (inputField != null) inputField.ActivateInputField();
    }

    private void StartLoadingAnim()
    {
        StopLoadingAnim();
        loadingAnim = StartCoroutine(LoadingAnimation());
    }

    private void StopLoadingAnim()
    {
        if (loadingAnim != null) { StopCoroutine(loadingAnim); loadingAnim = null; }
    }

    private IEnumerator LoadingAnimation()
    {
        int dots = 0;
        while (true)
        {
            loadingText.text = heroineName + "正在思考" + new string('。', dots % 4);
            dots++;
            yield return new WaitForSeconds(0.35f);
        }
    }

    private IEnumerator RequestCoroutine(string line)
    {
        AIFreeDialogueConfig.EnsureLoaded();
        AIConfigData cfg = AIFreeDialogueConfig.Data;
        string apiKey = AIFreeDialogueConfig.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            ShowError("没有配置 AI 密钥，请检查 AIFreeDialogueConfig.json 的 apiKeyObfuscated");
            yield break;
        }

        string[] msgs = AIFreeDialogueFlow.BuildPromptMessages(line);
        string body = "{\"model\":" + JsonEscape(cfg.model)
            + ",\"messages\":[{\"role\":\"system\",\"content\":" + JsonEscape(msgs[0])
            + "},{\"role\":\"user\",\"content\":" + JsonEscape(msgs[1]) + "}]"
            + ",\"temperature\":" + cfg.temperature.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)
            + ",\"max_tokens\":" + cfg.maxTokens
            + (cfg.thinkingDisabled ? ",\"thinking\":{\"type\":\"disabled\"}" : "")   // 关内部思考：延迟从3秒降到1秒
            + (cfg.jsonMode ? ",\"response_format\":{\"type\":\"json_object\"}" : "") // 强制JSON输出，提示词里需出现"JSON"字样
            + ",\"stream\":false}";

        using (UnityWebRequest req = new UnityWebRequest(cfg.endpoint, "POST"))
        {
            currentRequest = req;
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.timeout = Mathf.Max(5, cfg.requestTimeoutSeconds);

            yield return req.SendWebRequest();
            currentRequest = null;

            if (req.result == UnityWebRequest.Result.ProtocolError)
            {
                ShowError("服务端返回错误（HTTP " + req.responseCode + "）：" + req.error);
                yield break;
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                ShowError(DescribeNetworkError(req.error));
                yield break;
            }

            string content = ExtractAssistantContent(req.downloadHandler.text);
            AIFreeDialogueFlow.ParseReply(content, out string replyText, out string replyEmotion, out int favor);
            if (string.IsNullOrEmpty(replyText))
            {
                ShowError("AI 没有返回有效内容，请再试一次");
                yield break;
            }

            requestRoutine = null;
            AIFreeDialogueFlow.CommitDialogue(replyText, replyEmotion, favor);
            Hide();
        }
    }

    private static string DescribeNetworkError(string rawError)
    {
        if (string.IsNullOrEmpty(rawError)) return "未知网络错误";
        if (rawError.Contains("Timeout") || rawError.Contains("timeout")) return "请求超时了，再试一次吧";
        return "网络异常：" + rawError;
    }

    /// <summary>从 DeepSeek 响应里取出 choices[0].message.content</summary>
    private static string ExtractAssistantContent(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            ChatResponse resp = JsonUtility.FromJson<ChatResponse>(raw);
            if (resp != null && resp.choices != null && resp.choices.Count > 0
                && resp.choices[0].message != null)
            {
                return resp.choices[0].message.content;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("解析 AI 响应失败：" + e.Message);
        }
        return null;
    }

    private static string JsonEscape(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }

    // ============ UI 构建（全部代码生成） ============

    private void BuildUI()
    {
        AIFreeDialogueConfig.EnsureLoaded();
        heroineName = AIFreeDialogueConfig.Data.heroineName;
        Font font = Resources.Load<Font>("Fonts/楷体_GB2312");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 根：全屏半透明拦截层（挡住选项/菜单的点击）
        Image blocker = gameObject.AddComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.72f);
        RectTransform rootRT = (RectTransform)transform;
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // 卡片：960x600 居中（字号 40 的主文本需要更大的空间）
        RectTransform card = MakeRect(transform, "Card",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-480, -300), new Vector2(480, 300));
        Image cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = new Color(0.10f, 0.08f, 0.07f, 0.97f);

        Text title = MakeText(card, "Title", "自由对话", 46, new Color(0.92f, 0.86f, 0.78f), TextAnchor.MiddleCenter, font,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-400, -128), new Vector2(400, -58));
        // 三个状态区域共用同一块矩形（标题以下，铺满剩余空间）
        RectTransform area = MakeRect(card, "Areas",
            Vector2.zero, Vector2.one, new Vector2(60, 40), new Vector2(-60, -120));

        BuildInputArea(area, font);
        BuildLoadingArea(area, font);
        BuildErrorArea(area, font);
    }

    private void BuildInputArea(Transform parent, Font font)
    {
        // 区域必须铺满父物体，否则子元素全挤在默认 100x100 的居中方块里
        inputArea = MakeRect(parent, "InputArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
        Text label = MakeText(inputArea.transform, "Label", "你想对" + heroineName + "说点什么？", 40,
            new Color(0.92f, 0.90f, 0.87f), TextAnchor.MiddleLeft, font,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0, -96), new Vector2(640, -30));
        // 输入框：外框（亮色方框提醒可点击）+ 内芯（深色底 + 输入文字 + 占位符）
        RectTransform inputFrame = MakeRect(inputArea.transform, "InputBg",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -192), new Vector2(0, -112));
        Image frameImg = inputFrame.gameObject.AddComponent<Image>();
        frameImg.color = new Color(0.32f, 0.29f, 0.26f, 1f);   // 边框色（比卡片亮，形成方框轮廓）

        RectTransform inputBg = MakeRect(inputFrame, "InputBgInner",
            Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
        Image bgImg = inputBg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0.11f, 0.09f, 0.08f, 1f);      // 内芯深色底
        // 输入框文字
        Text inputText = MakeText(inputBg, "Text", "", 40, new Color(0.95f, 0.93f, 0.90f), TextAnchor.MiddleLeft, font,
            Vector2.zero, Vector2.one, new Vector2(18, 6), new Vector2(-18, -6));

        inputText.supportRichText = false;

        // 占位提示
        Text placeholder = MakeText(inputBg, "Placeholder", "写下一句话，" + heroineName + "会认真回应你……", 36,
            new Color(0.52f, 0.49f, 0.46f), TextAnchor.MiddleLeft, font,
            Vector2.zero, Vector2.one, new Vector2(18, 6), new Vector2(-18, -6));

        inputField = inputBg.gameObject.AddComponent<InputField>();
        inputField.textComponent = inputText;
        inputField.placeholder = placeholder;
        inputField.lineType = InputField.LineType.SingleLine;
        inputField.characterLimit = 60;
        inputField.onEndEdit.AddListener((val) =>
        {
            // Enter 提交；失焦/取消不触发（onEndEdit 两种情况下都会回调）
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnSendClicked();
        });

        Text hint = MakeText(inputArea.transform, "Hint",
            "回车或点击按钮发送 · 最多 60 字 · 消耗 " + AIFreeDialogueConfig.Data.energyCost + " 体力",
            24, new Color(0.65f, 0.62f, 0.58f), TextAnchor.MiddleLeft, font,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0, -244), new Vector2(760, -212));

        RectTransform btnRow = MakeRect(inputArea.transform, "BtnRow",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260, 70), new Vector2(260, 150));
        MakeButton(btnRow, "Btn_Send", "说出这句话", 34, new Color(0.55f, 0.20f, 0.16f), Color.white, font,
            new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0, 0), new Vector2(-10, 0), OnSendClicked);
        MakeButton(btnRow, "Btn_Cancel", "先不说了", 34, new Color(0.30f, 0.28f, 0.26f), new Color(0.90f, 0.88f, 0.85f), font,
            new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(10, 0), new Vector2(0, 0), OnCancelClicked);
    }

    private void BuildLoadingArea(Transform parent, Font font)
    {
        loadingArea = MakeRect(parent, "LoadingArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;

        loadingText = MakeText(loadingArea.transform, "LoadingText", heroineName + "正在思考", 40,
            new Color(0.92f, 0.90f, 0.87f), TextAnchor.MiddleCenter, font,
            new Vector2(0f, 0.68f), new Vector2(1f, 0.68f), new Vector2(0, -55), new Vector2(0, 55));

        Text sub = MakeText(loadingArea.transform, "SubText", "她需要一点时间来组织语言……", 26,
            new Color(0.65f, 0.62f, 0.58f), TextAnchor.MiddleCenter, font,
            new Vector2(0f, 0.46f), new Vector2(1f, 0.46f), new Vector2(0, -45), new Vector2(0, 5));

        RectTransform btnRow = MakeRect(loadingArea.transform, "BtnRow",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-140, 55), new Vector2(140, 135));
        MakeButton(btnRow, "Btn_Cancel", "取消", 34, new Color(0.30f, 0.28f, 0.26f), new Color(0.90f, 0.88f, 0.85f), font,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, OnCancelClicked);
    }

    private void BuildErrorArea(Transform parent, Font font)
    {
        errorArea = MakeRect(parent, "ErrorArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;

        errorText = MakeText(errorArea.transform, "ErrorText", "连接似乎断了……", 32,
            new Color(0.95f, 0.60f, 0.55f), TextAnchor.MiddleCenter, font,
            new Vector2(0f, 0.55f), new Vector2(1f, 0.55f), new Vector2(0, -70), new Vector2(0, 70));

        RectTransform btnRow = MakeRect(errorArea.transform, "BtnRow",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260, 70), new Vector2(260, 150));
        MakeButton(btnRow, "Btn_Retry", "再试一次", 34, new Color(0.55f, 0.20f, 0.16f), Color.white, font,
            new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0, 0), new Vector2(-10, 0), OnRetryClicked);
        MakeButton(btnRow, "Btn_Cancel", "算了", 34, new Color(0.30f, 0.28f, 0.26f), new Color(0.90f, 0.88f, 0.85f), font,
            new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(10, 0), new Vector2(0, 0), OnCancelClicked);
    }

    private RectTransform MakeRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    private Text MakeText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment, Font font,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform rt = MakeRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
        Text text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;   // 纯文字不参与点击，避免挡住按钮/输入框
        return text;
    }

    private void MakeButton(Transform parent, string name, string label, int fontSize, Color bgColor, Color textColor, Font font,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, UnityAction onClick)
    {
        RectTransform rt = MakeRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = bgColor;
        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);

        Text text = MakeText(rt, "Text", label, fontSize, textColor, TextAnchor.MiddleCenter, font,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        text.raycastTarget = false;   // 文字不挡按钮点击
    }
}

/// <summary>DeepSeek 响应解析用（JsonUtility 只认字段名，其余字段自动忽略）</summary>
[Serializable]
public class ChatResponse
{
    public List<ChatChoice> choices;
}

[Serializable]
public class ChatChoice
{
    public ChatMessage message;
}

[Serializable]
public class ChatMessage
{
    public string content;
    public string reasoning_content;
}
