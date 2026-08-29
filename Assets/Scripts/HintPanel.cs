using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 场景首次进入提示框。三个场景各有一个（对象名统一叫 Img_Tip，内含 Btn_Close 关闭按钮 + 提示文字）。
/// 由场景入口脚本在 Start 里调用 HintPanel.ShowIfFirstTime：
///   - 已看过的场景不再弹（PlayerPrefs 记键，跨场景、跨游戏重启都有效）；
///   - 弹出时淡入，点关闭按钮淡出；没有关闭按钮时 8 秒后自动收起。
/// 提示文字由调用方传入（各场景内容不同），关闭后才写 PlayerPrefs 标记。
/// 注意：面板在场景里是未激活的，ShowIfFirstTime 会先激活并置为全透明（防延迟期间闪现），
/// 再启动协程做延迟+淡入——未激活的物体不能 StartCoroutine。
/// </summary>
public class HintPanel : MonoBehaviour
{
    private string prefKey;
    private CanvasGroup group;
    private Coroutine fadeRoutine;
    private bool closing;

    /// <summary>
    /// 场景第一次进入时显示提示框。canvasRoot 传场景 Canvas（面板是它的直接子物体）。
    /// </summary>
    public static void ShowIfFirstTime(Transform canvasRoot, string panelName, string prefKey, string text, float delay = 0.8f)
    {
#if UNITY_EDITOR
        // 编辑器调试：每次进入场景都弹提示，方便反复看效果
#else
        if (PlayerPrefs.GetInt(prefKey, 0) != 0) return;   // 这个场景看过了，不再弹
#endif
        if (canvasRoot == null) return;
        Transform t = canvasRoot.Find(panelName);
        if (t == null)
        {
            Debug.LogWarning("HintPanel：场景里没有 " + panelName + "，跳过提示");
            return;
        }
        HintPanel hp = t.GetComponent<HintPanel>();
        if (hp == null) hp = t.gameObject.AddComponent<HintPanel>();

        // 提示文字：优先找名为 Text_Toast 的文本，否则用面板直属的第一个 Text
        Text[] texts = t.GetComponentsInChildren<Text>(true);
        Text target = null;
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i].name == "Text_Toast") { target = texts[i]; break; }
        }
        if (target == null)
        {
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].transform.parent == t) { target = texts[i]; break; }
            }
        }
        if (target != null) target.text = text;

        hp.prefKey = prefKey;
        hp.StartShow(delay);
    }

    private void StartShow(float delay)
    {
        // 面板在场景里是未激活的：先激活并置为全透明，否则无法启动协程（且延迟期间会闪一下）
        gameObject.SetActive(true);
        transform.SetAsLastSibling();   // 盖在最上层
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(ShowRoutine(delay));
    }

    private IEnumerator ShowRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (this == null) yield break;
        // 关闭按钮 = 面板里第一个 Button（场景里是 Btn_Close）
        Button[] btns = GetComponentsInChildren<Button>(true);
        Button closeBtn = btns.Length > 0 ? btns[0] : null;
        if (closeBtn != null)
        {
            closeBtn.onClick.RemoveAllListeners();
            closeBtn.onClick.AddListener(Close);
        }

        // 淡入
        float t = 0f;
        while (t < 0.4f)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Clamp01(t / 0.4f);
            yield return null;
        }
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        // 没有关闭按钮时兜底：8 秒后自动收起
        if (closeBtn == null)
        {
            yield return new WaitForSeconds(8f);
            Close();
        }
    }

    private void Close()
    {
        if (closing) return;
        closing = true;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator FadeOutRoutine()
    {
        if (group == null)
        {
            gameObject.SetActive(false);
            Finish();
            yield break;
        }
        group.interactable = false;
        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.deltaTime;
            group.alpha = 1f - Mathf.Clamp01(t / 0.3f);
            yield return null;
        }
        group.alpha = 0f;
        gameObject.SetActive(false);
        Finish();
    }

    private void Finish()
    {
        closing = false;
        if (!string.IsNullOrEmpty(prefKey))
        {
            PlayerPrefs.SetInt(prefKey, 1);   // 记入"已看过"，之后不再弹
            PlayerPrefs.Save();
        }
    }
}

