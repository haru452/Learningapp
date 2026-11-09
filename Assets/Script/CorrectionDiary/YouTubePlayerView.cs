using System;
using System.Globalization;
using UnityEngine;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using Gree.UnityWebView;
#endif

/// <summary>
/// YouTube の埋め込みプレーヤーを、videoArea (UI の RectTransform) の位置に重ねて表示する。
/// WebView には gree/unity-webview (WebViewObject) を使う。
///
/// - 動画が最後まで再生されると OnEnded が発火する (YouTube IFrame Player API で検知)
/// - WebView は Android / iOS 実機のみ。Editor などでは代わりにブラウザで開く (OnEnded は来ない)
/// - WebView は uGUI より手前に描画されるので、表示中は videoArea の上にボタン等を置かないこと
/// </summary>
public class YouTubePlayerView : MonoBehaviour
{
#pragma warning disable 0067, 0169, 0414   // Editor では WebView を使わないため
    [Tooltip("動画を表示する範囲 (空の Image などを置いておく)")]
    [SerializeField] private RectTransform videoArea;

    [Tooltip("埋め込み HTML のベース URL。YouTube はこれを埋め込み元 (origin) として扱う")]
    [SerializeField] private string embedBaseUrl = "https://localhost/";

    /// <summary>動画が最後まで再生された</summary>
    public event Action OnEnded;

    /// <summary>動画の再生が始まった (一時停止からの再開でも来る)</summary>
    public event Action OnPlaying;

    /// <summary>再生位置 (秒)。再生中は 1 秒ごとに来る</summary>
    public event Action<double> OnTime;

    /// <summary>プレーヤーのエラー (YouTube のエラー番号など)</summary>
    public event Action<string> OnError;
#pragma warning restore 0067, 0169, 0414

    /// <summary>アプリ内で再生できるか (false のときは Show でブラウザが開く)</summary>
    public static bool IsEmbeddedSupported =>
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        true;
#else
        false;
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    private WebViewObject _webView;
#endif

    /// <summary>動画を表示する範囲を変える (実行時に作った画面から使う)</summary>
    public void SetVideoArea(RectTransform area) => videoArea = area;

    // 今流している範囲 (Replay 用)。範囲指定の Show をまだ呼んでいなければ _clipId は null
    private string _clipId;
    private double _clipStart;
    private double _clipEnd;

    /// <summary>
    /// 範囲再生用に playerVars へ足す項目を作る。例: 12.4, 15.2 → "start:12,end:16"。
    /// start は切り捨て (負なら 0)、end は切り上げ。end が start 以下なら start のみ。
    /// </summary>
    public static string BuildPlayerVars(double start, double end)
    {
        long s = (long)Math.Floor(Math.Max(0, start));
        string vars = "start:" + s.ToString(CultureInfo.InvariantCulture);
        long e = (long)Math.Ceiling(end);
        if (end > start && e > s) vars += ",end:" + e.ToString(CultureInfo.InvariantCulture);
        return vars;
    }

    // YouTube の動画 ID は [A-Za-z0-9_-] だけ。JS / HTML に埋め込むので他は弾く
    private static bool IsValidVideoId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (char c in id)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
            if (!ok) return false;
        }
        return true;
    }

    private static void OpenClipInBrowser(string id, double start)
    {
        long s = (long)Math.Floor(Math.Max(0, start));
        Application.OpenURL("https://www.youtube.com/watch?v=" + id + "&t=" + s.ToString(CultureInfo.InvariantCulture) + "s");
    }

    /// <summary>
    /// 動画の startSeconds ～ endSeconds だけを流す (終わりで OnEnded)。実機のみアプリ内で自動再生、
    /// Editor などではその秒数からブラウザで開く。
    /// </summary>
    public void Show(string videoId, double startSeconds, double endSeconds)
    {
        if (!IsValidVideoId(videoId))
        {
            Debug.LogWarning("[YouTubePlayerView] 動画 ID が不正です: " + videoId);
            return;
        }
        _clipId = videoId;
        _clipStart = startSeconds;
        _clipEnd = endSeconds;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        EnsureWebView();
        UpdateMargins();
        _webView.LoadHTML(BuildHtml(videoId, "autoplay: 1, " + BuildPlayerVars(startSeconds, endSeconds) + ", "), embedBaseUrl);
        _webView.SetVisibility(true);
#else
        OpenClipInBrowser(videoId, startSeconds);
#endif
    }

    /// <summary>Show(id, start, end) で流した範囲をもう一度流す。まだ何も流していなければ何もしない</summary>
    public void Replay()
    {
        if (_clipId == null) return;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (_webView == null) return;
        // end で止まった後は seekTo より loadVideoById のほうが確実に再生し直せる
        string js = "if (player && player.loadVideoById) player.loadVideoById({videoId:'" + _clipId
            + "', startSeconds:" + Math.Floor(Math.Max(0, _clipStart)).ToString(CultureInfo.InvariantCulture)
            + (_clipEnd > _clipStart ? ", endSeconds:" + Math.Ceiling(_clipEnd).ToString(CultureInfo.InvariantCulture) : "")
            + "});";
        _webView.EvaluateJS(js);
#else
        OpenClipInBrowser(_clipId, _clipStart);
#endif
    }

    public void Show(string videoId)
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        EnsureWebView();

        UpdateMargins();
        _webView.LoadHTML(BuildHtml(videoId), embedBaseUrl);
        _webView.SetVisibility(true);
