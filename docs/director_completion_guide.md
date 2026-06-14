# Director de Escena — Guía de Finalización (UI + LLM Audiencia)

Este documento organiza el trabajo restante para "completar" el minijuego
Director de Escena en dos frentes:

1. **UI/visuales del teatro**: telón, audiencia animada, fondo de escenario y
   guion como "hoja".
2. **LLM**: la audiencia comenta en vivo (aprobación/desaprobación) mientras
   sube o baja la barra de aprobación.

Está pensado para convertirse 1:1 en issues de GitHub (ver sección 4 — el
contenido de cada issue está listo para copiar/pegar).

## ESTADO: los 5 issues fueron IMPLEMENTADOS (2026-06-12)

| Issue | Commit | Resultado |
|---|---|---|
| 1 — Telón + fondo | `eac62ab`, `eb5beb6` | Curtain.controller con 4 estados/clips, eventos verificados en Play Mode; ScenarioBackground.jpg asignado; telón sale 100% de pantalla (y=1550) |
| 2 — Sprites + aleatorización | `098f8d8` | `_emotionSprites[0-3]` asignados; secuencia aleatoria sin repetidos consecutivos |
| 3 — Audiencia fila de íconos | `6866a67` | 7 avatares procedurales tintados bajo Canvas; AudienceController reescrito con coroutines (aplauso escalonado/abucheo), API pública intacta |
| 4 — Guion como hoja | `eb5beb6` | ScriptPaper (papel rayado procedural, -1.6°) detrás de los textos del guion; EmotionIcon sobre la hoja |
| 5 — Diálogos LLM | `15298b9` | AudienceDialogueProvider (1 llamada Groq/elemento, fallback), diálogos en pass/fail y cruces 75%/25% de barra con cooldown 3s |

Pendientes menores: sprites de Disgust/Fear/Sad (índices 4-6 del array, falta
arte); probar el flujo completo desde MainMenu (con GameManager y LLM reales);
crear los issues en GitHub si aún se quiere trazabilidad (requiere `gh auth login`).

---

## 1. Estado actual de los subsistemas

| Subsistema | Script | Estado actual |
|---|---|---|
| Telón | `ScenarioController.cs` | Animator con triggers `Open`/`Close` y eventos `OnOpenComplete_AnimEvent`/`OnCloseComplete_AnimEvent` ya conectados desde `DirectorGame`. Objeto world-space `ScenarioRoot` en `(470.35, 349.14, 12.74)`. `Curtain.png` existe pero no se confirmó que esté asignado/visible en cámara. |
| Audiencia | `AudienceController.cs` | Un solo objeto con Animator (`Idle`/`SlightMove`/`React`, bool `IsPositive`). `ShowDialogue(text, duration)` ya implementado y pensado para LLM. `AudienceRoot` en `(1100.17, -0.00001, 10238.36)` — posición sospechosa. No hay múltiples miembros/íconos. |
| Máscara AR | `MaskController.cs` | Swap de 4 mallas por emoción (Neutral/Happy/Surprised/Angry). Posicionamiento por face-tracking deshabilitado actualmente. |
| Guion | `ScriptController.cs` | Maneja la secuencia (`_sequenceLength`, `GenerateLocalSequence`), textos `EmotionText`/`NextEmotionText`/`ProgressText`, `_emotionSprites[]` (vacío), `_introText`/`ShowIntro`/`HideIntro`. Sin contenedor visual tipo "hoja". |
| Barra de aprobación | `ApprovalBarController.cs` | Fill/drain con periodo de gracia (6s), logging por fases (📖/🎬/🎭/✅/❌). Funcional. |
| Cuenta regresiva | `CountdownController.cs` | Timer por elemento. Funcional. |
| Orquestador | `DirectorGame.cs` | Sistema de 3 vidas, reintento de elemento, UI de vidas (♥/♡). Conecta todos los subsistemas. |
| LLM | `LLMConnector.cs` (Groq, `llama-3.1-8b-instant`) | `Ask(systemPrompt, userMessage, onComplete, onError)` — coroutine, retry único en 429. Único uso actual: `SimonCommandGenerator` (1 llamada/ronda). Director aún no lo usa. |

