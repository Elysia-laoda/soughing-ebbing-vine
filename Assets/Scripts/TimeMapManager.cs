using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 时间跳跃地图（中枢选关场景）管理器。挂到 TimeMap 场景任意物体上即可，
/// 其余全部自动：按名字找场景对象、代码生成同心圆/节点/连线、缩放平移交互、
/// 详情框/弹窗/继承存档面板。
///
/// 布局模型：从上方俯视一棵倒圆锥树。同心圆 = 同一外部时间点的切面，
/// 世界线 = 固定角度（辐条），节点 = (worldlineId, timePoint) 极坐标，
/// 同一世界线相邻时间点自动连线；数据里的 links 画额外的关联线。
///
/// 交互：滚轮以原点为中心缩放（原点不漂移），拖拽平移 = 移动原点的屏幕位置；
/// 点击节点 → 锁定视角放大到节点 + 详情框；点空白处/关闭按钮解锁。
/// 完全缩小时中心显示教程剧情（孤立），完全放大时中心显示原点剧情。
/// </summary>
public class TimeMapManager : MonoBehaviour
{
    public static TimeMapManager Instance { get; private set; }
    /// <summary>背景图 Resources 路径（不含扩展名），运行时加载挂到最底层，避免场景精灵引用在构建中失效</summary>
    public string backgroundSpritePath = "Sprites/BG/Universal/MapBG";   // 2026-10-18：与剧情无关的散图归到 Sprites/BG/Universal/

    // ============ 数据 ============
    private TimeMapData data;
    private readonly List<NodeView> nodeViews = new List<NodeView>();
    private readonly List<LineView> lineViews = new List<LineView>();
    private readonly List<RingView> ringViews = new List<RingView>();
    private Dictionary<string, NodeView> nodeById;

    // ============ 场景对象（按名字找） ============
    private Canvas canvas;
    private RectTransform mapRoot;
    private GameObject detailPanel;
    private Text detailTitle;
    private Text detailSynopsis;
    private Button btnStartNew;
    private Button btnFromSave;
    private Button btnDetailClose;
    private GameObject messagePanel;
    private Text messageText;
    private ArchivePanel archivePanel;

    // ============ 视图状态 ============
    private float zoom = 1f;
    private Vector2 origin = Vector2.zero;   // 原点在 Canvas 上的位置（屏幕中心=0,0）
    private float diskAngle;                 // 圆盘整体自转角（度，负=顺时针）
    private bool locked;                     // 已锁定视角（详情框打开中）
    private float lockZoom;
    private Vector2 lockOrigin;
    private NodeView selected;
    private bool dragging;
    private bool messageOpen;
    private Coroutine tweenRoutine;

    // ============ 资源 ============
    private Sprite spriteCircle;
    private Sprite spriteWhite;
    private Material ringMatTemplate;   // UI/Ring shader 的模板材质
    private Font font;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // 时间地图场景入口先复位跨场景静态量：面板本体是运行时挂在未激活的 Panel_Archive 上，
        // 它的 Awake 要等面板第一次打开才跑，不能用来复位（否则会清掉刚写入的继承目标）
        ArchivePanel.ResetCrossSceneState();
        font = Resources.Load<Font>("Fonts/楷体_GB2312");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        spriteCircle = Sprite.Create(MakeCircleTexture(128), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100f);
        spriteWhite = Sprite.Create(MakeWhiteTexture(), new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);

        canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogError("TimeMapManager：场景里没有 Canvas");
            return;
        }

        SetupBackground();
        SetupMapRoot();
        SetupDragCatcher();
        SetupDetailPanel();
        SetupMessagePanel();
        SetupArchivePanel();
        BindHomeButton();

        if (!LoadMapData())
        {
            ShowMessage("找不到时间地图数据（Resources/StoryData/time_map.json）");
            return;
        }
        BuildMap();
        // 初始视角 0.1：教程居中，屏幕上呈现中段十几圈，内紧外松的喇叭轮廓
        zoom = Mathf.Clamp(data.layout.zoomMin * 40f, data.layout.zoomMin, data.layout.zoomMax);
        origin = Vector2.zero;
        ApplyView();

        // 时间地图专属 BGM（没挂/没配音源时静默跳过）
        AudioSourceManager asm = AudioSourceManager.Instance;
        if (asm != null && asm.musicAudio != null)
        {
            asm.PlayMusic("Secret");
        }
        // 首次进入提示（看过后不再弹，PlayerPrefs 记忆）
        HintPanel.ShowIfFirstTime(canvas.transform, "Img_Tip", "HintShown_TimeMap",
            "缩小为最小地图，选择中间的教程节点。\n点击节点，即可查看详情。\n无存档的推荐从新进入。");
    }

    // ============ 场景对象装配 ============
    /// <summary>递归查找 UI 对象（画布直挂的 UI 可能被信箱容器收纳，顶层 Find 会找不到）</summary>
    private static Transform FindUI(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform t = FindUI(root.GetChild(i), name);
            if (t != null) return t;
        }
        return null;
    }
    private void SetupBackground()
    {
        // 背景纯装饰：整个子树关掉射线检测，让点击穿透到拖拽层
        Transform bg = FindUI(canvas.transform, "Img_BaseWhite");
        if (bg != null) DisableRaycastRecursive(bg);
        EnsureRuntimeBackground();
    }
    /// <summary>
    /// 背景兜底：场景里背景精灵的引用在工程迁移（团结引擎 meta 用 base64 guid、场景用十六进制
    /// guid）后可能在构建里失效——编辑器里看正常、打包后不显示。这里直接从 Resources 加载一张
    /// 全屏背景挂到 UI 最底层，保证构建里背景一定存在。
    /// 想换背景：改 backgroundSpritePath（Resources 路径，不含扩展名），或直接换这张图。
    /// </summary>
    private void EnsureRuntimeBackground()
    {
        if (string.IsNullOrEmpty(backgroundSpritePath)) return;
        Sprite sp = Resources.Load<Sprite>(backgroundSpritePath);
        if (sp == null)
        {
            // 兜底：图片可能被导入成普通纹理（textureType 非 Sprite），转 Texture2D 手动建 Sprite
            Texture2D tex = Resources.Load<Texture2D>(backgroundSpritePath);
            if (tex != null)
            {
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                Debug.Log("TimeMapManager：背景图按纹理兜底加载成功 " + backgroundSpritePath);
            }
        }
        if (sp == null)
        {
            Debug.LogError("TimeMapManager：背景图加载失败 Resources/" + backgroundSpritePath
                + "（文件不存在，或没在 Resources 目录，或没导入成精灵）");
            return;
        }
        Debug.Log("TimeMapManager：背景图加载成功 " + backgroundSpritePath + " -> " + sp.name);
        Transform container = FindUI(canvas.transform, "__UIContainer");
        Transform parent = container != null ? container : canvas.transform;

        GameObject go = new GameObject("RuntimeBackground");
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(0);   // 最底层，盖在所有 UI 后面
        Image img = go.AddComponent<Image>();
        img.sprite = sp;
        img.color = Color.white;
        img.raycastTarget = false;   // 关键：全屏背景不拦点击，否则拖拽/点节点全被它吃掉
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Debug.Log("TimeMapManager：RuntimeBackground 已创建，父级=" + parent.name);
    }
    private static void DisableRaycastRecursive(Transform t)
    {
        Image img = t.GetComponent<Image>();
        if (img != null) img.raycastTarget = false;
        for (int i = 0; i < t.childCount; i++)
        {
            DisableRaycastRecursive(t.GetChild(i));
        }
    }

    private void SetupMapRoot()
    {
        Transform root = FindUI(canvas.transform, "MapRoot");
        if (root == null)
        {
            Debug.LogError("TimeMapManager：场景里没有 MapRoot");
            return;
        }
        mapRoot = root.GetComponent<RectTransform>();
        mapRoot.anchorMin = new Vector2(0.5f, 0.5f);
        mapRoot.anchorMax = new Vector2(0.5f, 0.5f);
        mapRoot.pivot = new Vector2(0.5f, 0.5f);
        mapRoot.anchoredPosition = Vector2.zero;
        mapRoot.localScale = Vector3.one;
    }

    /// <summary>全屏点击层：空白处拖拽平移、锁定态点空白解锁。放最底层，所有节点/面板盖在它上面</summary>
    private void SetupDragCatcher()
    {
        GameObject go = new GameObject("MapDragCatcher");
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetSiblingIndex(0);   // 最底层（背景已关射线检测，点击能穿透到它）
        Image img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.001f);   // 几乎全透明但可接收点击
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        MapDragCatcher catcher = go.AddComponent<MapDragCatcher>();
        catcher.owner = this;
    }

    private void SetupDetailPanel()
    {
        Transform panel = FindUI(canvas.transform, "Panel_Detail");
        if (panel == null)
        {
            Debug.LogError("TimeMapManager：场景里没有 Panel_Detail");
            return;
        }
        detailPanel = panel.gameObject;
        detailTitle = panel.Find("Text_Title")?.GetComponent<Text>();
        detailSynopsis = panel.Find("Text_Synopsis")?.GetComponent<Text>();
        btnStartNew = panel.Find("Btn_StartNew")?.GetComponent<Button>();
        btnFromSave = panel.Find("Btn_FromSave")?.GetComponent<Button>();
        btnDetailClose = panel.Find("Btn_Close")?.GetComponent<Button>();
        if (btnStartNew != null) btnStartNew.onClick.AddListener(OnStartNewClicked);
        if (btnFromSave != null) btnFromSave.onClick.AddListener(OnFromSaveClicked);
        if (btnDetailClose != null) btnDetailClose.onClick.AddListener(UnlockView);
        detailPanel.SetActive(false);
    }

    private void SetupMessagePanel()
    {
        Transform panel = FindUI(canvas.transform, "Panel_Message");
        if (panel == null)
        {
            Debug.LogError("TimeMapManager：场景里没有 Panel_Message");
            return;
        }
        messagePanel = panel.gameObject;
        // 消息文本 = Panel_Message 直属的 Text（Btn_OK 下面的不算）
        Text[] texts = panel.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i].transform.parent == panel)
            {
                messageText = texts[i];
                break;
            }
        }
        Button ok = panel.Find("Btn_OK")?.GetComponent<Button>();
        if (ok != null) ok.onClick.AddListener(HideMessage);
        messagePanel.SetActive(false);
    }

    /// <summary>存档面板：运行时创建 ArchivePanel 组件（Inherit 模式），槽位/拦截层/按钮全部代码生成</summary>
    private void SetupArchivePanel()
    {
        Transform panel = FindUI(canvas.transform, "Panel_Archive");
        if (panel == null)
        {
            Debug.LogError("TimeMapManager：场景里没有 Panel_Archive");
            return;
        }

        // 全屏拦截层（放在面板正下方；创建后先隐藏，只有点槽位弹按钮时才出现）
        GameObject blocker = new GameObject("Blocker_Inherit");
        blocker.transform.SetParent(canvas.transform, false);
        Image bimg = blocker.AddComponent<Image>();
        bimg.color = new Color(0f, 0f, 0f, 0.001f);
        blocker.AddComponent<Button>();
        RectTransform brt = blocker.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;
        blocker.SetActive(false);
        // 弹出小按钮（继承进入；创建后先隐藏，点槽位时才显示在槽位上方）
        GameObject actBtn = new GameObject("Btn_InheritAction");
        actBtn.transform.SetParent(canvas.transform, false);
        Image actImg = actBtn.AddComponent<Image>();
        actImg.sprite = Resources.Load<Sprite>("Sprites/UI/Grid");
        actImg.color = new Color(0.45f, 0.22f, 0.16f, 1f);
        Button actButton = actBtn.AddComponent<Button>();
        actButton.targetGraphic = actImg;
        RectTransform art = actBtn.GetComponent<RectTransform>();
        art.sizeDelta = new Vector2(160, 56);
        Text actText = MakeText(actBtn.transform, "Text", "继承进入", 26, Color.white, TextAnchor.MiddleCenter);
        actText.raycastTarget = false;
        actBtn.SetActive(false);

        archivePanel = panel.gameObject.AddComponent<ArchivePanel>();
        archivePanel.mode = ArchiveMode.Inherit;
        archivePanel.panelGo = panel.gameObject;
        archivePanel.slotContainer = panel.Find("Img_GridBG");
        archivePanel.blockerGo = blocker;
        archivePanel.actionBtnGo = actBtn;
        archivePanel.actionBtnText = actText;
        archivePanel.runtimeManagedButtons = true;   // 按钮是拦截层的兄弟节点，显隐由面板自己管理
        archivePanel.Init();
        // 面板底图必须排在槽位容器前面，否则会盖住槽位挡住点击（复制场景时顺序可能不对）
        Transform bgImg = panel.Find("Img_BG");
        if (bgImg != null) bgImg.SetAsFirstSibling();

        // 槽位容器缺 GridLayoutGroup 时补一个（防止槽位叠在一起点不中）
        if (archivePanel.slotContainer != null
            && archivePanel.slotContainer.GetComponent<GridLayoutGroup>() == null)
        {
            GridLayoutGroup glg = archivePanel.slotContainer.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(200, 200);
            glg.spacing = new Vector2(24, 24);
            glg.childAlignment = TextAnchor.MiddleCenter;
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
        }
        // 面板自带的关闭按钮（Panel_Archive/Btn_Close）
        Button close = panel.Find("Btn_Close")?.GetComponent<Button>();
        if (close != null) close.onClick.AddListener(archivePanel.OpenOrCloseArchive);
    }

    private void BindHomeButton()
    {
        Transform home = FindUI(canvas.transform, "Btn_Home");
        if (home == null)
        {
            Debug.LogWarning("TimeMapManager：场景里没有 Btn_Home");
            return;
        }
        Button btn = home.GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(GoMainMenu);
    }

    private void GoMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    // ============ 数据加载与地图构建 ============

    private bool LoadMapData()
    {
        TextAsset ta = Resources.Load<TextAsset>("StoryData/time_map");
        if (ta == null) return false;
        try
        {
            data = JsonUtility.FromJson<TimeMapData>(ta.text);
            if (data == null || data.nodes == null) return false;
            if (data.layout == null) data.layout = new TimeMapLayout();
            // links 是 [[id1,id2],...] 的数组嵌套，JsonUtility 不支持，这里手动解析
            data.links = ParseLinks(ta.text);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("时间地图数据解析失败：" + e.Message);
            return false;
        }
    }

    private List<string[]> ParseLinks(string json)
    {
        List<string[]> list = new List<string[]>();
        MatchCollection ms = Regex.Matches(json, @"\[\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\]");
        foreach (Match m in ms)
        {
            list.Add(new string[] { m.Groups[1].Value, m.Groups[2].Value });
        }
        return list;
    }
    /// <summary>
    /// 第 t 层（时间点）的屏幕半径。
    /// "horn"（喇叭形，默认）= S·(e^{λt}−1)：半径按等比增长，
    /// 屏幕上可见的圈数只由"半径比 16 倍"决定，与缩放无关——无论放大缩小看到的圈数几乎恒定；
    /// 圈距按指数加速增大（越往外差距越大），像隧道/喇叭。
    /// "perspective"（锥形）= S·t/(pole−t)：越往外间距越大，但可见圈数随缩放剧烈变化。
    /// "linear" = 均匀圈距（旧版观感）。
    /// </summary>
    private float RadiusFor(int t)
    {
        if (t <= 0) return 0f;
        TimeMapLayout lay = data.layout;
        if (lay.radiusMode == "horn")
        {
            return lay.hornScale * (Mathf.Exp(lay.hornLambda * t) - 1f);
        }
        if (lay.radiusMode == "perspective")
        {
            return lay.radiusScale * t / (lay.perspectivePole - t);
        }
        return lay.circleSpacing * t;
    }
    private void BuildMap()
    {
        int wlCount = data.worldlines != null ? Mathf.Max(1, data.worldlines.Count) : 1;

        // 0) 计算每个节点的参数：半径（喇叭形）+ 世界线扇区内的随机散布角 + 随机角速度
        nodeById = new Dictionary<string, NodeView>();
        List<NodeView> all = new List<NodeView>();
        int maxTimePoint = 0;
        float sectorHalf = 180f / Mathf.Max(1, wlCount);   // 每条世界线占 360/wlCount 度，节点在扇区半宽内随机
        for (int i = 0; i < data.nodes.Count; i++)
        {
            TimeMapNode nd = data.nodes[i];
            NodeView view = new NodeView { data = nd, isCenter = nd.isCenter };
            if (nd.isCenter)
            {
                view.radius = 0f;
                view.baseAngle = 0f;
            }
            else
            {
                float worldlineAngle = nd.worldlineId * 360f / wlCount;
                view.radius = RadiusFor(nd.timePoint);
                // 基准角 = 世界线角度 + 数据里的角度微调 + 扇区内随机散布
                view.baseAngle = (worldlineAngle + nd.angleOffset + UnityEngine.Random.Range(-sectorHalf, sectorHalf)) * Mathf.Deg2Rad;
                // 扇区内的初始摆动位置与随机角速度（速度取反边界 = ±扇区半宽）
                view.angleOffset = UnityEngine.Random.Range(-sectorHalf, sectorHalf);
                view.velocity = UnityEngine.Random.Range(-data.layout.nodeSpeedMax, data.layout.nodeSpeedMax);
                maxTimePoint = Mathf.Max(maxTimePoint, nd.timePoint);
            }
            all.Add(view);
            nodeById[nd.id] = view;
        }

        // 1) 同心圆装饰（每层时间点一个圆环，UI/Ring shader 画固定像素宽的线）
        //    这里必须真能拿到 shader：只用 Shader.Find 引用的 shader 会被打包剔除，
        //    拿到 null 时 Image 退回默认 UI 材质 —— 一圈一个大方块，34 圈叠起来就是把
        //    整屏洗成"淡黄色"(0.75,0.70,0.62)，背景全被盖住（构建版实测就是这个现象）。
        //    所以：优先从 Resources 加载；拿不到就整体跳过圆环装饰，绝不留下色块。
        Color ringColor = new Color(0.75f, 0.70f, 0.62f, 0.75f);
        float canvasScale = canvas != null ? canvas.scaleFactor : 1f;
        Shader ringShader = ResolveRingShader();
        if (ringShader == null)
        {
            Debug.LogError("UI/Ring shader 不可用：已跳过全部同心圆装饰（否则会变成整屏色块盖住背景）。"
                + "修法：把 UI_Ring.shader 放进 Assets/Resources/Shaders/，或在 Project Settings → Graphics → Always Included Shaders 里加入 UI/Ring");
        }
        for (int t = 1; ringShader != null && t <= maxTimePoint; t++)
        {
            float radius = RadiusFor(t);
            GameObject go = new GameObject("Circle_" + t);
            go.transform.SetParent(mapRoot, false);
            Image img = go.AddComponent<Image>();
            img.sprite = spriteWhite;
            img.raycastTarget = false;   // 纯装饰：不挡点击
            img.material = CreateRingMaterial(ringShader, radius, canvasScale, 3f);
            img.color = ringColor;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            float d = radius * 2f;
            rt.sizeDelta = new Vector2(d, d);
            ringViews.Add(new RingView { image = img, radius = radius, material = img.material });
        }

        // 2) 自动连线：同一世界线相邻时间点（扇区内摆动，连线随之实时弯折）
        for (int w = 0; w < wlCount; w++)
        {
            List<NodeView> lineNodes = all.FindAll(v => !v.isCenter && v.data.worldlineId == w);
            lineNodes.Sort((a, b) => a.data.timePoint.CompareTo(b.data.timePoint));
            Color lineColor = GetWorldlineColor(w, new Color(0.8f, 0.78f, 0.75f));
            for (int i = 0; i < lineNodes.Count - 1; i++)
            {
                if (lineNodes[i + 1].data.timePoint - lineNodes[i].data.timePoint == 1)
                {
                    CreateLine(lineNodes[i], lineNodes[i + 1], lineColor, data.layout.lineAlpha, 3f);
                }
            }
        }

        // 3) 数据里的显式关联线（跨世界线/中心节点连线），跳过两个中心节点（距离为0）
        Color linkColor = new Color(0.75f, 0.78f, 0.82f);
        for (int i = 0; i < data.links.Count; i++)
        {
            NodeView a = GetNodeOrNull(data.links[i][0]);
            NodeView b = GetNodeOrNull(data.links[i][1]);
            if (a == null || b == null) continue;
            if (a.isCenter && b.isCenter) continue;
            CreateLine(a, b, linkColor, data.layout.lineAlpha * 0.8f, 3f);
        }

        // 4) 节点按钮
        for (int i = 0; i < all.Count; i++)
        {
            CreateNode(all[i]);
        }
        nodeViews.AddRange(all);
    }
    /// <summary>
    /// 取 UI/Ring shader。优先 Resources 加载——Resources 里的资产必定进构建；
    /// 只靠 Shader.Find 引用的 shader 会被打包剔除，构建里必然返回 null（这就是时间地图
    /// 背景在打包版被整屏色块盖住的根因）。再退回 Shader.Find，兼容编辑器与
    /// 已把该 shader 加进 Always Included Shaders 的工程。
    /// </summary>
    private static Shader ResolveRingShader()
    {
        Shader sh = Resources.Load<Shader>("Shaders/UI_Ring");
        if (sh == null) sh = Shader.Find("UI/Ring");
        return sh;
    }
    /// <summary>创建一条圆环用的材质：线宽为固定像素（随 Image 拉伸不改变粗细）</summary>
    private Material CreateRingMaterial(Shader sh, float radius, float canvasScale, float thicknessPx)
    {
        if (ringMatTemplate == null)
        {
            if (sh == null) return null;   // 调用方已确保非 null；这里只做兜底
            ringMatTemplate = new Material(sh);
        }
        Material m = new Material(ringMatTemplate);
        float imagePx = Mathf.Max(1f, radius * 2f * canvasScale);
        m.SetFloat("_RadiusUV", 0.5f);
        m.SetFloat("_ThicknessUV", Mathf.Clamp(thicknessPx / imagePx, 0.0001f, 0.49f));
        return m;
    }
    private Color GetWorldlineColor(int wlId, Color fallback)
    {
        if (data.worldlines != null)
        {
            for (int i = 0; i < data.worldlines.Count; i++)
            {
                if (data.worldlines[i].id == wlId && !string.IsNullOrEmpty(data.worldlines[i].color))
                {
                    if (ColorUtility.TryParseHtmlString(data.worldlines[i].color, out Color c)) return c;
                }
            }
        }
        return fallback;
    }

    private NodeView GetNodeOrNull(string id)
    {
        return nodeById != null && nodeById.TryGetValue(id, out NodeView v) ? v : null;
    }
    /// <summary>节点当前在圆盘上的位置（mapRoot 本地坐标，未缩放）：扇区摆动 + 圆盘自转</summary>
    private Vector2 NodePos(NodeView v)
    {
        if (v.isCenter || v.radius <= 0f) return Vector2.zero;
        float angle = v.baseAngle + (v.angleOffset + diskAngle) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * v.radius;
    }
    private void CreateLine(NodeView a, NodeView b, Color color, float alpha, float width)
    {
        CreateLine(a, b, color, alpha, width, "Line_" + a.data.id + "_" + b.data.id);
    }

    private void CreateLine(NodeView a, NodeView b, Color color, float alpha, float width, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(mapRoot, false);
        Image img = go.AddComponent<Image>();
        img.sprite = spriteWhite;
        img.raycastTarget = false;   // 纯装饰：不挡点击
        img.color = new Color(color.r, color.g, color.b, alpha);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        LineView lv = new LineView { a = a, b = b, baseWidth = width, image = img };
        lineViews.Add(lv);
        UpdateLineGeometry(lv);   // 按当前节点位置摆好初始几何
    }

    /// <summary>按两个节点的实时位置更新连线几何（位置/长度/角度/线宽反补）</summary>
    private void UpdateLineGeometry(LineView lv)
    {
        Vector2 pa = NodePos(lv.a);
        Vector2 pb = NodePos(lv.b);
        Vector2 diff = pb - pa;
        RectTransform rt = lv.image.rectTransform;
        rt.anchoredPosition = pa;
        rt.sizeDelta = new Vector2(Mathf.Max(0.1f, diff.magnitude), Mathf.Max(0.5f, lv.baseWidth / zoom));
        rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);
    }

    private void CreateNode(NodeView view)
    {
        TimeMapNode nd = view.data;
        Color baseColor = nd.isCenter
            ? new Color(0.92f, 0.80f, 0.58f)
            : GetWorldlineColor(nd.worldlineId, new Color(0.8f, 0.78f, 0.75f));

        GameObject go = new GameObject("Node_" + nd.id);
        go.transform.SetParent(mapRoot, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = NodePos(view);
        rt.sizeDelta = new Vector2(150, 140);

        GameObject circleGo = new GameObject("Circle");
        circleGo.transform.SetParent(rt, false);
        Image circleImg = circleGo.AddComponent<Image>();
        circleImg.sprite = spriteCircle;
        circleImg.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.95f);
        RectTransform crt = circleGo.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = new Vector2(0, 0);
        crt.sizeDelta = new Vector2(96, 96);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = circleImg;
        NodeView captured = view;
        btn.onClick.AddListener(() => OnNodeClicked(captured));

        // 节点名：字号比原来大一档 + 深色描边，自转/缩小时压在圆环与连线上也看得清
        Text label = MakeText(rt, "Label", nd.title, 22, new Color(0.96f, 0.94f, 0.90f), TextAnchor.UpperCenter);
        Outline labelOutline = label.gameObject.AddComponent<Outline>();
        labelOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        labelOutline.effectDistance = new Vector2(1.5f, -1.5f);
        labelOutline.useGraphicAlpha = true;
        RectTransform lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0, 4);
        lrt.sizeDelta = new Vector2(180, 52);
        label.raycastTarget = false;

        view.rt = rt;
        view.image = circleImg;
    }

    // ============ 交互 ============

    private void Update()
    {
        if (mapRoot == null) return;
        if (!locked)
        {
            if (!messageOpen)
            {
                // 缩放：安卓触屏=双指捏合，桌面=滚轮，同一套对数缩放（以原点为中心）
                if (Input.touchCount == 2)
                {
                    Touch t0 = Input.GetTouch(0);
                    Touch t1 = Input.GetTouch(1);
                    Vector2 p0 = t0.position - t0.deltaPosition;
                    Vector2 p1 = t1.position - t1.deltaPosition;
                    float prev = (p0 - p1).magnitude;
                    float cur = (t0.position - t1.position).magnitude;
                    float delta = cur - prev;
                    if (Mathf.Abs(delta) > 0.5f)
                    {
                        zoom = Mathf.Clamp(zoom * Mathf.Exp(delta * 0.008f), data.layout.zoomMin, data.layout.zoomMax);
                        ApplyView();
                    }
                }
                else
                {
                    float scroll = Input.GetAxis("Mouse ScrollWheel");
                    if (Mathf.Abs(scroll) > 0.0001f)
                    {
                        // 对数滚轮：每格约 ×1.26，缩放范围很大也能轻松遍历（喇叭形的半径跨度大）
                        zoom = Mathf.Clamp(zoom * Mathf.Exp(scroll * 2.3f), data.layout.zoomMin, data.layout.zoomMax);
                        ApplyView();
                    }
                }
            }
            TickAnimation();
        }
    }
    /// <summary>
    /// 圆盘动画：整个圆盘顺时针自转（diskAngle 递减）+ 节点在各自世界线扇区内往返摆动（触边界速度取反）。
    /// 视角锁定（详情页打开）时暂停，保证锁定目标不漂移。
    /// </summary>
    private void TickAnimation()
    {
        diskAngle -= 360f / Mathf.Max(0.1f, data.layout.diskRotateSeconds) * Time.deltaTime;   // 负 = 顺时针
        float sectorHalf = 180f / Mathf.Max(1, data.worldlines != null ? data.worldlines.Count : 1);
        for (int i = 0; i < nodeViews.Count; i++)
        {
            NodeView v = nodeViews[i];
            if (v.isCenter) continue;
            v.angleOffset += v.velocity * Time.deltaTime;
            if (v.angleOffset > sectorHalf)
            {
                v.angleOffset = sectorHalf;
                v.velocity = -Mathf.Abs(v.velocity);
            }
            else if (v.angleOffset < -sectorHalf)
            {
                v.angleOffset = -sectorHalf;
                v.velocity = Mathf.Abs(v.velocity);
            }
            v.rt.anchoredPosition = NodePos(v);
        }
        for (int i = 0; i < lineViews.Count; i++)
        {
            UpdateLineGeometry(lineViews[i]);
        }
    }

    private void ApplyView()
    {
        mapRoot.localScale = new Vector3(zoom, zoom, 1f);
        mapRoot.localPosition = new Vector3(origin.x, origin.y, 0f);
        // 节点圆与文字保持固定屏幕大小（不随缩放变化），像隧道里迎面而来的标识牌
        float nodeScale = 1f / zoom;
        for (int i = 0; i < nodeViews.Count; i++)
        {
            nodeViews[i].rt.localScale = new Vector3(nodeScale, nodeScale, 1f);
        }
        // 圆环线宽按当前屏幕尺寸修正：保持固定像素粗细（不随缩放变化）
        float canvasScale = canvas != null ? canvas.scaleFactor : 1f;
        for (int i = 0; i < ringViews.Count; i++)
        {
            RingView rv = ringViews[i];
            if (rv.material == null) continue;
            float imagePx = Mathf.Max(1f, rv.radius * 2f * zoom * canvasScale);
            rv.material.SetFloat("_ThicknessUV", Mathf.Clamp(3f / imagePx, 0.0001f, 0.49f));
        }
        UpdateNodeVisibility();
    }

    /// <summary>
    /// 节点/圆环显隐：普通节点与圆环离原点太近（半径×缩放 < 阈值）就隐藏，
    /// 防止喇叭形压缩下内圈堆成一团——中心只留教程剧情，放大后内圈再逐圈浮现。
    /// 中心节点按缩放区间切换——缩小=教程剧情(displayZoom=out)，放大=原点剧情(in)。
    /// 锁定视角（详情页打开）时教程节点保持显示，避免放大后中心节点消失。
    /// </summary>
    private void UpdateNodeVisibility()
    {
        float hideR = data.layout.hideRadiusThreshold;
        float showOutMax = data.layout.zoomMin * 1f;        // 教程剧情只在"缩到最小、整树全景"时出现（约 zoom≤0.025）
        float showInMin = data.layout.zoomMax * 0.8f;       // 放大到这份上出现"原点剧情"
        for (int i = 0; i < ringViews.Count; i++)
        {
            RingView rv = ringViews[i];
            bool show = rv.radius * zoom >= hideR;
            if (rv.image.gameObject.activeSelf != show) rv.image.gameObject.SetActive(show);
        }
        for (int i = 0; i < nodeViews.Count; i++)
        {
            NodeView v = nodeViews[i];
            bool show;
            if (v.data.isCenter)
            {
                show = v.data.displayZoom == "out" ? (locked || zoom <= showOutMax) : zoom >= showInMin;
            }
            else
            {
                show = v.radius * zoom >= hideR;
            }
            if (v.rt.gameObject.activeSelf != show) v.rt.gameObject.SetActive(show);
        }
    }

    private void OnNodeClicked(NodeView view)
    {
        if (locked) return;
        if (messageOpen) return;
        // 未开发节点也正常打开详情页，进入动作由两个开始按钮拦截
        // 选中：锁定视角并放大到该节点。详情面板在屏幕右侧，把节点挪到它左边，避免被盖住
        selected = view;
        locked = true;
        lockZoom = zoom;
        lockOrigin = origin;
        if (detailTitle != null) detailTitle.text = view.data.title;
        if (detailSynopsis != null) detailSynopsis.text = view.data.synopsis;
        if (detailPanel != null) detailPanel.SetActive(true);
        float targetZoom = Mathf.Clamp(data.layout.detailZoom, data.layout.zoomMin, data.layout.zoomMax);
        Vector2 targetOrigin = -NodePos(view) * targetZoom;
        RectTransform prt = detailPanel != null ? detailPanel.GetComponent<RectTransform>() : null;
        if (prt != null)
        {
            // 节点停在详情面板左侧；面板很宽时最多推到屏幕左 1/4 处，防止移出屏幕
            float shift = -(prt.rect.width / 2f + 90f);
            float maxShift = -canvas.GetComponent<RectTransform>().rect.width * 0.25f;
            targetOrigin.x += Mathf.Max(shift, maxShift);
        }
        StartTween(targetOrigin, targetZoom, 0.4f);
    }

    private void UnlockView()
    {
        if (!locked) return;
        locked = false;
        selected = null;
        if (detailPanel != null) detailPanel.SetActive(false);
        StartTween(lockOrigin, lockZoom, 0.3f);
    }

    private void StartTween(Vector2 toOrigin, float toZoom, float duration)
    {
        if (tweenRoutine != null) StopCoroutine(tweenRoutine);
        tweenRoutine = StartCoroutine(TweenView(toOrigin, toZoom, duration));
    }

    private IEnumerator TweenView(Vector2 toOrigin, float toZoom, float duration)
    {
        Vector2 fromOrigin = origin;
        float fromZoom = zoom;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            zoom = Mathf.Lerp(fromZoom, toZoom, k);
            origin = Vector2.Lerp(fromOrigin, toOrigin, k);
            ApplyView();
            yield return null;
        }
        zoom = toZoom;
        origin = toOrigin;
        ApplyView();
        tweenRoutine = null;
    }

    // 拖拽与空白点击（由 MapDragCatcher 转发）
    public void OnMapPointerDown(PointerEventData e)
    {
        if (locked || messageOpen) return;
        if (Input.touchCount > 1) return;   // 双指捏合期间不进入单指拖拽
        dragging = true;
    }

    public void OnMapPointerUp(PointerEventData e)
    {
        dragging = false;
    }

    public void OnMapDrag(PointerEventData e)
    {
        if (!dragging || locked || messageOpen) return;
        if (Input.touchCount > 1) return;   // 捏合时手指位移不当作平移
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        origin += e.delta / scale;
        ApplyView();
    }

    public void OnMapClick()
    {
        if (locked) UnlockView();   // 锁定态点空白 = 退出详情
    }

    // ============ 详情框按钮 ============

    private void OnStartNewClicked()
    {
        if (selected == null) return;
        TimeMapNode nd = selected.data;
        if (!nd.developed)
        {
            // 未开发：详情页可以看，但进入操作在这里拦截
            ShowMessage("「" + nd.title + "」\n这个剧情还没有开发完成，敬请期待。");
            return;
        }
        // 入场券：默认背包（每种1个）里没有所需物品则拦下
        if (nd.requiredItemId > 0 && !ItemCatalog.HasInDefaultBag(nd.requiredItemId))
        {
            ShowMessage("进入需要持有「" + ItemCatalog.GetName(nd.requiredItemId) + "」\n从零开始没有这个物品，先去别的剧情里找找吧");
            return;
        }
        if (!StoryFileExists(nd.storyFile))
        {
            ShowMessage("剧情文件缺失：Resources/StoryData/" + nd.storyFile + ".json");
            return;
        }
        // 从新进入：不带任何存档数据（背包/体力/好感度走默认值）
        SaveSystem.pendingLoad = new SaveInfo { storyName = nd.storyFile, scriptIndex = 0 };
        SceneManager.LoadScene("PlotPlayer");
    }

    private void OnFromSaveClicked()
    {
        if (selected == null) return;
        TimeMapNode nd = selected.data;
        if (!nd.developed)
        {
            // 未开发：详情页可以看，但进入操作在这里拦截
            ShowMessage("「" + nd.title + "」\n这个剧情还没有开发完成，敬请期待。");
            return;
        }
        if (!StoryFileExists(nd.storyFile))
        {
            ShowMessage("剧情文件缺失：Resources/StoryData/" + nd.storyFile + ".json");
            return;
        }
        bool anySave = false;
        for (int i = 1; i <= SaveSystem.SLOT_COUNT; i++)
        {
            if (SaveSystem.SlotExists(i)) { anySave = true; break; }
        }
        if (!anySave)
        {
            ShowMessage("还没有任何存档可以继承\n先通关一个剧情，留下存档吧");
            return;
        }
        // 打开继承面板（Inherit 模式）：选槽后只继承背包/体力/好感度进入新剧情
        ArchivePanel.PendingStoryFile = nd.storyFile;
        ArchivePanel.PendingRequiredItemId = nd.requiredItemId;
        if (archivePanel != null)
        {
            // 层级顺序（从下到上）：拦截层 → 面板 → 弹出按钮。
            // 注意：面板先置顶，拦截层紧跟其下，弹出按钮最后置顶，
            // 否则拦截层会盖住面板/按钮，导致点"继承进入"像点空白处一样把按钮点没
            archivePanel.transform.SetAsLastSibling();
            if (archivePanel.blockerGo != null)
            {
                // 拦截层跟随面板的父级（信箱容器或画布），插到面板正下方，层级才不会错位
                archivePanel.blockerGo.transform.SetParent(archivePanel.transform.parent, false);
                archivePanel.blockerGo.transform.SetAsLastSibling();
                archivePanel.blockerGo.transform.SetSiblingIndex(archivePanel.transform.GetSiblingIndex() - 1);
            }
            if (archivePanel.actionBtnGo != null)
            {
                archivePanel.actionBtnGo.transform.SetAsLastSibling();
            }
            archivePanel.OpenOrCloseArchive();
        }
    }

    private bool StoryFileExists(string storyFile)
    {
        if (string.IsNullOrEmpty(storyFile)) return false;
        return Resources.Load<TextAsset>("StoryData/" + storyFile) != null;
    }

    // ============ 弹窗 ============

    /// <summary>通用消息弹窗（ArchivePanel 的入场券拦截也调这里）</summary>
    public void ShowMessage(string msg)
    {
        if (messageText != null) messageText.text = msg;
        messageOpen = true;
        if (messagePanel != null) messagePanel.SetActive(true);
    }

    private void HideMessage()
    {
        messageOpen = false;
        if (messagePanel != null) messagePanel.SetActive(false);
    }

    // ============ UI 构建小工具 ============

    private Text MakeText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static Texture2D MakeWhiteTexture()
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Color[] px = new Color[4];
        for (int i = 0; i < 4; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>圆形节点贴图（径向柔边，白色，靠 Image.color 染色）</summary>
    private static Texture2D MakeCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size / 2f;
        float r = half - 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 1.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }
}

