#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# setup.sh — Prepara el entorno del servidor de emociones (DeepFace) en Linux.
#
# Lo invoca Unity (BootstrapManager) automáticamente en la primera ejecución.
# NO lanza el servidor: solo deja el venv listo. Unity lanza emotion_server.py
# después con la ruta del python del venv.
#
# Pasos: detectar Python 3.10–3.12 → crear venv → instalar requirements.txt.
# Sale con 0 si el entorno queda listo, distinto de 0 si algo falla.
# ─────────────────────────────────────────────────────────────────────────────
set -u

# Directorio de este script (= PythonServer/), funcione desde donde funcione.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV_DIR="$SCRIPT_DIR/venv"
REQ_FILE="$SCRIPT_DIR/requirements.txt"
VENV_PY="$VENV_DIR/bin/python"

echo "=================================================================="
echo " ARcade Rush — preparación del servidor de emociones (DeepFace)"
echo "=================================================================="

# Si el venv ya existe, no hay nada que hacer.
if [ -x "$VENV_PY" ]; then
    echo "[setup] El entorno ya existe en: $VENV_DIR"
    exit 0
fi

# ── Detectar un Python compatible (3.12 → 3.11 → 3.10 → python3) ─────────────
PYTHON=""
for cand in python3.12 python3.11 python3.10 python3; do
    if command -v "$cand" >/dev/null 2>&1; then
        PYTHON="$cand"
        break
    fi
done

if [ -z "$PYTHON" ]; then
    echo "[setup] ERROR: no se encontró Python 3.10–3.12."
    echo "[setup] Instálalo, por ejemplo:  sudo apt install python3.12 python3.12-venv"
    exit 1
fi

echo "[setup] Usando Python: $PYTHON ($($PYTHON --version 2>&1))"

# ── Crear el entorno virtual ─────────────────────────────────────────────────
echo "[setup] Creando entorno virtual en: $VENV_DIR"
if ! "$PYTHON" -m venv "$VENV_DIR"; then
    echo "[setup] ERROR: falló la creación del venv (¿falta el paquete python3-venv?)."
    exit 1
fi

# ── Instalar dependencias ────────────────────────────────────────────────────
echo "[setup] Instalando dependencias (puede tardar varios minutos la 1ª vez)..."
"$VENV_PY" -m pip install --upgrade pip
if ! "$VENV_PY" -m pip install -r "$REQ_FILE"; then
    echo "[setup] ERROR: falló la instalación de dependencias."
    exit 1
fi

echo "[setup] Entorno listo. Unity lanzará el servidor automáticamente."
exit 0