---

## 2. Mapa de assets

**Existentes** (`Assets/Sprites/SceneDirector/`):
- `Curtain.png` — sprite del telón
- `AudienceSpriteSheet.png` — sprite sheet de audiencia
- `AudienceRef.png` — mockup de referencia para el layout de audiencia
- `TomatoSplat.png` — efecto de reacción negativa
- `Emoticon_Neutral/Happy/Surprised/Angry.png` — íconos de emoción (faltan Disgust, Fear, Sad)
- `HappyMaskRef.png`, `SurprisedMaskRef.png`, `AngryMaskRef.png` — referencias de máscara 3D

**Faltantes (a crear)**:
- Fondo de escenario para `StageBackground` (Image actualmente sin sprite)
- Sprite de "hoja de papel" / clipboard para el guion
- Íconos individuales de perfil/avatar para la fila de audiencia (si `AudienceSpriteSheet.png` no tiene frames recortables individualmente)
- Sprites de emoción para Disgust, Fear, Sad (si se implementa la aleatorización completa)

---

## 3. Orden de implementación recomendado

```
Issue 1 (telón/escenario visibles)
   │
   ├──> Issue 2 (sprites de emoción + aleatorización)  ─┐
   │                                                      │
   └──> Issue 3 (audiencia: fila de íconos animables)    ├──> Issue 4 (guion como hoja)
              │                                           │
              └──────────────> Issue 5 (LLM diálogos) <──┘
```

- **Issue 1** es la base visual — bajo riesgo, desbloquea poder ver el resto en contexto.
- **Issue 2** es independiente y rápida (carry-over de sesiones previas).
- **Issue 3** es el bloque más grande — requiere refactor de `AudienceController`.
- **Issue 4** depende de Issue 2 (íconos de emoción) para el contenido de la hoja.
- **Issue 5** depende de Issue 3 (necesita audiencia visible para mostrar `ShowDialogue`).

---

## 4. Issues (listas para copiar a GitHub)

### Issue 1 — Telón y fondo de escenario visibles

**Labels sugeridas**: `director`, `ui`, `priority:high`

**Descripción**:
El telón (`ScenarioController` + `Scenario.controller`) ya tiene la lógica de
animación (triggers `Open`/`Close`, eventos `OnOpenComplete_AnimEvent`/
`OnCloseComplete_AnimEvent` conectados desde `DirectorGame`), pero no está
confirmado que el sprite `Curtain.png` esté asignado y visible desde la cámara
del juego. `ScenarioRoot` está en una posición world-space (`470.35, 349.14,
12.74`) que parece fuera del frustum normal. Lo mismo para `StageBackground`
(Image en el Canvas), que existe pero sin sprite asignado.

**Tareas**:
- [ ] Verificar la posición/escala de `ScenarioRoot` respecto a la cámara del
      juego; reposicionar si está fuera de cámara.
- [ ] Confirmar que `Curtain.png` está asignado al `Image`/`SpriteRenderer` del
      telón.
- [ ] Asignar (o crear) un sprite de fondo de escenario para `StageBackground`.
- [ ] Probar `Open()`/`Close()` end-to-end en una partida real: telón abre al
      iniciar, cierra al terminar la ronda (ganada o perdida).

**Criterio de aceptación**: al jugar, se ve el telón abrirse al empezar y
cerrarse al terminar, con un fondo de escenario detrás de la acción.

---

### Issue 2 — Sprites de emoción + aleatorización de la secuencia (carry-over)

**Labels sugeridas**: `director`, `ui`, `content`, `priority:high`, `good-first-issue`

**Descripción**:
`ScriptController._emotionSprites[]` está vacío en `Director.unity`. El sistema
de swap (`RefreshUI()`) ya funciona — solo falta poblar el array. Además,
`GenerateLocalSequence()` solo cicla `Happy`/`Surprised`/`Angry`
(`(EmotionLabel)(1 + (i % 3))`), dejando sin usar `Disgust`/`Fear`/`Sad`.

