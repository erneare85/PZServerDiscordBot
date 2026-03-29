# Análisis técnico y funcional — PZServerDiscordBot

**Proyecto:** bot de Discord para administración de servidores dedicados de *Project Zomboid* (Windows).  
**Fork activo:** [erneare85/PZServerDiscordBot](https://github.com/erneare85/PZServerDiscordBot). **Upstream original:** [egebilecen/PZServerDiscordBot](https://github.com/egebilecen/PZServerDiscordBot). El árbol local puede incluir personalizaciones (por ejemplo en `Program.cs`, `UserCommands.cs`, `BotUtility.cs`).  
**Fecha del análisis:** 29 de marzo de 2026.

---

## 1. Resumen ejecutivo

La solución es una aplicación de consola **.NET Framework 4.7.2** que:

- Conecta un **bot de Discord** (Discord.Net 3.8.1) con prefijo `!` para comandos de texto.
- Controla el proceso del servidor PZ mediante **`server.bat`** y la **entrada estándar** del proceso (comandos tipo consola del juego).
- Programa tareas periódicas (reinicios, avisos, comprobación de mods del Workshop, arranque automático, comprobación de versión del bot, localizaciones).
- Persiste ajustes en **`pzdiscordbot.conf`** (JSON) y el token en **`bot_token.txt`** (o variable de entorno `EB_DISCORD_BOT_TOKEN`).

No hay soporte multi-gremio documentado: el código asume **un único servidor de Discord** (`Guilds.ElementAt(0)`).

---

## 2. Objetivo funcional

| Área | Descripción |
|------|-------------|
| **Canal público** | Comandos de usuario (`UserCommands`): estado del servidor, próximo reinicio, fecha in-game, información del bot, etc. |
| **Canal de comandos** | Configuración del bot (`BotCommands`) y control del servidor (`PZServerCommands`): arranque/parada, mensajes, whitelist, bans, teleports, opciones, mods Workshop, etc. |
| **Canal de log** | Mensajes informativos del bot (inicio, backups, avisos de reinicio, actualizaciones de mods, etc.). |
| **Automatización** | Reinicios programados (intervalo u horarios), avisos previos, detección de actualización de ítems del Workshop con reinicio diferido, opción de auto-arranque si el proceso termina. |
| **Utilidades** | Parser de perks desde logs, backup en ZIP de rutas clave del perfil Zomboid, RAM/CPU, localización vía JSON. |

La separación operativa depende de que los administradores configuren **tres canales** y restrinjan el canal de comandos solo a personal de confianza.

---

## 3. Arquitectura técnica

### 3.1 Stack y empaquetado

- **Lenguaje:** C# (ensamblado `PZServerDiscordBot.exe`).
- **Framework:** .NET Framework **4.7.2** (proyecto clásico `.csproj` + `packages.config`).
- **NuGet destacados:** Discord.Net 3.8.1, Newtonsoft.Json 13.0.2, Costura.Fody (empaquetado de dependencias), DiscordRPC (Rich Presence; uso actual mayormente comentado en `Program.cs`).
- **Plataforma:** **solo Windows** (rutas, `server.bat`, `System.Management` para métricas, etc.).

### 3.2 Estructura lógica del código (`src/`)

| Carpeta / archivo | Rol |
|-------------------|-----|
| `Program.cs` | Punto de entrada: carga de ajustes, localización, scheduler, cliente Discord, login, eventos `Ready` / `Disconnected`. |
| `Bot/CommandHandler.cs` | Enrutado de mensajes con prefijo `!`, validación de canal según módulo, ayuda `!help`. |
| `Bot/Commands/*.cs` | Módulos Discord.Net: `UserCommands`, `BotCommands`, `PZServerCommands`, `AdminCommands`. |
| `Bot/Scheduler.cs` | Temporizador `System.Timers.Timer` que ejecuta callbacks de tareas programadas. |
| `Bot/Schedules/*.cs` | Implementación de cada tarea (reinicio, anuncios, Workshop, auto-start, versión bot). |
| `Bot/SettingsModel.cs` | Modelo JSON de configuración (`GuildId`, IDs de canales, horarios, flags). |
| `Bot/Localization.cs` | Carga de cadenas, exportación por defecto, comprobación remota de traducciones. |
| `Discord/DiscordUtility.cs` | Token, resolución de gremio/canales, mapa de comandos por módulo, embeds. |
| `PZServer/Util/ServerUtility.cs` | Proceso del servidor, envío de líneas a la consola, reinicios coordinados con el scheduler. |
| `PZServer/ServerPath.cs` | Rutas del perfil Zomboid; en Release intenta inferir `user.home` desde `server.bat`. |
| `PZServer/Parsers/*` | INI del servidor, parser de logs de perks. |
| `PZServer/ServerBackupCreator.cs` | Compresión ZIP de carpetas configuradas hacia `./server_backup`. |
| `Bot/Util/*` | Logger, HTTP, Steam Web API (Workshop), utilidades varias. |

### 3.3 Patrones de diseño observados

- **Estado global estático:** `Application` (`Client`, `BotSettings`, `Commands`, etc.) simplifica el acceso pero dificulta pruebas y escalado multi-instancia.
- **Sin inyección de dependencias efectiva:** `IServiceProvider` se pasa como `null` al `CommandService`.
- **Modularidad por canales:** la “autorización” de comandos administrativos es **por canal de Discord**, no por roles de Discord ni permisos explícitos en atributos de comandos.

---

## 4. Flujos principales

### 4.1 Arranque (`Program.Main` → `MainAsync`)

1. Si no existe `pzdiscordbot.conf`, se crea uno por defecto y se guarda.
2. Deserialización JSON de ajustes con `ObjectCreationHandling.Replace`.
3. Carga de localización; en compilación con `EXPORT_DEFAULT_LOCALIZATION`, exportación del JSON por defecto.
4. Obtención del token: archivo o variable de entorno (con escritura opcional al archivo).
5. En Release: `ServerPath.CheckCustomBasePath()` valida `server.bat` y ajusta rutas.
6. Registro de ítems en `Scheduler` e inicio del temporizador (intervalo base 1 s).
7. Opcional: arranque del servidor si `AutoServerStart` está activo (solo Release).
8. Creación de `DiscordSocketClient` con **`GatewayIntents.All`**, registro de módulos, login y `StartAsync`.
9. En `Ready`: primera comprobación de canales, actualización de localización, etc.

### 4.2 Procesamiento de comandos (`CommandHandler`)

- Solo mensajes con prefijo **`!`** (la rama de mención al bot está en la condición pero el comentario sobre `IsBot` permite que **bots** también disparen lógica si cumplen el prefijo).
- Si la configuración de canales está completa, solo se aceptan comandos en **canal público** o **canal de comandos** configurados.
- `!help` lista comandos según el canal actual.
- Comandos “administrativos” deben coincidir con el canal esperado para su módulo (`GetChannelIdOfCommandModule`).

### 4.3 Control del servidor PZ (`ServerUtility`)

- El servidor se lanza con `ProcessStartInfo` sobre **`.\server.bat`**, con **redirección de stdin** para inyectar comandos (`quit`, `save`, `servermsg`, whitelist, etc.).
- Reinicios programados y manuales actualizan intervalos del scheduler y sincronizan el anunciador.

### 4.4 Tareas programadas (ejemplos)

- **ServerRestart / ServerRestartAnnouncer:** ciclo de reinicio y avisos escalonados.
- **WorkshopItemUpdateChecker:** lee Workshop IDs del INI, consulta API pública de Steam, compara `TimeUpdated` con `Application.StartTime`; si hay actualización reciente, programa reinicio y notifica Discord y jugadores.
- **AutoServerStart:** reintenta arranque si el servidor no está en ejecución (según configuración).
- **BotVersionChecker:** invoca `BotUtility.NotifyLatestBotVersion()` (en el fork local la lógica de GitHub está **sustituida por un valor fijo**; ver observaciones).

---

## 5. Configuración y persistencia

| Recurso | Ubicación / formato | Contenido sensible |
|---------|---------------------|--------------------|
| Ajustes del bot | `.\pzdiscordbot.conf` | IDs de gremio y canales, horarios, flags. |
| Token Discord | `.\bot_token.txt` o `EB_DISCORD_BOT_TOKEN` | **Secreto crítico**. |
| Logs | `.\pzbot.log` | Traza y excepciones. |
| Localizaciones | `.\localization\*.json` | Textos UI del bot. |
| Backups | `.\server_backup\` | ZIPs de db, Server, Lua, Saves. |

El perfil del juego por defecto apunta a `%USERPROFILE%\Zomboid\` salvo que `server.bat` defina otra ruta vía `user.home`.

---

## 6. Integraciones externas

- **Discord API:** Gateway WebSocket + REST (Discord.Net); permisos e intents deben estar bien configurados en el portal de desarrolladores (el README original lo documenta).
- **Steam Web API:** `ISteamRemoteStorage/GetPublishedFileDetails/v1/` vía POST sin API key en el código revisado (API pública para metadatos de ítems publicados).
- **GitHub (raw):** `Localization` puede descargar `list.json` y ficheros de idioma desde el repositorio upstream (URLs fijas en código).

---

## 7. Mapa de módulos de comandos (alto nivel)

- **UserCommands:** interacción pública limitada (estado, reinicio, fecha de partida, `bot_info`, etc.). En el fork aparece un comando personalizado adicional.
- **BotCommands:** configuración de canales, horarios, caché de perks, auto-start, backup, localización, métricas de sistema.
- **PZServerCommands:** passthrough a consola del servidor y acciones de administración del juego.
- **AdminCommands:** en el código actual contiene un comando `!debug` marcado para depuración (vacío / llamada a Steam con array vacío); riesgo operativo si se despliega en producción sin restricciones adicionales.

---

## 8. Calidad, mantenimiento y riesgos transversales

- **Concurrencia:** el scheduler usa `System.Timers.Timer` (posible reentrada si un callback tarda más que el tick); algunas rutas usan `.Result` sobre tareas async, lo que puede causar **bloqueos** en contextos con sincronización UI o ASP.NET (menos crítico en consola, pero sigue siendo antipatrón).
- **Manejo de errores:** muchas llamadas a la consola del servidor capturan excepciones y las ignoran silenciosamente, lo que dificulta el diagnóstico.
- **Pruebas automatizadas:** no se observa proyecto de tests en el árbol típico; la validación es manual.
- **Secretos en disco:** la variable de entorno puede **persistirse** en `bot_token.txt` automáticamente, duplicando superficie de exposición.

---

## 9. Vulnerabilidades

Las siguientes entradas describen **riesgos de seguridad y abuso** inherentes al diseño o a la implementación actual, no un pentest formal.

1. **Superficie de control sin RBAC de Discord:** cualquier usuario que pueda escribir en el **canal de comandos** obtiene capacidad equivalente a administrador del servidor de juego (`!server_cmd`, parada, bans, items, etc.). No hay comprobación de rol de Discord en los módulos revisados.
2. **Inyección / abuso vía `!server_cmd` y comandos parametrizados:** se reenvía texto arbitrario a la consola del servidor; combinado con el punto anterior, es un vector de **ejecución de órdenes** dentro del modelo de confianza del dedicado.
3. **Comando `!debug` en `AdminCommands`:** expone una vía de ejecución en el mismo canal de administración; si el módulo está cargado y el canal es comprometido, amplía la superficie (aunque la implementación actual sea trivial o rota).
4. **Token de bot en archivo plano:** `bot_token.txt` y `pzdiscordbot.conf` en el directorio de trabajo son legibles por cualquier proceso/usuario con permisos en esa carpeta. La escritura automática del token desde variable de entorno **aumenta** la persistencia del secreto.
5. **`GatewayIntents.All`:** privilegia al máximo los intents del Gateway; incrementa datos expuestos al bot y va contra el principio de mínimo privilegio; además Discord puede requerir justificación para intents privilegiados.
6. **Rich Presence mal configurado si se reactiva:** en `Program.cs`, `DiscordRpcClient(DiscordUtility.GetToken())` usaría el **token del bot**; el RPC de Discord suele requerir **Client ID** de aplicación OAuth, no el token del bot. Reactivar ese código sin corregirlo puede filtrar credenciales o fallar de forma insegura.
7. **Exposición de información en logs y Discord:** `get_settings` y logs de comandos pueden revelar IDs internos y configuración; útil para soporte pero sensible si el canal se filtra.
8. **Falta de exclusión explícita en `.gitignore` para secretos:** el `.gitignore` del proyecto ignora `*.log` y carpetas de build, pero **no** lista de forma explícita `bot_token.txt` ni `pzdiscordbot.conf`; un error humano puede versionar secretos o configuración con IDs.
9. **Confianza en archivos locales del servidor:** manipular `server.bat` o el INI puede redirigir rutas (`user.home`) o comportamiento del proceso lanzado por el bot.

---

## 10. Actualizaciones

1. **Discord.Net 3.8.1:** comprobar en [GitHub de Discord.Net](https://github.com/discord-net/Discord.Net) si hay versiones más recientes con correcciones de Gateway, intents o breaking changes en la API de Discord.
2. **Newtonsoft.Json 13.0.2:** valorar actualización a la última 13.x por parches de serialización y alineación con el ecosistema.
3. **.NET Framework 4.7.2:** el runtime está en modo mantenimiento; a medio plazo conviene **migrar a .NET 8 (LTS)** u otra versión soportada para parches de seguridad del runtime y mejoras de `HttpClient`.
4. **Paquetes `System.*` 4.3.x:** muchas son transiciones hacia APIs portable; al migrar a SDK-style / .NET moderno, gran parte se unifica en el BCL.
5. **Costura.Fody / Fody:** mantener versiones compatibles con el toolchain de MSBuild que uséis en CI.
6. **Comprobación de versión del bot:** en `BotUtility.GetLatestBotVersion()` el código que consultaba GitHub está **comentado** y sustituido por un retorno fijo; reactivar la URL oficial o la de vuestro fork y restaurar la comparación semántica devuelve valor real al schedule `BotVersionChecker`.
7. **`Application.BotRepoURL`:** en `Program.cs` está como `" - "`; enlaces de ayuda y mensajes de localización que usan `{repo_url}` quedan **rotos o inútiles** hasta restaurar la URL del repositorio.

---

## 11. Mejoras recomendadas

1. **Autorización explícita:** usar `[RequireUserPermission]` / `[RequireRole]` o una lista de IDs de usuario permitidos para `BotCommands` y `PZServerCommands`, además del canal privado.
2. **Principio de mínimo privilegio en Discord:** sustituir `GatewayIntents.All` por el subconjunto mínimo (mensajes, contenido de mensajes si aplica, etc.) según la documentación actual de Discord.
3. **Eliminar o proteger `!debug`:** borrar el comando en Release, condicionarlo a `#if DEBUG`, o restringirlo a un ID de usuario fijo configurable.
4. **Async end-to-end:** reemplazar `.Result` en `WorkshopItemUpdateChecker` (y similares) por `await` dentro de `async`/`Task.Run` con propagación correcta de errores.
5. **Gestión de secretos:** no escribir el token desde la variable de entorno a disco por defecto; documentar uso solo de entorno o de un almacén seguro; añadir `bot_token.txt` y `pzdiscordbot.conf` a `.gitignore` en la raíz del repo si pueden contener datos sensibles del entorno.
6. **Corregir Rich Presence:** si se desea RPC, usar **Client ID** y flujo adecuado, nunca el token del bot en `DiscordRpcClient`.
7. **Corregir bug de log en `UserCommands` (`game_date`):** el mensaje de log usa `string.Format` con un placeholder faltante para la ruta del archivo, lo que reduce la trazabilidad ante fallos.
8. **Multi-gremio (opcional):** parametrizar `GuildId` y dejar de usar `ElementAt(0)` para evitar comportamiento indefinido si el bot está en varios servidores.
9. **Tests mínimos:** pruebas unitarias para `IniParser`, `Scheduler.GetIntervalFromTimes`, y serialización de `BotSettings`.
10. **Observabilidad:** niveles de log, rotación de fichero y correlación de `command -> efecto en servidor` sin registrar secretos.

---

## 12. Observaciones generales para mejorar el proyecto

- **Alineación con el upstream:** el README y las URLs de localización siguen apuntando al repositorio original; si este fork es el que mantenéis, conviene unificar documentación, badges y `BotRepoURL` para que soporte y contribuciones sean coherentes.
- **Personalización visible en código:** textos de actividad del bot (“chiteros en el servidor”), enlaces de Discord incrustados en Rich Presence comentado, y comandos de broma en `UserCommands` son adecuados para un servidor concreto pero deberían **externalizarse** a configuración o localización si el objetivo es redistribuir el binario.
- **`EXPORT_DEFAULT_LOCALIZATION` definido en `Program.cs`:** en builds de producción, valorar desactivar la exportación automática para no sobrescribir o ensuciar el árbol de `localization/` inadvertidamente.
- **Coherencia de versión:** `AssemblyInfo` / `SemanticVersion` en código deben coincidir con releases y etiquetas git para que `BotVersionChecker` y los mensajes a usuarios sean fiables.
- **README y seguridad:** añadir una sección explícita sobre **canal de comandos restringido**, rotación de token si se filtra, y buenas prácticas de permisos del bot en Discord.
- **Roadmap técnico:** migración a .NET moderno permitiría `IHost`, configuración con `Microsoft.Extensions.Configuration`, DI limpia, y despliegue como servicio de Windows con mejor supervisión.

---

*Fin del documento.*
