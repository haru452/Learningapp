using System;
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

    public void Show(string videoId)
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (_webView == null)
        {
            _webView = new GameObject("YouTubeWebView").AddComponent<WebViewObject>();
            _webView.Init(
                cb: OnJsMessage,
                err: msg => Debug.LogWarning("[YouTubePlayerView] WebView error: " + msg));
        }

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
    private void OnJsMessage(string msg)
    {
        if (msg == "ended") OnEnded?.Invoke();
        else if (msg.StartsWith("error:")) Debug.LogWarning("[YouTubePlayerView] Player " + msg);
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
    private string BuildHtml(string videoId)
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
function onYouTubeIframeAPIReady() {
  new YT.Player('player', {
    width: '100%', height: '100%', videoId: '" + videoId + @"',
    playerVars: { playsinline: 1, rel: 0, modestbranding: 1, cc_load_policy: 0, origin: '" + origin + @"' },
    events: {
      onStateChange: function (e) { if (e.data === YT.PlayerState.ENDED) notify('ended'); },
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
