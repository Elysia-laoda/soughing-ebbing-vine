using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 安卓/多分辨率适配（自动安装，无需挂任何场景，DontDestroyOnLoad 常驻）：
/// 1. 强制横屏：启动即锁定 LandscapeLeft——即使手机系统开着竖屏方向锁，
///    应用的请求方向优先级更高，也会被掰成横屏；
/// 2. 16:9 信箱（letterbox，纯 Canvas 方案，不碰相机）：
///    - Canvas 保持 Overlay（与安卓实测正常的渲染路径完全一致）；
///    不用 CanvasScaler（团结引擎对运行时改 scaler 配置不可靠），
///      改为每帧直接写 canvas.scaleFactor = min(屏幕宽/1920, 屏幕高/1080)，
///      画布逻辑尺寸两个方向恒 ≥ 1920×1080，任何屏幕都不可能裁掉内容；
///    - 代码创建一个 1920×1080 的"内容容器"，把画布现有 UI 全部装进去
///      （锚点/偏移不变，层级顺序不变）——UI 永远按 1920×1080 渲染，零拉伸；
///    - 容器外剩余区域用灰色条补齐。
/// 每次场景加载与屏幕尺寸变化时自动重新应用；同一场景内幂等（不会重复建容器）。
/// </summary>
public class AspectLetterbox : MonoBehaviour
{
    private static AspectLetterbox _instance;
    private Canvas canvas;
    private RectTransform container;      // 16:9 内容容器（运行时创建，随场景销毁）
    private Image barLeft, barRight, barTop, barBottom;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        if (_instance != null) return;
        GameObject go = new GameObject("AspectLetterbox");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<AspectLetterbox>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        ForceLandscape();
        RebuildForScene();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ForceLandscape();
        RebuildForScene();
    }

    private void Update()
    {
        // 每帧刷新：安卓方向切换是异步的（竖屏→横屏可能跨多帧，且可能在
        // 首帧之后才生效），不依赖"尺寸变化事件"，直接每帧按当前屏幕重算信箱。
        // 开销只有一次比较+几次赋值，可以忽略。
        ApplyLetterbox();
    }

    /// <summary>强制横屏（安卓上即使系统开了竖屏方向锁，应用请求的方向仍然生效）</summary>
    private static void ForceLandscape()
    {
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
    }

    /// <summary>
    /// 新场景：重找 Canvas；容器已被旧场景销毁（Unity 对象判空）则重建并装入 UI。
    /// Start 与 sceneLoaded 同帧都会走到这里，靠 container 判空保证只建一次。
    /// </summary>
    private void RebuildForScene()
    {
        canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null) return;

        if (container == null)
        {
            // 1) 不依赖 CanvasScaler：团结引擎对运行时改 scaler 配置支持不佳
            //    （match 切换不可靠，实测横屏仍按竖屏配置裁边）。
            //    改为每帧直接写 canvas.scaleFactor（见 ApplyLetterbox）。
            //    这里把可能残留的 scaler 清掉，防止它覆盖我们写的 scaleFactor。
            CanvasScaler staleScaler = canvas.GetComponent<CanvasScaler>();
            if (staleScaler != null) Destroy(staleScaler);

            // 2) 16:9 内容容器：居中、1920×1080
            GameObject containerGo = new GameObject("__UIContainer");
            containerGo.transform.SetParent(canvas.transform, false);
            containerGo.transform.SetSiblingIndex(0);
            container = containerGo.AddComponent<RectTransform>();
            container.anchorMin = container.anchorMax = container.pivot = new Vector2(0.5f, 0.5f);
            container.anchoredPosition = Vector2.zero;
            container.sizeDelta = new Vector2(1920f, 1080f);

            // 3) 把画布现有 UI 全部装进容器：容器固定占 0 号位，
            //    每次搬 1 号位（第一个未搬的 UI）→ 正序逐个后插，层级顺序不变。
            //    ⚠ 不能用带快照计数的 GetChild(i) 正序循环：搬一个少一个，
            //    会隔一个跳一个并最终越界（UnityException: Transform child out of bounds）
            while (canvas.transform.childCount > 1)
            {
                Transform child = canvas.transform.GetChild(1);
                child.SetParent(container, false);
            }

            // 4) 灰边条（插到容器下层）
            barLeft = CreateBar("Bar_Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            barRight = CreateBar("Bar_Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f));
            barTop = CreateBar("Bar_Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            barBottom = CreateBar("Bar_Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        }

        ApplyLetterbox();
    }

    private Image CreateBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetSiblingIndex(0);   // 每次前插到最底，四个条最终都在容器之前
        Image img = go.AddComponent<Image>();
        img.sprite = CreateWhiteSprite();
        img.color = new Color(0.13f, 0.13f, 0.13f, 1f);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return img;
    }

    /// <summary>按当前屏幕重算信箱：匹配方向（match）+ 灰边尺寸（容器恒为 1920×1080，剩余区域全是灰条）。
    /// 每帧调用（见 Update），保证竖屏→横屏切换后立刻修正。
    /// </summary>
    private void ApplyLetterbox()
    {
        if (canvas == null || container == null) return;

        // 统一缩放：把 1920×1080 参考完整放进屏幕（取两个方向缩放的最小值）。
        // 这样画布逻辑尺寸两个方向都 ≥ 参考尺寸——容器永远放得下，
        // 上下/左右被裁在结构上不可能发生；也不依赖 CanvasScaler 的 match 行为。
        float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        if (Mathf.Abs(canvas.scaleFactor - scale) > 0.0001f)
        {
            canvas.scaleFactor = scale;   // 直接写 Canvas 属性，绕过 scaler
        }

        float logicalW = Screen.width / scale;
        float logicalH = Screen.height / scale;
        float barW = Mathf.Max(0f, (logicalW - 1920f) / 2f);
        float barH = Mathf.Max(0f, (logicalH - 1080f) / 2f);

        if (barLeft != null) barLeft.rectTransform.sizeDelta = new Vector2(barW, 0f);
        if (barRight != null) barRight.rectTransform.sizeDelta = new Vector2(barW, 0f);
        if (barTop != null) barTop.rectTransform.sizeDelta = new Vector2(0f, barH);
        if (barBottom != null) barBottom.rectTransform.sizeDelta = new Vector2(0f, barH);
    }

    private static Sprite _whiteSprite;
    private static Sprite CreateWhiteSprite()
    {
        if (_whiteSprite != null) return _whiteSprite;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Color[] px = new Color[4];
        for (int i = 0; i < 4; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
        return _whiteSprite;
    }
}

