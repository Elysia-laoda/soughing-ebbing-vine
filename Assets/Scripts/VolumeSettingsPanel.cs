using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 音量设置面板。两个场景各挂一份（主菜单 + 剧情播放器），逻辑相同。
/// 4 个滑条按 AudioChannel 顺序拖引用：Music/BGM → Sound/音效 → Dialogue/语音 → Surrounding/环境音。
/// 拖动滑条实时改音量并写 PlayerPrefs（跨场景生效）；打开面板时从 PlayerPrefs 回读滑条位置。
/// 关闭/重置按钮全部代码绑定，场景里不用配 onClick。
/// 注意：脚本可以直接挂在 Panel_Volume 上（面板初始 SetActive(false) 也没关系），
/// Instance 是懒加载的，会在场景里主动搜索未激活对象；滑条/按钮绑定在面板第一次被打开时由 Awake 完成。
/// </summary>
public class VolumeSettingsPanel : MonoBehaviour
{
    private static VolumeSettingsPanel _instance;

    /// <summary>
    /// 懒加载单例：面板初始是未激活状态（Awake 不会执行，Instance 不会被赋值），
    /// 所以取不到时主动在场景里搜（包含未激活对象），保证 VolumeSettingsPanel.Instance.xxx 任何时候都可用。
    /// </summary>
    public static VolumeSettingsPanel Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<VolumeSettingsPanel>(true);
            }
            return _instance;
        }
        private set { _instance = value; }
    }

    public GameObject panelGo;    // Panel_Volume（面板根对象）
    public Slider[] sliders;      // 4 个滑条，顺序必须与 AudioChannel 一致
    public Text[] valueTexts;     // 4 个百分比文本，与滑条一一对应（可不拖全）
    public Button closeBtn;       // Btn_Close 关闭按钮
    public Button resetBtn;       // Btn_Reset 恢复默认按钮（可选）

    private bool open;

    private void Awake()
    {
        Instance = this;
        // 按钮全部代码绑定，避免在场景里手动配 onClick
        if (closeBtn != null)
        {
            closeBtn.onClick.RemoveAllListeners();
            closeBtn.onClick.AddListener(Close);
        }
        if (resetBtn != null)
        {
            resetBtn.onClick.RemoveAllListeners();
            resetBtn.onClick.AddListener(ResetToDefault);
        }
        if (sliders != null)
        {
            for (int i = 0; i < sliders.Length; i++)
            {
                if (sliders[i] == null) continue;
                int index = i;   // 闭包捕获循环变量，避免全部回调拿到最后一个索引
                sliders[i].onValueChanged.RemoveAllListeners();
                sliders[i].onValueChanged.AddListener((value) => OnSliderChanged(index, value));
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>开关音量面板，挂到场景"音量设置"按钮的 onClick 上</summary>
    public void OpenOrCloseSettings()
    {
        open = !open;
        if (open)
        {
            RefreshFromSaved();   // 每次打开都回读保存的音量，保证滑条位置与真实音量一致
        }
        if (panelGo != null) panelGo.SetActive(open);
    }

    /// <summary>整体收起面板（菜单按钮收起时由 UIManager 调用）</summary>
    public void Hide()
    {
        if (open) OpenOrCloseSettings();
    }

    private void Close()
    {
        if (open) OpenOrCloseSettings();
    }

    /// <summary>滑条拖动：实时生效 + 保存，并刷新旁边的百分比文字</summary>
    private void OnSliderChanged(int index, float value)
    {
        AudioSourceManager.SetChannelVolume((AudioChannel)index, value);
        UpdateValueText(index);
    }

    /// <summary>打开面板时把滑条和百分比文字对齐到已保存的音量</summary>
    private void RefreshFromSaved()
    {
        if (sliders == null) return;
        for (int i = 0; i < sliders.Length; i++)
        {
            if (sliders[i] == null) continue;
            sliders[i].value = AudioSourceManager.GetChannelVolume((AudioChannel)i);
            UpdateValueText(i);
        }
    }

    /// <summary>恢复默认音量（BGM/音效/环境音 0.5、语音 1.0）</summary>
    public void ResetToDefault()
    {
        if (sliders == null) return;
        for (int i = 0; i < sliders.Length; i++)
        {
            if (sliders[i] == null) continue;
            sliders[i].value = AudioSourceManager.GetDefaultVolume((AudioChannel)i);
        }
    }

    private void UpdateValueText(int index)
    {
        if (valueTexts == null || index >= valueTexts.Length || valueTexts[index] == null) return;
        if (sliders == null || index >= sliders.Length || sliders[index] == null) return;
        valueTexts[index].text = Mathf.RoundToInt(sliders[index].value * 100) + "%";
    }
}
