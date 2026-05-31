# ARcade Rush: CubiWare

Juego de minijuegos con detección de gestos y emociones usando MediaPipe y DeepFace.

---

## Cómo ejecutar el juego

### Prerequisitos

| Componente | Linux | Windows |
|---|---|---|
| **Unity 6** | ✅ requerido | ✅ requerido |
| **Python 3.10+** | ✅ usualmente ya instalado | [Descargar python.org](https://www.python.org/downloads/) |
| Webcam | ✅ | ✅ |

---

### Linux

1. Clonar el repositorio
2. Abrir **Unity Hub** → abrir carpeta `CubiWare/`
3. Abrir la escena `Assets/Scenes/Bootstrap.unity`
4. Presionar **Play ▶**

Unity configura automáticamente el entorno Python y lanza el servidor de detección de emociones.

> **Primera ejecución:** Unity descargará las dependencias Python y los modelos de IA (~500 MB). Puede tardar **3–7 minutos**. Las siguientes veces tarda menos de 5 segundos.

---

### Windows

1. Instalar **Python 3.10+** desde [python.org](https://www.python.org/downloads/)
2. Clonar el repositorio
3. Abrir **Unity Hub** → abrir carpeta `CubiWare/`
4. Abrir la escena `Assets/Scenes/Bootstrap.unity`
5. Presionar **Play ▶**

> **Primera ejecución:** mismo proceso que en Linux (~3–7 min). Las siguientes veces es instantáneo.

---

### Verificar que todo funciona

Al presionar Play deberías ver en la **consola de Unity**:

```
[BootstrapManager] Python server launched (PID XXXX)
[EmotionWebSocketClient] Connected to ws://localhost:8765/ws
```

Si ves `[EmotionWebSocketClient] Connection failed` repetidamente:
- Verificar que Python 3.10+ esté instalado y en PATH (`python3 --version` en terminal)
- Verificar que el puerto 8765 no esté bloqueado por firewall

---

### Detener el juego

Al presionar **Stop ■** en Unity, el servidor Python se cierra automáticamente.

---

## Estructura del proyecto

```
CubiWare/
├── Assets/
│   ├── Scenes/          # Bootstrap, MainMenu, EmotionTest, Director, Simon, Shooter...
│   └── Scripts/
│       ├── Core/         # GameManager, BootstrapManager, SceneLoader
│       ├── EmotionDetection/  # EmotionGameBridge, EmotionWebSocketClient
│       ├── Minigames/    # Director, Simon, Shooter, EmotionTest
│       └── Hand/         # Gesture detection (MediaPipe)
├── PythonServer/
│   ├── emotion_server.py  # Servidor DeepFace (lanzado automáticamente por Unity)
│   ├── requirements.txt
│   └── README.md          # Documentación del servidor
└── README.md              # Este archivo
```

---

## Para el equipo de desarrollo

El servidor Python (`PythonServer/emotion_server.py`) se lanza automáticamente al presionar Play. También puedes lanzarlo manualmente para debugging:

```bash
cd PythonServer
venv/bin/python emotion_server.py        # Linux/macOS
venv\Scripts\python emotion_server.py   # Windows
```

El endpoint `http://localhost:8765/status` muestra el estado actual de la detección en tiempo real.
