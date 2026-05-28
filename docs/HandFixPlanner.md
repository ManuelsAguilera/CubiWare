# HandFixPlanner: Lógica de Proyección de Mano en el Minijuego Shooter

**Autor**: Lead Technical Writer
**Propósito**: Este documento proporciona un desglose arquitectónico y matemático exhaustivo del subsistema de proyección de manos dentro del minijuego Shooter. Está diseñado para servir como una referencia técnica que asista a los ingenieros en el diagnóstico y resolución independiente de imprecisiones en la proyección.

---

## Índice
1. [Glosario de Términos Técnicos](#glosario-de-términos-técnicos)
2. [Archivos Principales del Subsistema](#1-archivos-principales-del-subsistema)
3. [Técnicas de Programación Aplicadas](#2-técnicas-de-programación-aplicadas)
4. [Objetos y Componentes del Motor Unity](#3-objetos-y-componentes-del-motor-unity)
5. [Lógica Matemática de Proyección y Flujo de Datos](#4-lógica-matemática-de-proyección-y-flujo-de-datos)

---

## Glosario de Términos Técnicos

- **Landmarks**: Puntos de referencia o coordenadas tridimensionales generadas por el modelo de Machine Learning que identifican partes específicas de la mano (ej. la yema del dedo índice, la base de la palma).
- **Datos de seguimiento (Tracking Data)**: Conjunto de información continua proporcionada por el sensor y el modelo (MediaPipe) sobre la posición y postura de la mano en tiempo real.
- **Hitscan**: Técnica de videojuegos donde el impacto se calcula trazando un rayo instantáneo (cálculo balístico sin simulación de tiempo o caída) en línea recta desde un punto de origen hacia la dirección en la que se apunta.
- **Z-Depth (Profundidad)**: La distancia en el eje Z respecto al punto de vista de la cámara. Define qué tan lejos aparece un objeto proyectado en el mundo virtual 3D.
- **Normalización (Normalized Vectors)**: Proceso matemático que escala valores posicionales a un rango estándar (de 0.0 a 1.0) para abstraerlos de la resolución específica de la pantalla.
- **Orquestador (Orchestrator)**: Un componente o script central cuyo propósito principal es coordinar, integrar y dirigir el flujo de ejecución entre otros componentes más especializados.
- **Jitter**: Fluctuaciones, vibraciones o ruido en los datos de entrada (generalmente debido a imperfecciones en la detección óptica del sensor) que hacen que el objeto rastreado tiemble en pantalla.
- **Paralaje (Parallax)**: Desplazamiento aparente de un objeto cuando se observa desde dos líneas de visión diferentes. En este contexto, la diferencia visual entre la posición de la cámara y la posición del cañón del arma.

---

## 1. Archivos Principales del Subsistema

La lógica de proyección y apuntado de la mano está distribuida a través de los siguientes componentes primarios. Comprender la interacción entre estos archivos es crítico para la depuración.

- **`Assets/Scripts/Minigames/Shooter/ShooterHandController.cs`**
  *Responsabilidad*: Actúa como el orquestador principal de entrada para el minijuego. Traduce la posición cruda de la punta del dedo índice en un rayo de apuntado (`AimOrigin` y `AimDirection`) y delega las acciones de disparo a las mecánicas del arma.
- **`Assets/Scripts/Hand/Hand3DProjector.cs`**
  *Responsabilidad*: Gestiona la proyección global en 3D de los 21 puntos (landmarks) de la mano. Incluye una calibración de profundidad dinámica (`GetCalibratedDepth`) basada en la escala física estimada de la mano del usuario.
- **`Assets/Scripts/Hand/HandModel.cs`**
  *Responsabilidad*: Controla la representación visual de la mano en el espacio 3D. Mapea los datos de seguimiento 2D a articulaciones 3D y renderizadores de líneas para los huesos.
- **`Assets/Scripts/Hand/HandPositionTracker.cs`**
  *Responsabilidad*: Calcula un punto central suavizado y promediado de la mano y maneja las preferencias globales del usuario, como el modo espejo de la pantalla (`IsMirrored`).

---

## 2. Técnicas de Programación Aplicadas

El subsistema emplea varios patrones de diseño estándar de la industria para mantener un bajo acoplamiento y una gestión de estado predecible:

- **Inversión de Dependencias (IoC)**: Componentes como `ShooterHandController` y `HandModel` dependen de la interfaz `IHandDetector` en lugar de acoplarse directamente al servicio de visión de ML subyacente (`MediaPipeController`). Esto facilita las pruebas y la modularidad.
- **Patrón Observador (Arquitectura Orientada a Eventos)**: El sistema utiliza eventos asíncronos de C# (`OnHandDetected`, `OnHandLost`, `OnClosedFist`) para propagar los cambios de estado. Esto evita costosos ciclos de sondeo (polling) en el método `Update()`.
- **Suavizado de Señal (Media Móvil)**: Para mitigar el "jitter" del sensor inherente a la visión por computadora, `HandPositionTracker` utiliza una `Queue<Vector2>` como un búfer circular para calcular un promedio móvil de las coordenadas de la mano a lo largo de múltiples fotogramas.

---

## 3. Objetos y Componentes del Motor Unity

La implementación depende en gran medida de APIs específicas de Unity para la transformación espacial y las físicas:

- **`Camera.main`**: El objeto fundamental para las transformaciones del espacio de coordenadas.
  - `ScreenToWorldPoint()`: Convierte coordenadas de píxeles 2D con una profundidad Z definida en espacio mundial 3D.
  - `ScreenPointToRay()`: Genera un rayo direccional desde el plano de recorte cercano de la cámara a través de una coordenada de pantalla específica.
- **Motor de Físicas (`Ray` y `RaycastHit`)**: Utilizado para la detección de impactos (hitscan). El rayo de apuntado intersecta con los colisionadores que residen en el `_targetLayer`.
- **Renderizadores (`LineRenderer`, `Material`)**: Usados específicamente dentro de `HandModel` y `GunController` para visualizar conexiones anatómicas (huesos) y trayectorias balísticas (estelas de balas).

---

## 4. Lógica Matemática de Proyección y Flujo de Datos

La complejidad central radica en traducir coordenadas normalizadas 2D de machine learning en espacio 3D de juego procesable. El flujo de datos matemático se ejecuta de la siguiente manera:

### Fase A: Normalización al Espacio de Pantalla
El modelo de ML proporciona los puntos de la mano como vectores normalizados `[0.0, 1.0]`. El código transforma estos en coordenadas de píxeles (`X_pixel`, `Y_pixel`):
```csharp
Vector3 screenPos = new Vector3(
    (1f - tip.x) * Screen.width,
    (1f - tip.y) * Screen.height,
    Z_Depth
);
```
- **Inversión del Eje Y (`1f - tip.y`)**: Esencial porque el modelo de ML designa `(0,0)` como la esquina superior izquierda, mientras que el espacio de pantalla de Unity designa `(0,0)` como la esquina inferior izquierda.
- **Transformación del Eje X (`1f - tip.x`)**: Aplicado explícitamente en varios scripts para invertir (espejar) el eje horizontal.

### Fase B: Asignación de Profundidad (Z-Depth)
La asignación del eje `Z` (profundidad desde la cámara) varía significativamente dependiendo del objetivo del componente:
- **Profundidad Dinámica (`Hand3DProjector`)**: Calcula la profundidad de forma contextual midiendo la distancia en píxeles entre la base de la palma y el dedo medio, estimando la proximidad física.
- **Profundidad Estática (`HandModel` y `ShooterHandController`)**: Fuerza un valor de profundidad codificado estáticamente (`Z = 10f` o `Z = 0f` para el origen inicial del rayo) para estabilizar la visualización y los orígenes de emisión de rayos.

### Fase C: Emisión de Rayos Vectoriales (Apuntado)
1. El `ShooterHandController` aísla el dedo índice (Punto 8).
2. Construye un rayo que se origina desde la lente de la cámara utilizando `ScreenPointToRay()`.
3. El punto de intersección de este rayo y el entorno dicta el `_aimTargetPoint`.
4. El `GunController` calcula dinámicamente su rotación local para alinear su vector frontal (`forward`) con este `_aimTargetPoint` a través del método `LookAt()`.

---

## 5. Detalle Técnico de Miembros (Atributos y Métodos)

### A. `ShooterHandController.cs` (Orquestador de Input)
*   **Atributos Clave:**
    *   `_gunController` (GunController): Referencia al actuador del arma.
    *   `_aimTargetPoint` (Vector3): Punto final en el mundo 3D donde impacta la mirada.
    *   `AimDirection` / `AimOrigin` (Vector3): Vectores que definen la geometría del rayo de apuntado.
    *   `IsAiming` (bool): Indica si el sistema ha validado la presencia de una mano para apuntar.
*   **Métodos Clave:**
    *   `UpdateAimRay()`: **Proceso Crítico**. Convierte el Landmark 8 (índice) de espacio normalizado a coordenadas de pantalla y dispara un `Raycast` para hallar el `_aimTargetPoint`.
    *   `HandleHandDetected()`: Suscribe los datos de entrada y activa la bandera de detección.

### B. `Hand3DProjector.cs` (Cálculo de Profundidad)
*   **Atributos Clave:**
    *   `_nearScale` / `_farScale` (float): Valores de referencia de tamaño de mano en píxeles.
    *   `_nearZ` / `_farZ` (float): Unidades de profundidad correspondientes en el espacio de Unity.
    *   `LandmarkWorldPositions` (Vector3[]): Almacén de los 21 puntos ya proyectados en el mundo.
*   **Métodos Clave:**
    *   `GetCalibratedDepth(float handScale)`: Implementa la lógica de interpolación lineal para adivinar qué tan lejos está la mano del usuario.
    *   `HandleHandDetected()`: Calcula el tamaño relativo de la mano (palma + dedo medio) antes de proyectar.

### C. `HandModel.cs` (Visualización)
*   **Atributos Clave:**
    *   `_joints` (Transform[]): Referencias a las esferas físicas de las articulaciones.
    *   `_bones` (LineRenderer[]): Referencias a los componentes que dibujan los huesos.
    *   `_smoothSpeed` (float): Factor de interpolación para evitar el parpadeo visual.
*   **Métodos Clave:**
    *   `Update()`: Mapea los puntos de pantalla a mundo usando una profundidad constante (`10f`) para asegurar visibilidad constante.

### D. `HandPositionTracker.cs` (Filtro de Datos)
*   **Atributos Clave:**
    *   `IsMirrored` (bool): Estado del eje X (invertido o normal).
    *   `_positionBuffer` (Queue<Vector2>): Almacén temporal para el cálculo de la media móvil.
*   **Métodos Clave:**
    *   `HandleHandDetected()`: Realiza el promedio aritmético de 5 puntos de la palma para estabilizar el centro de la mano.

### E. `GunController.cs` (Actuador)
*   **Atributos Clave:**
    *   `_muzzleTransform` (Transform): El origen físico de la bala (punta del cañón).
    *   `_hitLayerMask` (LayerMask): Define qué objetos son considerados "objetivos".
*   **Métodos Clave:**
    *   `LookAt(Vector3 target)`: Orienta el modelo 3D del arma hacia el punto de impacto calculado por el controlador de mano.
    *   `PerformHitscan()`: Realiza el disparo físico final desde el cañón hacia adelante.

---

## 6. Diagrama de Flujo de Datos (Secuencia de Actividades)

A continuación se describe el viaje de un dato desde el sensor hasta el impacto en el juego:

```text
[SENSOR WEBCAM]
      |
      V
[MEDIAPIPE CONTROLLER] 
(Genera 21 Landmarks Normalizados 0.0 - 1.0)
      |
      +----------------------------+---------------------------+
      |                            |                           |
      V                            V                           V
[HAND POSITION TRACKER]      [HAND 3D PROJECTOR]         [HAND MODEL]
1. Promedia Palma            1. Calcula Escala           1. Z Fijo (10f)
2. Aplica Espejo (X)         2. Interpola Profundidad    2. Suaviza (Lerp)
3. Suaviza (Buffer)          3. Proyecta 21 Puntos       3. Dibuja Esferas/Lineas
      |                            |                           |
      |                            V                           |
      |                 (Dato: LandmarkWorldPositions)         |
      |                            |                           |
      +----------------------------+---------------------------+
                                   |
                                   V
                      [SHOOTER HAND CONTROLLER]
                      1. Toma Landmark 8 (Punto Índice)
                      2. ScreenPointToRay(Z=10f)
                      3. Detecta Impacto Visual (Raycast Cámara)
                      4. Define: _aimTargetPoint
                                   |
                                   V
                            [GUN CONTROLLER]
                            1. LookAt(_aimTargetPoint)
                            2. Gesto Fist -> Shoot()
                            3. Raycast Físico (Desde el CAÑÓN)
                            4. Resultado: TARGET_HIT / MISS
```

---
*Fin del Documento. Consulte esta arquitectura al investigar discrepancias en la proyección.*