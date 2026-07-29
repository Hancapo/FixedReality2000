# Fixed Reality 2000

Parche BepInEx para **Broken Reality 2000** centrado en corregir sus opciones
gráficas, ampliar la configuración disponible y aplicar optimizaciones de bajo
riesgo.

## Requisitos

- Broken Reality 2000 para Windows, con backend Mono.
- BepInEx 5.4.23.5.
- Unity 2023.2.20f1, usado por la versión actual del juego.

## Cambios principales

### Video

- Detecta correctamente las resoluciones y frecuencias disponibles.
- Admite resoluciones ultrawide 21:9 y super-ultrawide 32:9 reportadas por el
  monitor.
- Agrega un filtro Aspect Ratio bajo Resolution con AUTO, 4:3, 16:9, 16:10,
  21:9 y 32:9. Fullscreen y Borderless solo muestran modos reales; Windowed
  también ofrece tamaños calculados desde el monitor, sin una tabla fija.
- Cambiar Aspect Ratio solamente actualiza la lista. La ventana cambia cuando
  se elige una Resolution.
- Usa el aspect ratio real en las cámaras de pantalla y expande la UI
  screen-space sin recortarla; 16:9 permanece sin cambios.
- Aplica Fullscreen, Borderless y Windowed durante la partida.
- Agrega límite de FPS: Unlimited, 60, 120, 144, 165, 240 y 360.
- Agrega filtrado Original o Nearest.
- Nearest solo afecta materiales bajo `ENVIRONMENT` fuera de `00_room` y
  usa Point con anisotropía 16x en texturas con mipmaps, conservando los
  valores originales para restaurarlos al volver a Original.
- Original habilita filtrado anisotrópico 16x en las texturas filtradas y con
  mipmaps bajo `ENVIRONMENT`, sin afectar `00_room`.
- Agrega MSAA: Off, 2x, 4x y 8x.
- Agrega el preset Very High.
- Repara las sombras de Medium, High y Very High, usando resoluciones
  progresivamente mayores.

### Game

- Agrega un slider de FOV de 50 a 120 grados.
- Conserva el FOV al cerrar y volver a abrir las opciones.
- Evita que animaciones del jugador restauren 60 grados.
- Compensa la posición y escala del viewmodel para que los objetos en la mano
  mantengan una composición estable al cambiar el FOV.

### Audio y sliders

- Muestra el valor numérico debajo del handle de los sliders.
- Corrige la presentación de los sliders de audio y FOV.

### Movimiento

- Mantén `Shift` para correr.
- Agrega head bobbing sutil al caminar y correr.

### Rendimiento

- Elimina el límite de 60 FPS que el juego fuerza en
  `BrokenPlayer.Prepare`, especialmente visible en Low.
- Puede desactivar `player_storecamera` mientras no se necesita. `F8` la
  restaura durante la escena actual.
- Activa SRP Batcher y dynamic batching de URP sin sustituir renderers ni
  modificar materiales.
- Cachea búsquedas repetidas de `Camera.main` y `GameObject.Find` en scripts
  concretos.

Las opciones del menú —FOV, límite de FPS, filtrado y MSAA— se guardan mediante
`PlayerPrefs`; no aparecen duplicadas en el CFG.

## Configuración

El archivo se genera en:

```text
Broken Reality 2000\BepInEx\config\FixedReality2000.cfg
```

Las opciones restantes son deliberadamente pequeñas:

```ini
[General]
ReloadConfigHotkey = F5
ShowReloadNotification = true

[Fixes]
FixLowQualityFpsCap = true

[Performance]
ToggleSecondaryCameraHotkey = F8
DisableUnusedStoreCamera = true
EnableRenderBatchingOptimizations = true
OptimizePerFrameLookups = true

[Movement]
EnableSprint = true
SprintMultiplier = 1.65
EnableHeadBobbing = true
HeadBobAmplitude = 0.03
HeadBobFrequency = 9
```

Guarda el CFG y pulsa `F5` para recargarlo durante la partida.

## Compilar

```powershell
dotnet build .\FixedReality2000.sln -c Release
```

Para compilar e instalar directamente:

```powershell
dotnet build .\FixedReality2000.sln -c Release -p:DeployOnBuild=true
```

Si el juego está instalado en otra carpeta:

```powershell
dotnet build .\FixedReality2000.sln -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Broken Reality 2000"
```

El DLL se instala en:

```text
Broken Reality 2000\BepInEx\plugins\FixedReality2000\FixedReality2000.dll
```

Consulta [CHANGELOG.md](CHANGELOG.md) para el detalle completo.