// ============ 数据模型（对应 Resources/StoryData/time_map.json） ============

[Serializable]
public class TimeMapData
{
    public TimeMapLayout layout;
    public List<TimeMapWorldline> worldlines;
    public List<TimeMapNode> nodes;
    [NonSerialized] public List<string[]> links;   // JsonUtility 不解析，由 TimeMapManager 手动解析
}

[Serializable]
public class TimeMapLayout
{
    public string radiusMode = "horn";          // "horn"=喇叭形(指数,可见圈数近似恒定,默认) "perspective"=锥形 "linear"=均匀
    public float hornScale = 90f;               // 喇叭形整体尺度
    public float hornLambda = 0.325f;            // 喇叭形增长速率(每圈半径比 e^λ)，越大可见圈数越少、内外差距越大
    public float radiusScale = 15000f;          // perspective 模式的整体尺度
    public float perspectivePole = 50f;         // perspective 模式的极点(时间点)，必须大于最大 timePoint；越小透视越强
    public float circleSpacing = 140f;          // linear 模式的圈距
    public float zoomMin = 0.0005f;             // 最小缩放：喇叭形最外圈半径 44 万 px × 0.0025 ≈ 1100px，几乎整树入屏
    public float zoomMax = 40f;                 // 最大缩放：能贴近第一圈
    public float hideRadiusThreshold = 140f;      // 节点离原点这么近（px）才隐藏
    public float detailZoom = 2f;
    public float lineAlpha = 0.65f;             // 连线不透明度（加深）
    public float nodeSpeedMax = 12f;            // 节点在扇区内的最大角速度（度/秒）
    public float diskRotateSeconds = 20f;
}

