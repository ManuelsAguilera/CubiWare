# ─────────────────────────────────────────────────────────────────────────────
# setup.ps1 — Prepara el entorno del servidor de emociones (DeepFace) en Windows.
#
# Lo invoca Unity (BootstrapManager) automáticamente en la primera ejecución,
# en una ventana visible para que se vea el progreso de la instalación.
# NO lanza el servidor: solo deja el venv listo. Unity lanza emotion_server.py
# después con la ruta del python del venv.
#
# Pasos: detectar Python 3.10–3.12 (instalarlo con winget si falta, avisando
# antes) → crear venv → instalar requirements.txt.
# Sale con 0 si el entorno queda listo, 1 si algo falla.
#
# Ejecutar (lo hace Unity): powershell -ExecutionPolicy Bypass -NoProfile -File setup.ps1
# ─────────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
$VenvDir   = Join-Path $ScriptDir "venv"
$ReqFile   = Join-Path $ScriptDir "requirements.txt"
$VenvPy    = Join-Path $VenvDir "Scripts\python.exe"

Write-Host "=================================================================="
Write-Host " ARcade Rush - preparacion del servidor de emociones (DeepFace)"
Write-Host "=================================================================="

# Si el venv ya existe, no hay nada que hacer.
if (Test-Path $VenvPy) {
    Write-Host "[setup] El entorno ya existe en: $VenvDir"
    exit 0
}

# ── Detectar un Python compatible vía el launcher 'py' (3.12 -> 3.11 -> 3.10) ─
function Find-CompatiblePython {
    foreach ($ver in @("3.12", "3.11", "3.10")) {
        try {
            $out = & py "-$ver" --version 2>$null
            if ($LASTEXITCODE -eq 0) { return $ver }
        } catch { }
    }
    return $null
}

$pyVer = Find-CompatiblePython

# ── Si no hay Python compatible: avisar, explicar e instalar con winget ──────
if ($null -eq $pyVer) {
    Write-Host ""
    Write-Host "------------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host " No se encontro Python 3.10-3.12 en este equipo." -ForegroundColor Yellow
    Write-Host ""
    Write-Host " Se instalara *Python 3.12* porque la deteccion de emociones del"  -ForegroundColor Yellow
    Write-Host " juego usa TensorFlow + DeepFace, que SOLO tienen paquetes"        -ForegroundColor Yellow
    Write-Host " precompilados para Python 3.10-3.12 en Windows. Con versiones"     -ForegroundColor Yellow
    Write-Host " mas nuevas (3.13/3.14) la instalacion falla."                      -ForegroundColor Yellow
    Write-Host ""
    Write-Host " Se usara winget:  winget install -e --id Python.Python.3.12"      -ForegroundColor Yellow
    Write-Host "------------------------------------------------------------------" -ForegroundColor Yellow

    # Pausa breve para que el mensaje sea legible antes de continuar.
    for ($i = 5; $i -ge 1; $i--) {
        Write-Host "`r Continuando en $i s... (Ctrl+C para cancelar)   " -NoNewline
        Start-Sleep -Seconds 1
    }
    Write-Host ""

    # Comprobar que winget existe.
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Host "[setup] ERROR: winget no esta disponible." -ForegroundColor Red
        Write-Host "[setup] Instala Python 3.12 manualmente desde https://www.python.org/downloads/" -ForegroundColor Red
        Write-Host "[setup] y vuelve a iniciar el juego." -ForegroundColor Red
        exit 1
    }

    Write-Host "[setup] Instalando Python 3.12 con winget..."
    winget install -e --id Python.Python.3.12 --accept-package-agreements --accept-source-agreements
    # No abortamos por el exit code de winget (devuelve codigos no-cero benignos,
    # p.ej. si ya estaba instalado). Re-detectamos para confirmar.

    # Refrescar PATH del proceso actual para que 'py' aparezca tras instalar.
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")

    $pyVer = Find-CompatiblePython
    if ($null -eq $pyVer) {
        Write-Host "[setup] ERROR: Python 3.12 no quedo disponible tras la instalacion." -ForegroundColor Red
        Write-Host "[setup] Cierra y vuelve a abrir el juego; si persiste, reinicia el equipo." -ForegroundColor Red
        exit 1
    }
}

Write-Host "[setup] Usando Python: py -$pyVer ($(& py "-$pyVer" --version 2>&1))"

# ── Crear el entorno virtual ─────────────────────────────────────────────────
Write-Host "[setup] Creando entorno virtual en: $VenvDir"
& py "-$pyVer" -m venv "$VenvDir"
if (-not (Test-Path $VenvPy)) {
    Write-Host "[setup] ERROR: fallo la creacion del venv." -ForegroundColor Red
    exit 1
}

# ── Instalar dependencias ────────────────────────────────────────────────────
Write-Host "[setup] Instalando dependencias (puede tardar varios minutos la 1a vez)..."
& "$VenvPy" -m pip install --upgrade pip
& "$VenvPy" -m pip install -r "$ReqFile"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[setup] ERROR: fallo la instalacion de dependencias." -ForegroundColor Red
    exit 1
}

Write-Host "[setup] Entorno listo. Unity lanzara el servidor automaticamente."
exit 0
