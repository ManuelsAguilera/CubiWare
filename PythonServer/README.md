# ARcade Rush — Emotion Detection Server

Python server que recibe frames JPEG desde Unity vía WebSocket y clasifica emociones con DeepFace.

## Requisitos

- Python 3.10 o superior
- pip

## Setup

```bash
cd PythonServer
pip install -r requirements.txt
```

> **Primera ejecución:** DeepFace descarga los modelos automáticamente (~500 MB). Asegúrate de tener conexión a internet.

## Ejecutar

```bash
python emotion_server.py
```

Salida esperada:
```
[EmotionServer] DeepFace loaded OK
[EmotionServer] Starting server on ws://localhost:8765/ws
[EmotionServer] HTTP endpoints: http://localhost:8765/status | /health
```

## Usar con Unity

1. Ejecutar el servidor (`python emotion_server.py`)
2. Abrir Unity y entrar en Play Mode
3. Navegar a la escena EmotionTest (botón Test1 en el menú)
4. El cliente se conecta automáticamente

## Endpoints HTTP

| Endpoint | Descripción |
|---|---|
| `GET /health` | Verificar que el servidor está corriendo |
| `GET /status` | Estado actual: emoción detectada, FPS, cara detectada |

## Protocolo WebSocket `ws://localhost:8765/ws`

- **Unity → Server:** bytes binarios = frame JPEG (320×240, quality 50)
- **Server → Unity:** JSON con clasificación (ver formato abajo)

```json
{
  "dominant_emotion": "happy",
  "confidence": 0.87,
  "face_detected": true,
  "scores": {
    "angry": 0.02, "disgust": 0.01, "fear": 0.03,
    "happy": 0.87, "sad": 0.01, "surprise": 0.04, "neutral": 0.02
  },
  "timestamp": 1718000000.123
}
```

## Flag --strict-detection

Por defecto, `face_detected` es `true` siempre que DeepFace procese el frame.
Con la flag `--strict-detection`, se requiere que el rostro detectado tenga:
- Ancho mínimo de 50px en la imagen original
- Confianza > 30% en la emoción dominante

```bash
python emotion_server.py --strict-detection
```
