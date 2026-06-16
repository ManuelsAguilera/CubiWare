"""
ARcade Rush — DeepFace Emotion Server
Receives JPEG frames from Unity via WebSocket, classifies emotions with DeepFace,
and returns results as JSON.

Protocol:
  Unity → Server : binary message = JPEG bytes (320×240, quality 50)
  Server → Unity : text message   = JSON emotion result

Usage:
  python emotion_server.py [--strict-detection] [--port 8765]
"""

import argparse
import json
import os
import sys
import threading
import time
from collections import deque

import cv2
import numpy as np

# ── Parse args ──────────────────────────────────────────────────────────────
parser = argparse.ArgumentParser()
parser.add_argument("--strict-detection", action="store_true",
                    help="Set face_detected=False when face is small or confidence is low")
parser.add_argument("--port", type=int, default=8765)
parser.add_argument("--parent-pid", type=int, default=None,
                    help="Unity process PID. Server exits automatically when Unity closes.")
args = parser.parse_args()

STRICT_DETECTION  = args.strict_detection
PORT              = args.port
PARENT_PID        = args.parent_pid
MIN_FACE_WIDTH    = 50     # pixels — used in strict mode
MIN_CONFIDENCE    = 0.30   # 0-1   — used in strict mode
HISTORY_SIZE      = 4      # rolling average window (overridable by whitelist config)
ANALYZE_INTERVAL  = 0.10   # seconds between DeepFace calls

# ── Load emotion whitelist config ──────────────────────────────────────────
EMOTION_WHITELIST_PATH = os.path.join(os.path.dirname(__file__), "emotion_whitelist.json")
_enabled_emotions = None
_disabled_emotions = None
_per_emotion_thresholds = {}
_default_threshold = 0.40

try:
    with open(EMOTION_WHITELIST_PATH, 'r') as f:
        whitelist_config = json.load(f)
    _enabled_emotions = whitelist_config.get("enabled_emotions", None)
    _disabled_emotions = whitelist_config.get("disabled_emotions", [])
    _per_emotion_thresholds = whitelist_config.get("per_emotion_confidence_thresholds", {})
    _default_threshold = whitelist_config.get("default_threshold", 0.40)
    config_window = whitelist_config.get("rolling_window_size", None)
    if config_window is not None:
        HISTORY_SIZE = int(config_window)
    print(f"[EmotionServer] Loaded whitelist config from emotion_whitelist.json")
    if _enabled_emotions:
        print(f"[EmotionServer]   ACTIVE emotions:   {_enabled_emotions}")
    if _disabled_emotions:
        print(f"[EmotionServer]   DISABLED emotions:  {_disabled_emotions}")
    print(f"[EmotionServer]   Thresholds:         {_per_emotion_thresholds}")
    print(f"[EmotionServer]   Default threshold:  {_default_threshold}")
    print(f"[EmotionServer]   Rolling window:     {HISTORY_SIZE}")
except FileNotFoundError:
    print("[EmotionServer] No emotion_whitelist.json found — using all 7 emotions with default threshold 0.40.")
except Exception as e:
    print(f"[EmotionServer] Error loading whitelist: {e} — using all 7 emotions.")



def _parent_process_alive(pid: int) -> bool:
    """Cross-platform existence check for the parent (Unity) process.

    On Windows, os.kill(pid, 0) does NOT check existence — it routes to
    TerminateProcess, which would try to kill the parent. So on Windows we
    open the process by handle and inspect it instead. On POSIX, signal 0 is
    a proper existence check.
    """
    if sys.platform == "win32":
        import ctypes
        from ctypes import wintypes

        # PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
        PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
        STILL_ACTIVE = 259

        kernel32 = ctypes.windll.kernel32
        handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not handle:
            return False  # process gone (or no access — treat as gone)
        try:
            exit_code = wintypes.DWORD()
            if kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
                return exit_code.value == STILL_ACTIVE
            return False
        finally:
            kernel32.CloseHandle(handle)
    else:
        try:
            os.kill(pid, 0)  # signal 0 = existence check only
            return True
        except OSError:
            return False


def _start_parent_watchdog(pid: int):
    """Background thread: exits the server if the Unity process disappears."""
    def _watch():
        while True:
            time.sleep(2)
            if not _parent_process_alive(pid):
                print(f"[EmotionServer] Unity process {pid} ended — shutting down.")
                os._exit(0)
    threading.Thread(target=_watch, daemon=True).start()


if PARENT_PID:
    _start_parent_watchdog(PARENT_PID)

# ── Verify DeepFace import ───────────────────────────────────────────────────
print("[EmotionServer] Loading DeepFace...")
try:
    from deepface import DeepFace
    print("[EmotionServer] DeepFace loaded OK")
except ImportError as e:
    print(f"[EmotionServer] ERROR: DeepFace not installed — {e}")
    print("[EmotionServer] Run: pip install -r requirements.txt")
    sys.exit(1)

