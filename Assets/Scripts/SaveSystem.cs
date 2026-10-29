using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 存读档的磁盘读写与跨场景数据传递。
/// 存档文件位置：Application.persistentDataPath/Saves/Save_序号.json（出包后可写、升级不丢档）。
/// 剧情文件位置：Assets/Resources/StoryData/ 下（运行时按名字用 Resources.Load 读取）。
/// </summary>
public static class SaveSystem
{
    public const string DEFAULT_STORY_NAME = "Tutorial";  // 新游戏默认剧情文件名（不含扩展名；原 Story1 已改名）
    public const int SLOT_COUNT = 4;                    // 存档槽数量
    public static SaveInfo pendingLoad;                 // 主菜单→剧情场景的读档数据，用完即清

    public static string SaveDir
    {
        get { return Path.Combine(Application.persistentDataPath, "Saves"); }
    }

    public static string GetSlotPath(int slotId)
    {
        return Path.Combine(SaveDir, "Save_" + slotId + ".json");
    }

    public static bool SlotExists(int slotId)
    {
        return File.Exists(GetSlotPath(slotId));
    }

    /// <summary>读取某个槽位的存档；不存在或损坏时返回 null（面板按空白槽处理）</summary>
    public static SaveInfo LoadSlot(int slotId)
    {
        string path = GetSlotPath(slotId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonUtility.FromJson<SaveInfo>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning("存档文件损坏，已按空白槽处理: " + path + " (" + e.Message + ")");
            return null;
        }
    }

    /// <summary>写入存档（覆盖旧档），自动记录保存时间</summary>
    public static void SaveSlot(int slotId, SaveInfo info)
    {
        Directory.CreateDirectory(SaveDir);
        info.saveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        File.WriteAllText(GetSlotPath(slotId), JsonUtility.ToJson(info));
    }

    public static void ClearPendingLoad()
    {
        pendingLoad = null;
    }
    /// <summary>删除某个槽位的存档文件（文件不存在时静默忽略）</summary>
    public static void DeleteSlot(int slotId)
    {
        string path = GetSlotPath(slotId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

[Serializable]
public class SaveInfo
{
    public string storyName;            // 剧情 JSON 文件名（不含扩展名）
    public int scriptIndex;             // 剧情索引，读档时从这里继续播
    public string saveTime;             // 存档时间，仅用于槽位显示
    public string bgSpriteName;         // 当前背景图名（Sprites/BG/ 下）
    public string musicName;            // 当前BGM名（AudioClips/Music/ 下）
    public string surroundingName;      // 当前环境音名（AudioClips/Surrounding/ 下）
    public int eneryValue;              // 当前体力值（沿用 GameManager.eneryValue 的拼写）
    public List<FavorSaveData> favorability;  // 好感度快照（Dictionary 无法直接序列化，转 List 存）
    public List<ItemSaveData> items;    // 背包快照（物品 id + 数量）
    public List<CharacterSnapshot> characters;  // 立绘快照（下标 0/1 = characterID 0/1；旧档为 null
}
[Serializable]
public class CharacterSnapshot
{
    public bool shown;          // 存档时这个立绘槽是否显示中
    public string spritePath;   // 资源相对路径 <角色名>/<表情>（对应 Sprites/Characters/ 下）
    public int posId;           // 位置 1=左 2=右 3=中（0=这条线上没设过）
    public bool rotate;         // 是否水平翻转
}
[Serializable]
public class ItemSaveData
{
    public int id;
    public int count;
}

[Serializable]
public class FavorSaveData
{
    public string name;
    public int value;
}