[Serializable]
public class TimeMapWorldline
{
    public int id;
    public string name;
    public string color;
}

[Serializable]
public class TimeMapNode
{
    public string id;
    public string title;
    public string synopsis;
    public string storyFile;
    public bool isCenter;
    public string displayZoom;      // 中心节点："out"=完全缩小显示(教程) "in"=完全放大显示(原点)
    public int worldlineId;
    public int timePoint;
    public float angleOffset;
    public int requiredItemId;      // 入场券物品 id，0=无
    public bool developed;
}

// ============ 视图包装 ============

public class NodeView
{
    public TimeMapNode data;
    public RectTransform rt;
    public Image image;
    public float radius;         // 到原点的距离（mapRoot 本地坐标，未缩放）
    public float baseAngle;      // 世界线扇区内的基准角度（含随机散布）
    public float angleOffset;    // 扇区内的当前摆动偏移（度）
    public float velocity;       // 角速度（度/秒），触到扇区边界取反
    public bool isCenter;        // 中心节点（教程/原点）：固定在圆心不动
}

public class LineView
{
    public NodeView a;
    public NodeView b;
    public float baseWidth;
    public Image image;
}
public class RingView
{
    public Image image;
    public float radius;
    public Material material;   // UI/Ring 材质，按屏幕尺寸修正线宽
}


/// <summary>全屏底层点击层：拖拽平移 + 锁定态点空白解锁</summary>
public class MapDragCatcher : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerClickHandler
{
    public TimeMapManager owner;

    public void OnPointerDown(PointerEventData e) { if (owner != null) owner.OnMapPointerDown(e); }
    public void OnPointerUp(PointerEventData e) { if (owner != null) owner.OnMapPointerUp(e); }
    public void OnDrag(PointerEventData e) { if (owner != null) owner.OnMapDrag(e); }
    public void OnPointerClick(PointerEventData e) { if (owner != null) owner.OnMapClick(); }
}
