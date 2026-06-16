# ARcade Rush — Emotion Detection Server

Python server que recibe frames JPEG desde Unity vía WebSocket y clasifica emociones con DeepFace.

## Requisitos

- Python 3.10 a 3.12 (Recomendado: **Python 3.12**).
- pip

> [!WARNING]
> Versiones más recientes como **Python 3.14** no están soportadas actualmente debido a la falta de compatibilidad de TensorFlow en Windows.

## Arranque automático (recomendado)

**No necesitas ejecutar nada a mano.** Al entrar en Unity (Play Mode o build),
`BootstrapManager` prepara y lanza el servidor automáticamente:

- En la **primera ejecución** corre el script de setup en una ventana visible
  (`setup.ps1` en Windows, `setup.sh` en Linux): crea el `venv` e instala las
  dependencias. Tarda varios minutos y requiere **conexión a internet**
  (DeepFace además descarga sus modelos, ~500 MB).
- En Windows, si **no** hay Python 3.10–3.12, el script avisa qué va a instalar
  y por qué, y luego instala **Python 3.12** con `winget install Python.Python.3.12`.
- En ejecuciones posteriores detecta el `venv` y lanza el servidor directamente
  (oculto; su salida aparece en la consola de Unity).
- El servidor se apaga solo al cerrar Unity (watchdog por `--parent-pid`).

> **Requisito Windows:** tener `winget` disponible o, en su defecto, Python 3.12
> instalado manualmente. Versiones 3.13/3.14 **no** son compatibles (TensorFlow).

---

## Setup manual (fallback / desarrollo)

Solo necesario si quieres preparar el entorno tú mismo o el arranque automático falla.

1. **Crear el entorno virtual (Windows):**
   ```bash
   cd PythonServer
   py -3.12 -m venv venv
   ```

2. **Instalar dependencias:**
   ```bash
   venv\Scripts\pip.exe install -r requirements.txt
   ```

> **Primera ejecución:** DeepFace descarga los modelos automáticamente (~500 MB). Asegúrate de tener conexión a internet.

## Ejecutar manualmente

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
venv\Scripts\python.exe emotion_server.py --strict-detection
```

## Solución de Problemas (Troubleshooting)

### 1. El término 'venv/bin/python' no se reconoce
En Windows, las rutas del entorno virtual usan barras invertidas (`\`) y el directorio `Scripts` en lugar de `bin`:
* **Comando correcto en Windows:**
  ```powershell
  venv\Scripts\python.exe emotion_server.py
  ```

### 2. Error `ModuleNotFoundError: No module named 'cv2'`
Esto ocurre cuando las dependencias no están instaladas en el entorno virtual activo.
* **Solución:** Ejecuta la instalación usando la ruta absoluta al pip del entorno virtual:
  ```powershell
  venv\Scripts\pip.exe install -r requirements.txt
  ```

### 3. Error al compilar `numpy` (`metadata-generation-failed` / `Unknown compiler`)
Este error ocurre al usar una versión de Python demasiado nueva (como **Python 3.14+**). Debido a que TensorFlow carece de paquetes pre-compilados (wheels) para estas versiones en Windows, `pip` intenta compilar dependencias desde el código fuente (.tar.gz), lo cual falla si no tienes herramientas de compilación C++ instaladas.
* **Solución:**
  1. Instala **Python 3.12** utilizando Winget:
     ```powershell
     winget install Python.Python.3.12
     ```
  2. Elimina el entorno virtual actual y créalo de nuevo forzando el uso de Python 3.12:
     ```powershell
     Remove-Item -Recurse -Force venv
     py -3.12 -m venv venv
     venv\Scripts\pip.exe install -r requirements.txt
     ```
