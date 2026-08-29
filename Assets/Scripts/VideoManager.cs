using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class VideoManager : MonoBehaviour
{
    public static VideoManager Instance { get; private set; }

    public RawImage rawImage;        // 显示视频的 RawImage（场景里拖进来）
    public VideoPlayer videoPlayer;  // VideoPlayer 组件（场景里拖进来）
    private System.Action onFinished;

    private void Awake()
    {
        Instance = this;
        // 开场前把视频画面设成透明，避免露白/露黑。
        // 注意：不能 SetActive(false)——VideoPlayer 和 RawImage 在同一个物体上，
        // 关闭整个物体会连 VideoPlayer 一起禁用，导致 Prepare 报错。
        SetRawImageAlpha(0f);
    }

    /// <summary>
    /// 播放开场视频：准备完成后淡入播放，播完淡出并回调 onFinished。
    /// videoName 是 StreamingAssets/Videos/ 下的文件名（不含扩展名 .mp4）。
    /// </summary>
    public void PlayVideo(string videoName, System.Action onFinished)
    {
        this.onFinished = onFinished;

        videoPlayer.playOnAwake = false;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = System.IO.Path.Combine(Application.streamingAssetsPath, "Videos", videoName + ".mp4");
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = CreateRenderTexture();
        videoPlayer.isLooping = false;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct; // 视频自带音轨直接出声
        UpdateVideoVolume(); // 视频音轨音量跟随背景音乐设置
        rawImage.texture = videoPlayer.targetTexture;

        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.loopPointReached += OnFinished;
        videoPlayer.Prepare(); // 先准备，准备好再播，避免黑屏闪一下
    }

    private RenderTexture CreateRenderTexture()
    {
        // 视频按 16:9 渲染：信箱视口恒为 16:9，用固定 1920×1080
        // （按 Screen 尺寸建会在手机宽屏下把 16:9 视频拉成 20:9 再压回信箱，出现挤压变形）
        RenderTexture rt = new RenderTexture(1920, 1080, 0);
        rt.Create();
        return rt;
    }

    private void SetRawImageAlpha(float alpha)
    {
        Color c = rawImage.color;
        rawImage.color = new Color(c.r, c.g, c.b, alpha);
    }

    private void OnPrepared(VideoPlayer vp)
    {
        // 显示视频画面（恢复不透明），同时开始播放
        SetRawImageAlpha(1f);
        vp.Play();
        UIManager.Instance.DoShowOrHideUITeen(false, false, 2f, UIManager.Instance.mask.gameObject);
    }

    private void OnFinished(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnPrepared;
        vp.loopPointReached -= OnFinished;

        SetRawImageAlpha(0f);
        if (onFinished != null)
        {
            var cb = onFinished;
            onFinished = null;
            cb();
        }
    }

    /// <summary>跳过视频（可挂到"点击跳过"按钮上）</summary>
    public void Skip()
    {
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
            OnFinished(videoPlayer);
        }
    }
    /// <summary>
    /// 把背景音乐通道的音量同步到视频音轨（Direct 直通模式不走 AudioSource，只能单独设）。
    /// 背景音乐滑条变化时由 AudioSourceManager.SetChannelVolume 调用，播放中也会实时生效。
    /// </summary>
    public void UpdateVideoVolume()
    {
        if (videoPlayer != null)
        {
            videoPlayer.SetDirectAudioVolume(0, AudioSourceManager.GetChannelVolume(AudioChannel.Music));
        }
    }
}