#else
        Application.OpenURL("https://www.youtube.com/watch?v=" + videoId);
#endif
    }

    public void Hide()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (_webView == null) return;
        // 音が鳴り続けないよう、空のページに切り替えてから隠す
        _webView.LoadHTML("<html><body style='background:#000'></body></html>", embedBaseUrl);
        _webView.SetVisibility(false);
#endif
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (_webView != null) Destroy(_webView.gameObject);
        _webView = null;
#endif
    }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    private void EnsureWebView()
    {
        if (_webView != null) return;
        _webView = new GameObject("YouTubeWebView").AddComponent<WebViewObject>();
        _webView.Init(
            cb: OnJsMessage,
            err: msg => Debug.LogWarning("[YouTubePlayerView] WebView error: " + msg));
    }

    private void OnJsMessage(string msg)
    {
        if (msg == "ended") OnEnded?.Invoke();
        else if (msg == "playing") OnPlaying?.Invoke();
        else if (msg.StartsWith("time:"))
        {
            if (double.TryParse(msg.Substring(5), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double t)) OnTime?.Invoke(t);
        }
        else if (msg.StartsWith("error:"))
        {
            Debug.LogWarning("[YouTubePlayerView] Player " + msg);
            OnError?.Invoke(msg);
        }
    }

    // videoArea の画面上の位置に合わせて WebView を置く
    private void UpdateMargins()
    {
        if (videoArea == null)
        {
            _webView.SetMargins(0, 0, 0, Screen.height / 2);
            return;
        }

        var canvas = videoArea.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        var corners = new Vector3[4];
        videoArea.GetWorldCorners(corners);   // 左下, 左上, 右上, 右下
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        _webView.SetMargins(
            Mathf.RoundToInt(min.x),
            Mathf.RoundToInt(Screen.height - max.y),
            Mathf.RoundToInt(Screen.width - max.x),
            Mathf.RoundToInt(min.y));
    }

    // YouTube IFrame Player API で再生し、終了を Unity に知らせる
    private string BuildHtml(string videoId, string extraVars = "")
    {
        string origin = embedBaseUrl.TrimEnd('/');
        return @"<!DOCTYPE html>
<html><head>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<meta name='referrer' content='strict-origin-when-cross-origin'>
<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#player{width:100%;height:100%}</style>
</head><body>
<div id='player'></div>
<script>
function notify(msg) {
  if (window.Unity && Unity.call) { Unity.call(msg); }
  else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.unityControl) {
    window.webkit.messageHandlers.unityControl.postMessage(msg);
  }
}
var player;
setInterval(function () {
  if (player && player.getCurrentTime && player.getPlayerState && player.getPlayerState() === YT.PlayerState.PLAYING)
    notify('time:' + player.getCurrentTime());
}, 1000);
function onYouTubeIframeAPIReady() {
  player = new YT.Player('player', {
    width: '100%', height: '100%', videoId: '" + videoId + @"',
    playerVars: { playsinline: 1, rel: 0, modestbranding: 1, cc_load_policy: 0, " + extraVars + @"origin: '" + origin + @"' },
    events: {
      onStateChange: function (e) {
        if (e.data === YT.PlayerState.ENDED) notify('ended');
        else if (e.data === YT.PlayerState.PLAYING) notify('playing');
      },
      onError: function (e) { notify('error:' + e.data); }
    }
  });
}
</script>
<script src='https://www.youtube.com/iframe_api'></script>
</body></html>";
    }
#endif
}