from flask import Flask, jsonify
from flask_sock import Sock

app = Flask(__name__)
sock = Sock(app)

# ── Shared state (thread-safe via lock) ─────────────────────────────────────
_state_lock     = threading.Lock()
_latest_frame   = None   # bytes — most recent JPEG from Unity
_latest_result  = {      # most recent classification
    "dominant_emotion": "neutral",
    "confidence": 0.0,
    "face_detected": False,
    "scores": {k: 0.0 for k in ["angry","disgust","fear","happy","sad","surprise","neutral"]},
    "timestamp": time.time()
}
_emotion_history = deque(maxlen=HISTORY_SIZE)  # rolling average buffer


# ── DeepFace worker thread ───────────────────────────────────────────────────

def _analyze_loop():
    """Processes the latest frame at ANALYZE_INTERVAL seconds. Runs as daemon thread."""
    global _latest_result

    while True:
        time.sleep(ANALYZE_INTERVAL)

        with _state_lock:
            frame_bytes = _latest_frame

        if frame_bytes is None:
            continue

        try:
            # Decode JPEG bytes to numpy array
            np_arr  = np.frombuffer(frame_bytes, np.uint8)
            frame_bgr = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)
            if frame_bgr is None:
                continue

            result = DeepFace.analyze(
                img_path=frame_bgr,
                actions=["emotion"],
                enforce_detection=False,
                detector_backend="opencv",
                silent=True
            )

            # DeepFace can return a list when multiple faces are found
            if isinstance(result, list):
                result = result[0]

            raw_scores = result.get("emotion", {})

            # Normalize scores from 0-100 to 0-1
            norm_scores = {k: v / 100.0 for k, v in raw_scores.items()}

            # Rolling average
            _emotion_history.append(norm_scores)
            avg_scores = {}
            for emotion_name in norm_scores:
                avg_scores[emotion_name] = sum(
                    h.get(emotion_name, 0.0) for h in _emotion_history
                ) / len(_emotion_history)

            # ── Whitelist filter: zero out disabled emotions so they never become dominant ──
            if _enabled_emotions is not None:
                for emotion_name in list(avg_scores.keys()):
                    if emotion_name not in _enabled_emotions:
                        avg_scores[emotion_name] = 0.0

            dominant = max(avg_scores, key=avg_scores.get)
            confidence = avg_scores[dominant]

            # face_detected heuristic
            face_detected = True
            if STRICT_DETECTION:
                region = result.get("region", {})
                face_w = region.get("w", 0)
                if face_w < MIN_FACE_WIDTH or confidence < MIN_CONFIDENCE:
                    face_detected = False

            with _state_lock:
                _latest_result = {
                    "dominant_emotion": dominant,
                    "confidence": round(confidence, 4),
                    "face_detected": face_detected,
                    "scores": {k: round(v, 4) for k, v in avg_scores.items()},
                    "timestamp": time.time()
                }

        except Exception as e:
            # Don't crash the loop on analysis errors
            print(f"[EmotionServer] Analysis error: {e}")


_worker = threading.Thread(target=_analyze_loop, daemon=True)
_worker.start()


# ── WebSocket handler ────────────────────────────────────────────────────────

@sock.route("/ws")
def emotion_ws(ws):
    """
    Handles one Unity client connection.
    Receives JPEG frames (binary), responds with latest emotion JSON (text).
    Each message from Unity triggers one JSON response.
    """
    print(f"[EmotionServer] Client connected")
    global _latest_frame

    try:
        while True:
            data = ws.receive()

            if isinstance(data, bytes) and len(data) > 0:
                with _state_lock:
                    _latest_frame = data

            # Send back latest result regardless of message type
            with _state_lock:
                result_copy = dict(_latest_result)

            ws.send(json.dumps(result_copy))

    except Exception:
        # Client disconnected — don't propagate
        pass

    print(f"[EmotionServer] Client disconnected")


# ── HTTP endpoints ───────────────────────────────────────────────────────────

@app.route("/health")
def health():
    return jsonify({"ok": True})


@app.route("/status")
def status():
    with _state_lock:
        result = dict(_latest_result)
    return jsonify({
        "running": True,
        "face_detected": result["face_detected"],
        "current_emotion": result["dominant_emotion"],
        "confidence": result["confidence"],
        "strict_detection": STRICT_DETECTION
    })


# ── Entry point ──────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print(f"[EmotionServer] Starting server on ws://localhost:{PORT}/ws")
    print(f"[EmotionServer] HTTP endpoints: http://localhost:{PORT}/status | /health")
    print(f"[EmotionServer] Strict detection: {STRICT_DETECTION}")
    print(f"[EmotionServer] Parent PID watchdog: {PARENT_PID or 'disabled'}")
    print(f"[EmotionServer] Waiting for Unity to connect and send frames...")

    app.run(host="localhost", port=PORT, threaded=True)
