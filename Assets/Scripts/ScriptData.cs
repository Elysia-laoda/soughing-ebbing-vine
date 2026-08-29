using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ScriptData//剧本数据变量
{
    public int loadType;//载入资源类型 1.背景 2.人物 3.事件
    public string name;//角色名称
    public int characterID;//角色ID
    public string spriteName;//图片名称
    public string dialogueContent;//对话内容
    public int characterPos;//人物位置 1.左 2.右 3.中
    public bool ifRotate;//是否需要翻转
    public int soundType;//音频类型 1.对话 2.音效 3.音乐
    public string soundPath;//音频路径
    public int energyValue;//体力值(是改变值不是当前值)
    public int favorability;//好感度值(是改变值不是当前值)
    public int eventID;//事件ID 1.显示选项 2.跳转剧本 3.显示隐藏遮罩 4.特殊事件 5.显示隐藏角色 6.获得物品
    public int eventData;//事件数据 类1.选项数量 类2.跳转剧本ID 类3.0隐藏1显示 类4.特殊事件ID 类5.物品ID
    public int scriptID;//剧本标记位
    public string emotion;
}

[Serializable]
public class ScriptDataList//JsonUtility不能直接解析顶层数组,需要用这个包装类包一层
{
    public List<ScriptData> scriptDatas;
}