**Tareas**:
- [ ] Crear `Assets/Sprites/Emotions/`, importar/crear sprites para las 7
      `EmotionLabel` (Neutral, Happy, Surprised, Angry, Disgust, Fear, Sad).
      Texture Type = Sprite (2D and UI).
- [ ] En el GameObject con `ScriptController`, asignar el array `Emotion
      Sprites` (Size 7) por índice de enum: `0=Neutral 1=Happy 2=Surprised
      3=Angry 4=Disgust 5=Fear 6=Sad`.
- [ ] Modificar `GenerateLocalSequence()` para elegir aleatoriamente entre las
      6 emociones no-neutrales, sin repetir la emoción anterior consecutivamente.

**Criterio de aceptación**: el ícono de emoción cambia correctamente para cada
elemento del guion, y la secuencia puede incluir cualquiera de las 6 emociones
sin repeticiones consecutivas.

---

### Issue 3 — Audiencia como fila de íconos de perfil animables individualmente

**Labels sugeridas**: `director`, `ui`, `refactor`, `priority:high`

**Descripción**:
`AudienceController` hoy controla **un solo objeto** con Animator
(`Idle`/`SlightMove`/`React`, bool `IsPositive`, trigger `React`). El pedido es
mostrar una **fila de íconos de perfil genéricos**, donde cada uno pueda
animarse independientemente de arriba hacia abajo simulando aplausos o
desaprobación. Existen `AudienceSpriteSheet.png` (posible fuente de íconos) y
`AudienceRef.png` (mockup de referencia de layout).

**Tareas**:
- [ ] Crear un prefab `AudienceMember` (Image de perfil + Animator propio con
      estados `Idle`/`React`, reutilizando o adaptando
      `Audience_ReactPos.anim`/`Audience_ReactNeg.anim` para un movimiento
      simple arriba-abajo).
- [ ] Refactorizar `AudienceController` a un **manager** que instancia N
      `AudienceMember` en una fila horizontal (UI `HorizontalLayoutGroup` o
      posiciones fijas), usando `AudienceRef.png` como referencia de
      composición.
- [ ] `ReactPositive()`/`ReactNegative()` ahora disparan la reacción en un
      subconjunto (o todos) de los miembros, con pequeño offset de tiempo entre
      ellos para un efecto de "ola".
- [ ] Mantener la API pública existente intacta: `SetIdle()`, `SlightMove()`,
      `ReactPositive()`, `ReactNegative()`, `ShowDialogue()`,
      `OnReactComplete_AnimEvent()` — `DirectorGame` no debería necesitar
      cambios para seguir funcionando.
- [ ] Si `AudienceSpriteSheet.png` no tiene íconos individuales recortables,
      crear/usar placeholders genéricos de perfil (ej. círculos con iniciales o
      siluetas).

**Criterio de aceptación**: se ve una fila de varios íconos de audiencia; al
pasar/fallar un elemento, varios (no necesariamente todos) se animan hacia
arriba/abajo de forma escalonada simulando aplauso/abucheo.

---

### Issue 4 — Guion mostrado como "hoja" visual

**Labels sugeridas**: `director`, `ui`, `priority:medium`

**Descripción**:
Actualmente el guion son solo textos sueltos (`EmotionText`,
`NextEmotionText`, `ProgressText`, `EmotionIcon`). El pedido es mostrarlo dentro
de un elemento visual tipo "hoja de papel" o clipboard.

**Tareas**:
- [ ] Crear/conseguir un sprite de papel/clipboard para `Assets/Sprites/SceneDirector/`.
- [ ] Crear un panel UI con ese sprite de fondo, reorganizando
      `EmotionText`/`NextEmotionText`/`ProgressText`/`EmotionIcon` dentro del
      marco.
- [ ] Mostrar la secuencia completa o una ventana de 2-3 elementos con sus
      íconos de emoción (depende de Issue 2), resaltando el elemento activo.
