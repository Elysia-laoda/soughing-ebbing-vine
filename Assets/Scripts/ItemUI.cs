using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ItemUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    public ItemInfo itemInfo;
    private Transform imgBagBG;
    private CanvasGroup canvasGroup;
    private bool dragMoved;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }
    public void OnBeginDrag(PointerEventData eventData)
    {
        dragMoved = false;
        UIManager.Instance.HideUseBtn();
        imgBagBG = transform.parent;
        canvasGroup.blocksRaycasts = false;
        transform.SetParent(UIManager.Instance.transform);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 移动超过 5 个像素就算拖拽，后续松开时的 onClick 会被 OnClickItem 里的守卫拦掉
        if ((eventData.position - eventData.pressPosition).magnitude > 5f) dragMoved = true;
        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        GameObject pointObj = eventData.pointerEnter;
        if (pointObj == null || pointObj.tag == "Bag")
        {
            //鼠标下方是空白则回归原位
            transform.SetParent(imgBagBG);
            transform.localPosition = Vector3.zero;
        }
        else if (pointObj.tag == "Item")
        {
            if (pointObj.GetComponent<ItemUI>().itemInfo.id == itemInfo.id)
            {
                // 数量合并：把拖过去的数量并入目标物品，然后销毁拖过来的格子
                ItemUI targetUI = pointObj.GetComponent<ItemUI>();
                targetUI.itemInfo.count += itemInfo.count;
                UIManager.Instance.RefreshTipIfShowing(targetUI.itemInfo);
                Destroy(gameObject);
            }
            else
            {
                //位置交换
                transform.SetParent(pointObj.transform.parent);
                transform.localPosition = Vector3.zero;
                transform.SetSiblingIndex(pointObj.transform.GetSiblingIndex());
                pointObj.transform.SetSiblingIndex(transform.parent.childCount + 1);
            }
        }
        else
        {
            transform.SetParent(imgBagBG);
            transform.localPosition = Vector3.zero;

        }
        canvasGroup.blocksRaycasts = true;
    }
    public void OnClickItem()
    {
        // 拖拽结束时也会触发一次点击，拖过就忽略
        if (dragMoved) { dragMoved = false; return; }
        // 只有带效果物品才弹使用按钮（小熊这类无效果物品点了没反应）
        if (!GameManager.Instance.itemEffectDict.ContainsKey(itemInfo.id)) return;
        UIManager.Instance.ShowUseBtn(itemInfo, transform.position);
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        UIManager.Instance.ShowTipInfo(itemInfo, transform.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        UIManager.Instance.HideTipInfo();
    }
}
