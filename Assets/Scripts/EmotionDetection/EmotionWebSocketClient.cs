using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ARcadeRush.Core;

namespace ARcadeRush.EmotionDetection
{
    /// <summary>
    /// Low-level WebSocket client for the DeepFace emotion server.
    ///
    /// Responsibilities (this class only):
    ///   1. Maintain a WebSocket connection to ws://localhost:8765/ws
    ///   2. Capture Unity camera frames, scale to 320×240, encode as JPEG,
    ///      and send them to the server as binary messages.
    ///   3. Receive JSON emotion results and expose them via OnEmotionReceived.
    ///   4. Reconnect automatically when the connection drops.
    ///
    /// No game logic here. EmotionGameBridge consumes this client.
    /// Uses System.Net.WebSockets.ClientWebSocket (built into .NET Standard 2.1,
    /// no external packages required).
    /// </summary>
    public class EmotionWebSocketClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string _serverUrl = "ws://localhost:8765/ws";
        [SerializeField] private float  _reconnectDelay = 3f;

        [Header("Frame Sending")]
        [Tooltip("Seconds between frame sends. 0.1 = 10 frames/second.")]
        [SerializeField] private float _sendInterval = 0.1f;
        [Tooltip("Width of the scaled frame sent to the server.")]
        [SerializeField] private int _frameWidth = 320;
        [Tooltip("Height of the scaled frame sent to the server.")]
        [SerializeField] private int _frameHeight = 240;
        [Tooltip("JPEG quality (1-100). Lower = smaller payload, faster transfer.")]
        [SerializeField][Range(10, 100)] private int _jpegQuality = 50;

        // ── Public API ────────────────────────────────────────────────────────
        public bool        IsConnected { get; private set; }
        public EmotionData LatestData  { get; private set; }

        public event Action<EmotionData> OnEmotionReceived;

        // ── Private ───────────────────────────────────────────────────────────
        private ClientWebSocket          _ws;
        private CancellationTokenSource  _cts;
        private readonly ConcurrentQueue<EmotionData> _incomingQueue = new();

        // Frame capture (main thread)
        private RenderTexture _rt;
        private Texture2D     _scaledTex;
        private float         _sendTimer;

        // Pending frame for background send (shared between main thread and task)
        private byte[]        _pendingFrameBytes;
        private readonly object _frameLock = new();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Start()
        {
            _cts = new CancellationTokenSource();
            _ = ConnectLoop(_cts.Token);
        }

        private void Update()
        {
            // Dispatch incoming results on the main thread
            while (_incomingQueue.TryDequeue(out var data))
            {
                LatestData = data;
                OnEmotionReceived?.Invoke(data);
            }

            // Capture and enqueue frame for sending
            if (!IsConnected) return;
            _sendTimer += Time.deltaTime;
            if (_sendTimer < _sendInterval) return;
            _sendTimer = 0f;

            var frameBytes = CaptureFrame();
            if (frameBytes != null)
                lock (_frameLock) { _pendingFrameBytes = frameBytes; }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _ws?.Dispose();
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_scaledTex != null) Destroy(_scaledTex);
        }

        // ── Connection loop (background task) ────────────────────────────────

        private async Task ConnectLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                try
                {
                    await _ws.ConnectAsync(new Uri(_serverUrl), ct);
                    IsConnected = true;
                    Debug.Log($"[EmotionWebSocketClient] Connected to {_serverUrl}");

                    var receiveTask = ReceiveLoop(ct);
                    var sendTask    = SendLoop(ct);

                    // If either loop ends (disconnect), restart
                    await Task.WhenAny(receiveTask, sendTask);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EmotionWebSocketClient] Connection failed: {e.Message}");
                }

                IsConnected = false;

                if (!ct.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(_reconnectDelay), ct).ContinueWith(_ => { });
            }
        }

        private async Task SendLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                byte[] toSend = null;
                lock (_frameLock) { toSend = _pendingFrameBytes; _pendingFrameBytes = null; }

                if (toSend != null)
                {
                    try
                    {
                        await _ws.SendAsync(new ArraySegment<byte>(toSend),
                            WebSocketMessageType.Binary, endOfMessage: true, ct);
                    }
                    catch { return; }
                }

                await Task.Delay(10, ct).ContinueWith(_ => { });
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[16384];

            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var data = JsonUtility.FromJson<EmotionData>(json);
                    _incomingQueue.Enqueue(data);
                }
                catch (OperationCanceledException) { return; }
                catch { return; }
            }
        }

        // ── Frame capture (main thread) ───────────────────────────────────────

        private byte[] CaptureFrame()
        {
            var camTex = CameraFeedCtrl.Instance?.ActiveWebCamTexture;
            if (camTex == null || !camTex.isPlaying) return null;

            // Lazy-init persistent render and texture objects (avoids per-frame alloc)
            if (_rt == null || _rt.width != _frameWidth || _rt.height != _frameHeight)
            {
                if (_rt != null) { _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(_frameWidth, _frameHeight, 0);
            }
            if (_scaledTex == null || _scaledTex.width != _frameWidth)
            {
                if (_scaledTex != null) Destroy(_scaledTex);
                _scaledTex = new Texture2D(_frameWidth, _frameHeight, TextureFormat.RGB24, false);
            }

            var prev = RenderTexture.active;
            Graphics.Blit(camTex, _rt);
            RenderTexture.active = _rt;
            _scaledTex.ReadPixels(new Rect(0, 0, _frameWidth, _frameHeight), 0, 0);
            _scaledTex.Apply();
            RenderTexture.active = prev;

            return _scaledTex.EncodeToJPG(_jpegQuality);
        }
    }
}