- [ ] Animación simple de aparición/ocultamiento de la hoja al iniciar/terminar
      la ronda — puede reusar el patrón de `_introText`/`ShowIntro()`/`HideIntro()`
      en `ScriptController`.

**Criterio de aceptación**: el guion se ve dentro de un elemento gráfico tipo
hoja de papel, con los íconos de emoción de la secuencia visibles y el
elemento activo resaltado.

---

### Issue 5 — Diálogos de audiencia vía LLM (Groq) sincronizados con la barra

**Labels sugeridas**: `director`, `llm`, `priority:medium`

**Descripción**:
La audiencia debe emitir frases cortas de aprobación/desaprobación mientras la
barra de aprobación sube o baja. `AudienceController.ShowDialogue(text,
duration)` ya existe para esto. `LLMConnector.Instance.Ask(systemPrompt,
userMessage, onComplete, onError)` (Groq, `llama-3.1-8b-instant`) es el punto
de integración — **no llamarlo por frame**, hay que pre-generar un lote de
frases por elemento para no saturar la API (mismo patrón que
`SimonCommandGenerator.GenerateFallbackText` para el fallback).

**Tareas**:
- [ ] En `ScriptController.OnElementStarted` (o equivalente en `DirectorGame`),
      pre-generar 3-4 frases de aprobación y 3-4 de desaprobación vía
      `LLMConnector.Instance.Ask(...)` con un system prompt tipo "audiencia de
      teatro, frases cortas en español, tono [aprobación/abucheo]".
- [ ] Implementar fallback a frases hardcodeadas si `onError` se dispara
      (siguiendo el patrón de `SimonCommandGenerator`).
- [ ] Ciclar las frases pre-generadas en eventos discretos de la barra (cruces
      de 25/50/75/100% de fill, o transiciones fill↔drain), llamando
      `AudienceController.Instance.ShowDialogue(linea, duración)`.
- [ ] Verificar en consola que no haya más de 1-2 llamadas a Groq por elemento.

**Criterio de aceptación**: durante una partida, la audiencia muestra frases
distintas de aprobación cuando la barra sube y de desaprobación cuando baja,
sin que la consola muestre llamadas repetidas/excesivas a la API de Groq.

---

## 5. Patrón de integración LLM (referencia)

Ejemplo basado en `SimonCommandGenerator.cs` (único uso actual de
`LLMConnector`):

```csharp
private void RequestAudienceLines(Action<List<string>> onReady)
{
    string systemPrompt =
        "Eres el público de un teatro. Responde solo con 3 frases cortas " +
        "de aprobación en español, separadas por '|'. Sin explicaciones.";

    LLMConnector.Instance.Ask(
        systemPrompt,
        "El actor está actuando bien, dale ánimo.",
        onComplete: response =>
        {
            var lines = response.Split('|')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            onReady?.Invoke(lines);
        },
        onError: _ =>
        {
            // Fallback — mismo patrón que SimonCommandGenerator.GenerateFallbackText
            onReady?.Invoke(new List<string> { "¡Bravo!", "¡Eso es!", "¡Sigue así!" });
        });
}
```

Llamar `RequestAudienceLines` **una vez por elemento** (no por frame), guardar
el resultado en una lista y consumir de ahí en los eventos de la barra.

---

## 6. Checklist de verificación end-to-end

- [ ] **Issue 1**: al jugar, telón abre/cierra visiblemente; fondo de
      escenario visible detrás de la UI de juego.
- [ ] **Issue 2**: cada elemento del guion muestra el ícono correcto; la
      secuencia incluye variedad de las 6 emociones sin repetir consecutivas.
- [ ] **Issue 3**: fila de íconos de audiencia visible; reacciones
      escalonadas (no todas a la vez) en pase/fallo de elemento.
- [ ] **Issue 4**: guion visible dentro de un panel tipo hoja, con íconos y
      elemento activo resaltado.
- [ ] **Issue 5**: diálogos de audiencia distintos en aprobación/desaprobación;
      consola muestra ≤ 1-2 llamadas Groq por elemento (revisar logs de
      `LLMConnector`/`ServiceLogger`).
