using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 存档面板里的一个槽位。由 ArchivePanel.RefreshSlots 用代码生成（不做预制体文件）。
/// 槽位显示内容完全由磁盘上的存档文件驱动：有档显示"剧情名+存档时间"，没档显示"空"。
/// </summary>
public class ArchiveSlotUI : MonoBehaviour
{
    public int slotId;      // 槽位编号（从 1 开始），对应 Save_slotId.json
    public bool occupied;   // 当前槽位是否有存档
    public Text textInfo;   // 槽内文字

    private void Awake()
    {
        Button btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            if (ArchivePanel.Instance != null)
            {
                ArchivePanel.Instance.OnSlotClicked(this);
            }
        });
    }

    /// <summary>重新从磁盘读取该槽位存档并刷新显示</summary>
    public void RefreshText()
    {
        SaveInfo info = SaveSystem.LoadSlot(slotId);
        occupied = info != null;
        if (textInfo != null)
        {
            textInfo.text = occupied ? info.storyName + "\n" + info.saveTime : "空";
        }
    }
}
