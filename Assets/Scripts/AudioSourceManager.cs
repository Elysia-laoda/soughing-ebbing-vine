using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 声音通道枚举：与 AudioSourceManager 的 4 个音源一一对应。
/// 音量设置面板（VolumeSettingsPanel）按此顺序拖滑条：Music/Sound/Dialogue/Surrounding。
/// </summary>
public enum AudioChannel
{
    Music,      // 背景音乐（同时控制开场视频音轨音量）
    Sound,      // 音效
    Dialogue,   // 对话语音
    Surrounding // 环境音
}

public class AudioSourceManager : MonoBehaviour
{
    public static AudioSourceManager Instance { get; private set; }
    public AudioSource sourceAudio;
    public AudioSource musicAudio;
    public AudioSource dialogueAudio;
    public AudioSource surroundingAudio;

    private void Awake()
    {
        Instance = this;
    }
    private void Start()
    {
        // 场景切换后把 PlayerPrefs 里保存的音量套用到各音源（没拖音源的场景自动跳过）
        ApplyAllVolumes();
    }

    /// <summary>把 PlayerPrefs 里保存的音量套用到所有音源</summary>
    public void ApplyAllVolumes()
    {
        ApplyVolume(AudioChannel.Music);
        ApplyVolume(AudioChannel.Sound);
        ApplyVolume(AudioChannel.Dialogue);
        ApplyVolume(AudioChannel.Surrounding);
    }

    /// <summary>
    /// 设置某通道音量：实时生效 + 写入 PlayerPrefs，供设置面板调用。
    /// 本场景没挂 AudioSourceManager（如主菜单未挂脚本）时也能正常保存，进剧情场景后生效。
    /// </summary>
    public static void SetChannelVolume(AudioChannel channel, float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKey(channel), volume);
        if (Instance != null)
        {
            Instance.ApplyVolume(channel);
        }
        // 开场视频音轨是 Direct 直通模式不走 AudioSource，由背景音乐滑条一并控制
        if (channel == AudioChannel.Music && VideoManager.Instance != null)
        {
            VideoManager.Instance.UpdateVideoVolume();
        }
    }

    /// <summary>读取某通道已保存的音量（从没设置过时用默认值）</summary>
    public static float GetChannelVolume(AudioChannel channel)
    {
        string key = PrefKey(channel);
        return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetFloat(key) : GetDefaultVolume(channel);
    }

    /// <summary>通道默认音量：与场景里初始音源音量一致（BGM/音效/环境音 0.5、语音 1.0），玩家没动过滑条时听感不变</summary>
    public static float GetDefaultVolume(AudioChannel channel)
    {
        switch (channel)
        {
            case AudioChannel.Music: return 0.5f;
            case AudioChannel.Sound: return 0.5f;
            case AudioChannel.Dialogue: return 1f;
            case AudioChannel.Surrounding: return 0.5f;
            default: return 1f;
        }
    }

    private static string PrefKey(AudioChannel channel)
    {
        return "AudioVolume_" + channel;
    }

    private void ApplyVolume(AudioChannel channel)
    {
        AudioSource src = GetSource(channel);
        if (src != null)
        {
            src.volume = GetChannelVolume(channel);
        }
    }

    private AudioSource GetSource(AudioChannel channel)
    {
        switch (channel)
        {
            case AudioChannel.Music: return musicAudio;
            case AudioChannel.Sound: return sourceAudio;
            case AudioChannel.Dialogue: return dialogueAudio;
            case AudioChannel.Surrounding: return surroundingAudio;
            default: return null;
        }
    }

    public void PlaySound(string soundPath)
    {
        //播放音效
        sourceAudio.PlayOneShot(Resources.Load<AudioClip>("AudioClips/Sound/" + soundPath));
    }
    public void PlayMusic(string musicPath, bool loop = true)
    {
        //播放音乐
        musicAudio.loop = loop;
        musicAudio.clip = (Resources.Load<AudioClip>("AudioClips/Music/" + musicPath));
        musicAudio.Play();
    }
    public void StopMusic()
    {
        //停止音乐
        musicAudio.Stop();
    }
    public void PlayDialogue(string dialoguePath)
    {
        dialogueAudio.Stop();
        //播放对话音频
        dialogueAudio.PlayOneShot(Resources.Load<AudioClip>("AudioClips/Dialogue/" + dialoguePath));
    }
    public void PlaySurrounding(string surroundingPath)
    {
        surroundingAudio.loop = true;
        surroundingAudio.clip = (Resources.Load<AudioClip>("AudioClips/Surrounding/" + surroundingPath));
        surroundingAudio.Play();

    }
    public void StopSurrounding()
    {
        //停止环境音
        surroundingAudio.Stop();
    }

}
