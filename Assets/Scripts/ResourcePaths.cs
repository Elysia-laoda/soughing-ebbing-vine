using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 资源路径中心（2026-10-18 用户重构资源区之后统一走这里）。
/// 新结构：
///   背景    Sprites/BG/&lt;剧情&gt;/&lt;图名&gt;
///   立绘    Sprites/Characters/&lt;角色&gt;/&lt;剧情&gt;/&lt;表情&gt;
///   语音    AudioClips/Dialogue/&lt;角色&gt;/&lt;剧情&gt;/&lt;序号&gt;
/// 兼容旧结构：找不到时退回根目录（Sprites/BG/&lt;图名&gt;、Sprites/Characters/&lt;角色&gt;/&lt;表情&gt;）。
/// 表情支持跨剧情强引用：「@&lt;剧情&gt;/&lt;表情&gt;」，例如 "@Tutorial/normal"。
/// </summary>
public static class ResourcePaths
{
    // ---------- 背景 ----------
    public static string BGSpritePath(string story, string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return null;
        if (string.IsNullOrEmpty(story)) return "Sprites/BG/" + spriteName;
        return "Sprites/BG/" + story + "/" + spriteName;
    }
    public static Sprite LoadBG(string story, string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return null;
        Sprite sp = Resources.Load<Sprite>(BGSpritePath(story, spriteName));
        if (sp != null) return sp;
        // 与剧情无关的通用散图（黑场等）放在 Sprites/BG/Universal/
        sp = Resources.Load<Sprite>("Sprites/BG/Universal/" + spriteName);
        if (sp != null) return sp;
        return Resources.Load<Sprite>("Sprites/BG/" + spriteName);   // 旧结构兜底
    }

    // ---------- 立绘 ----------
    /// <summary>立绘资源相对路径（不含 "Sprites/Characters/" 前缀）；支持 @剧情/表情</summary>
    public static string CharacterSpritePath(string story, string characterName, string emotion)
    {
        if (string.IsNullOrEmpty(characterName)) return null;
        if (string.IsNullOrEmpty(emotion)) emotion = "normal";
        string root = "Sprites/Characters/" + characterName + "/";
        if (emotion.Length > 1 && emotion[0] == '@') return root + emotion.Substring(1);   // 跨剧情强引用
        if (string.IsNullOrEmpty(story)) return root + emotion;
        return root + story + "/" + emotion;
    }
    public static Sprite LoadCharacter(string story, string characterName, string emotion)
    {
        if (string.IsNullOrEmpty(characterName)) return null;
        string primary = CharacterSpritePath(story, characterName, emotion);
        Sprite sp = string.IsNullOrEmpty(primary) ? null : Resources.Load<Sprite>(primary);
        if (sp != null) return sp;
        string emo = string.IsNullOrEmpty(emotion) ? "normal" : emotion;
        if (emo.Length > 1 && emo[0] == '@')   // 跨剧情找不到时不再兜底，避免拿到错的图
        {
            return null;
        }
        return Resources.Load<Sprite>("Sprites/Characters/" + characterName + "/" + emo);   // 旧结构兜底
    }

    // ---------- 语音 ----------
    /// <summary>相对 "AudioClips/Dialogue/" 的语音路径：&lt;角色&gt;/&lt;剧情&gt;/&lt;序号&gt;</summary>
    public static string DialogueAudioPath(string story, string characterName, string soundPath)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(soundPath)) return null;
        if (string.IsNullOrEmpty(story)) return characterName + "/" + soundPath;
        return characterName + "/" + story + "/" + soundPath;
    }
}
