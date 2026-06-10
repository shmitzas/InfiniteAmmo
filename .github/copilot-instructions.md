# SwiftlyS2 Plugin Development Guidelines

> Copilot instructions for developing Counter-Strike 2 plugins on the **SwiftlyS2** framework for the Rampage.lt server network.

---

## Table of Contents

1. [Plugin Structure & Base Variables](#1-plugin-structure--base-variables)
2. [Configuration](#2-configuration)
3. [RampageAPI Configuration](#3-rampageapi-configuration)
4. [Logging (Spam-Free, DetailedLogging)](#4-logging)
5. [Commands](#5-commands)
6. [Translations / Localization](#6-translations--localization)
7. [Events, Listeners & Hooks](#7-events-listeners--hooks)
8. [Shared Interfaces (Cross-Plugin APIs)](#8-shared-interfaces)
9. [Thread Safety & Async Data Access](#9-thread-safety--async-data-access)
10. [Data Management & Optimization](#10-data-management--optimization)
11. [Database Patterns](#11-database-patterns)
12. [Menus & UI](#12-menus--ui)
13. [Player Validation](#13-player-validation)
14. [Plugin Lifecycle](#14-plugin-lifecycle)
15. [Auditing & Bug Prevention](#15-auditing--bug-prevention)
16. [Using SwiftlyS2 Docs & CS2 Schema Dumps via @sd MCP](#16-using-swiftlys2-docs--cs2-schema-dumps-via-sd-mcp)
17. [CI/CD & Deployment](#17-cicd--deployment)

---

## 1. Plugin Structure & Base Variables

### Plugin Class Declaration

Every plugin extends `BasePlugin` and is decorated with `[PluginMetadata]`:

```csharp
using SwiftlyS2.Shared;

[PluginMetadata(
    Id = "PluginName",
    Version = "1.0.0",
    Name = "Plugin Name",
    Author = "Shmitzas",
    Description = "Short description of the plugin."
)]
public partial class PluginName : BasePlugin
{
    // Core reference (static for access from partial classes/services)
    public static new ISwiftlyCore Core { get; private set; } = null!;

    // Logger — use ILogger<T> from DI for structured logging
    private ILogger<PluginName> logger = null!;

    // Plugin configuration — loaded from config.jsonc
    private Config cfg = null!;

    // Constructor — receives ISwiftlyCore via DI
    public PluginName(ISwiftlyCore core) : base(core) { }
}
```

### Essential Base Variables

| Variable | Type | Purpose |
|----------|------|---------|
| `Core` | `ISwiftlyCore` | Central entry point for ALL framework services |
| `logger` | `ILogger<T>` | Structured logging (from `Microsoft.Extensions.Logging`) |
| `cfg` | `Config` | Plugin-specific configuration model |
| `apiCfg` | `RampageApiConfig` | Rampage API configuration (if needed) |

### Key `Core` Sub-Services

| Service | Access | Purpose |
|---------|--------|---------|
| `Core.PlayerManager` | Player queries | `GetAllPlayers()`, `GetAllValidPlayers()`, `SendChat()` |
| `Core.Command` | Command registration | `RegisterCommand()`, `RegisterCommandAlias()`, `UnregisterCommand()` |
| `Core.GameEvent` | CS2 game events | `HookPre<T>()`, `HookPost<T>()` |
| `Core.Event` | Framework lifecycle events | `OnClientPutInServer`, `OnMapLoad`, `OnTick`, etc. |
| `Core.Scheduler` | Timers & main-thread scheduling | `NextTick()`, `NextWorldUpdate()`, `RepeatBySeconds()` |
| `Core.Configuration` | Config management | `InitializeJsonWithModel<T>()`, `GetConfigPath()` |
| `Core.Database` | Database connections | `GetConnection(string name)` |
| `Core.Localizer` | Server-default translations | `Core.Localizer["key", args]` |
| `Core.Translation` | Per-player translations | `GetPlayerLocalizer(player)` |
| `Core.MenusAPI` | Menu system | `CreateBuilder()`, `OpenMenuForPlayer()` |
| `Core.Permission` | Permission checks | `PlayerHasPermissions(steamId, perms)` |
| `Core.CSGODirectory` | Server file path | Path to `csgo/` directory |

### Project File (.csproj) Template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SwiftlyS2.CS2" Version="*" />
    <!-- MS Extensions — always exclude runtime assets -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="*"
                      ExcludeAssets="runtime" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="*"
                      ExcludeAssets="runtime" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="*"
                      ExcludeAssets="runtime" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="*"
                      ExcludeAssets="runtime" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

> **IMPORTANT**: All `Microsoft.Extensions.*` packages must use `ExcludeAssets="runtime" PrivateAssets="all"` since the runtime already provides them.

---

## 2. Configuration

### Config Model

Define config as a nested class or standalone class:

```csharp
namespace PluginName;

public class Config
{
    public bool DetailedLogging { get; set; } = false;
    public string DatabaseConnection { get; set; } = "default";
    public int SomeInterval { get; set; } = 30;
    public List<string> SomeList { get; set; } = ["default_value"];
    // Add all configurable values with sensible defaults
}
```

### Loading Configuration (Standard Pattern)

```csharp
private void LoadConfig()
{
    const string fileName = "config.jsonc";
    const string section = "PluginName";

    // 1. Initialize JSON file with model defaults (auto-creates config if missing)
    Core.Configuration
        .InitializeJsonWithModel<Config>(fileName, section)
        .Configure(builder =>
        {
            builder.AddJsonFile(
                Core.Configuration.GetConfigPath(fileName),
                optional: false,
                reloadOnChange: true
            );
        });

    // 2. Build DI service provider
    ServiceCollection services = new();
    services
        .AddSwiftly(Core, addLogger: true, addConfiguration: true)
        .AddOptionsWithValidateOnStart<Config>()
        .BindConfiguration(section);

    var provider = services.BuildServiceProvider();

    // 3. Resolve typed config + logger
    logger = provider.GetRequiredService<ILogger<PluginName>>();
    cfg = provider.GetRequiredService<IOptions<Config>>().Value;
}
```

### Hot-Reload Configuration (IOptionsMonitor)

For configs that should update without server restart:

```csharp
internal IOptionsMonitor<Config> Config { get; private set; } = null!;

private IOptionsMonitor<T> BuildConfigService<T>(string fileName, string sectionName)
    where T : class, new()
{
    Core.Configuration
        .InitializeJsonWithModel<T>(fileName, sectionName)
        .Configure(cfg => cfg.AddJsonFile(
            Core.Configuration.GetConfigPath(fileName),
            optional: false,
            reloadOnChange: true));

    ServiceCollection services = new();
    services.AddSwiftly(Core)
        .AddOptions<T>()
        .BindConfiguration(sectionName);

    var provider = services.BuildServiceProvider();
    return provider.GetRequiredService<IOptionsMonitor<T>>();
}

// Access current value:
var value = Config.CurrentValue.SomeProperty;

// React to changes:
Config.OnChange(newConfig => { /* handle hot reload */ });
```

### Multiple Config Files

Plugins can split configuration across multiple files:

```csharp
Config = BuildConfigService<PluginConfig>("config.json", "K4Ranks");
Points = BuildConfigService<PointsConfig>("points.json", "K4RanksPoints");
Commands = BuildConfigService<CommandsConfig>("commands.json", "K4RanksCommands");
```

---

## 3. RampageAPI Configuration

The global Rampage API config lives at `addons/swiftlys2/configs/api.jsonc` and is shared across plugins.

### Config Model

```csharp
public class RampageApiConfig
{
    public string ApiUrl { get; set; } = "https://api.example.com";
    public string ApiSecret { get; set; } = "";
    public string Server { get; set; } = "public";
}
```

### Loading RampageAPI Config

```csharp
private void LoadRampageApiConfig(ServiceCollection services)
{
    const string apiFileName = "api.jsonc";
    const string apiSection = "RampageApi";
    var globalConfigPath = Path.Combine(
        Core.CSGODirectory, "addons", "swiftlys2", "configs", apiFileName);

    // Auto-create if missing
    if (!File.Exists(globalConfigPath))
    {
        var defaultConfig = System.Text.Json.JsonSerializer.Serialize(
            new { RampageApi = new RampageApiConfig() },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
        Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
        File.WriteAllText(globalConfigPath, defaultConfig);
    }

    Core.Configuration.Configure(builder =>
    {
        builder.AddJsonFile(globalConfigPath, optional: false, reloadOnChange: true);
    });

    services
        .AddOptionsWithValidateOnStart<RampageApiConfig>()
        .BindConfiguration(apiSection);
}

// After building the provider:
apiCfg = provider.GetRequiredService<IOptions<RampageApiConfig>>().Value;
```

### Using the API Config

```csharp
private void RegisterRampageApi()
{
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(apiCfg.ApiUrl)
    };

    if (!string.IsNullOrWhiteSpace(apiCfg.ApiSecret))
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiCfg.ApiSecret);
    }

    // Create API service instances with the configured HttpClient
    myApiService = new MyApiService(httpClient, apiCfg.Server);
}
```

---

## 4. Logging

### DetailedLogging Pattern

Add `DetailedLogging` to your Config class. ALL verbose/informational logging MUST be gated behind this flag:

```csharp
public class Config
{
    public bool DetailedLogging { get; set; } = false;
    // ...
}
```

### Logging Rules

| Level | Gated by DetailedLogging? | When to use |
|-------|--------------------------|-------------|
| `LogInformation` | **YES** — always wrap in `if (cfg.DetailedLogging)` | Method entry, flow tracing, data values |
| `LogDebug` | **YES** — always wrap in `if (cfg.DetailedLogging)` | Detailed internal state |
| `LogWarning` | **NO** — always log | Unexpected but recoverable situations |
| `LogError` | **NO** — always log | Failures, exceptions |

### Examples

```csharp
// CORRECT — Detailed logging gated by config flag
if (cfg.DetailedLogging)
    logger.LogInformation("Loading player data for SteamID {SteamId}", steamId);

if (cfg.DetailedLogging)
    logger.LogInformation("Not ready players count: {Count}", notReadyPlayers.Count);

if (cfg.DetailedLogging)
    logger.LogInformation("Randomly selected site {Site} for this round.", site);

// CORRECT — Errors and warnings are ALWAYS logged (never gated)
logger.LogError(ex, "Failed to load player data for SteamID {SteamId}", steamId);
logger.LogWarning("Player in slot {Slot} is no longer valid, skipping.", playerSlot);

// CORRECT — Use structured logging placeholders (not string interpolation) for log params
logger.LogInformation("Ban details: Admin={AdminSteamId}, Player={PlayerSteamId}, Duration={Duration}",
    adminSteamId, playerSteamId, duration);

// WRONG — Don't use string interpolation for log message templates
// logger.LogInformation($"Ban details: Admin={adminSteamId}"); // BAD
```

### Logger Access Patterns

```csharp
// Pattern 1: ILogger<T> from DI (preferred)
private ILogger<PluginName> logger = null!;
logger.LogInformation("Message");

// Pattern 2: Via Core (when DI is not set up)
Core.Logger.LogWarning("Message");
```

---

## 5. Commands

### Pattern A: `[Command]` Attribute (Simple/Static Commands)

```csharp
// Basic command
[Command("mycommand")]
public void OnMyCommand(ICommandContext context)
{
    if (!context.IsSentByPlayer) return;
    var player = context.Sender!;
    player.SendChat(Core.Localizer["server_prefix"] + " Hello!");
}

// Command with permission
[Command("admin_action", permission: "myplugin.admin")]
public void OnAdminAction(ICommandContext context) { }

// Command with aliases
[Command("invite")]
[CommandAlias("inv")]
[CommandAlias("kv")]
public void OnInvite(ICommandContext context) { }
```

### Pattern B: `Core.Command.RegisterCommand()` (Dynamic/Config-Driven)

```csharp
// Registration
private void RegisterCommands()
{
    Core.Command.RegisterCommand("mycommand", OnMyCommand, registerRaw: true, permission: "myplugin.use");
    Core.Command.RegisterCommandAlias("mycommand", "mc", registerRaw: true);
}

// Config-driven registration
internal void RegisterCommands()
{
    var commandHandlers = new Dictionary<string, ICommandService.CommandListener>
    {
        { "ready", OnReady },
        { "unready", OnUnReady },
        { "timeout", OnTimeout },
    };

    foreach (var (commandName, handler) in commandHandlers)
    {
        if (!cfg.Commands.TryGetValue(commandName, out var commandInfo)) continue;
        Core.Command.RegisterCommand(commandName, handler, true, commandInfo.Permission);
        foreach (var alias in commandInfo.Aliases)
            Core.Command.RegisterCommandAlias(commandName, alias);
    }
}

// Unregistration (in Unload or when needed)
internal void UnregisterCommands()
{
    foreach (var commandName in cfg.Commands.Keys.ToList())
        Core.Command.UnregisterCommand(commandName);
}
```

### Command Handler — ICommandContext

```csharp
private void OnMyCommand(ICommandContext context)
{
    // Check if sent by a player (vs console)
    if (!context.IsSentByPlayer) return;

    var player = context.Sender!;  // IPlayer
    var args = context.Args;       // string[] (excludes command name)

    // Validate arguments
    if (args.Length < 1)
    {
        context.Reply("Usage: !mycommand <target>");
        return;
    }

    // Permission check
    if (Core.Permission.PlayerHasPermissions(player.SteamID, ["myplugin.vip"]))
    {
        player.SendChat(Core.Localizer["server_prefix"] + " VIP feature activated!");
    }
}
```

### Sending Responses

```csharp
// Reply to command source (chat or console)
context.Reply("Message");

// Send to specific player
player.SendChat(Core.Localizer["server_prefix"] + " " + Core.Localizer["some.key"]);

// Send to player async (from background thread)
player.SendConsoleAsync(Core.Localizer["some.key", arg1, arg2]);

// Broadcast to all players
Core.PlayerManager.SendChat(Core.Localizer["server_prefix"] + " " + message);

// Helper pattern for safe message delivery (respects NextTick)
private void PrintMessageToPlayer(IPlayer? player, string message)
{
    Core.Scheduler.NextTick(() =>
    {
        if (player == null || !player.IsValid) return;
        player.SendChat(Core.Localizer["server_prefix"] + " " + message);
    });
}
```

---

## 6. Translations / Localization

### File Structure

Translations live in `resources/translations/` as JSONC files:

```
resources/
  translations/
    en.jsonc    ← English (ALWAYS create)
    lt.jsonc    ← Lithuanian (ALWAYS create)
```

> **RULE**: Always create both `en.jsonc` and `lt.jsonc` translation files.

### Translation File Format

```jsonc
// resources/translations/en.jsonc
{
    "server_prefix": "[ [darkred]Rampage.lt [default]]",
    "welcome.message": "Welcome to the server, [lime]{0}[default]!",
    "error.not_alive": "You must be [red]alive [default]to use this command!",
    "info.balance": "Your balance: [gold]{0}[default] credits",
    "admin.ban.success": "[green]{0}[default] banned [red]{1}[default] for {2} — Reason: {3}"
}
```

```jsonc
// resources/translations/lt.jsonc
{
    "server_prefix": "[ [darkred]Rampage.lt [default]]",
    "welcome.message": "Sveiki atvykę į serverį, [lime]{0}[default]!",
    "error.not_alive": "Jūs turite būti [red]gyvas[default], kad naudotumėte šią komandą!",
    "info.balance": "Jūsų balansas: [gold]{0}[default] kreditai",
    "admin.ban.success": "[green]{0}[default] užblokavo [red]{1}[default] {2} — Priežastis: {3}"
}
```

### Available Color Tags

`[default]`, `[darkred]`, `[green]`, `[lime]`, `[red]`, `[gold]`, `[silver]`, `[blue]`, `[grey]`, `[yellow]`, `[lightblue]`, `[orange]`, `[purple]`, `[magenta]`, `[lightred]`

### Placeholders

Use C# `string.Format` positional syntax: `{0}`, `{1}`, `{2}`, etc.

### HTML Content (for center messages)

```jsonc
{
    "center.ready_counter": "[<span color=\"red\">{0}</span>/<span color=\"green\">{1}</span>]",
    "center.logo": "<img style='height: 10px;' src='https://example.com/logo.png' alt='logo'><hr><h1 color=\"lightblue\" class=\"fontSize-m\">Rampage.lt</h1>"
}
```

### Using Translations in Code

```csharp
// Server-default locale (most common)
Core.Localizer["server_prefix"]                                    // Simple key
Core.Localizer["welcome.message", playerName]                     // With arguments
Core.Localizer["admin.ban.success", admin, target, duration, reason] // Multiple args

// Per-player locale (player sees text in their CS2 language)
var playerLoc = Core.Translation.GetPlayerLocalizer(player);
player.SendChat($"{playerLoc["server_prefix"]} {playerLoc["welcome.message", player.PlayerName]}");

// Helper pattern for prefixed messages
public static void SendChatPrefixed(ISwiftlyCore core, IPlayer player, string message)
{
    var prefix = core.Localizer["server_prefix"] ?? string.Empty;
    player.SendChat($"{prefix} {message}");
}

// Broadcast with localization
public static void BroadcastLocalized(ISwiftlyCore core, string key, params object[] args)
{
    var message = core.Localizer[key, args];
    core.PlayerManager.SendChat(core.Localizer["server_prefix"] + " " + message);
}
```

---

## 7. Events, Listeners & Hooks

SwiftlyS2 has **four** distinct event mechanisms. Understand when to use each:

### A. `[GameEventHandler]` — Declarative CS2 Game Events

Auto-discovered attribute on public methods. **Unregistered automatically on unload.**

```csharp
[GameEventHandler(HookMode.Post)]
public HookResult OnPlayerDeath(EventPlayerDeath @event)
{
    var victim = @event.Accessor.GetPlayer("userid");
    var attacker = @event.Accessor.GetPlayer("attacker");
    if (victim == null || !victim.IsValid) return HookResult.Continue;
    // ... logic ...
    return HookResult.Continue;
}

[GameEventHandler(HookMode.Pre)]
public HookResult OnClientDisconnect(EventClientDisconnect @event)
{
    @event.DontBroadcast = true;
    return HookResult.Handled;  // Suppress default behavior
}
```

### B. `Core.GameEvent.HookPre/HookPost` — Imperative CS2 Game Events

Manual registration. **Unregistered automatically on unload.**

```csharp
// In Load() or a registration method
Core.GameEvent.HookPost<EventPlayerHurt>(OnPlayerHurt);
Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
Core.GameEvent.HookPre<EventPlayerDeath>(OnPlayerDeathPre);

// Handler signature
private HookResult OnPlayerHurt(EventPlayerHurt @event)
{
    // ... logic ...
    return HookResult.Continue;
}
```

### C. `Core.Event` — Framework Lifecycle Events (Manual +=/-=)

C# event delegates for framework hooks. **MUST be manually unregistered with `-=`.**

```csharp
// Registration
private void RegisterListeners()
{
    Core.Event.OnClientPutInServer += OnClientPutInServer;
    Core.Event.OnClientDisconnected += OnClientDisconnected;
    Core.Event.OnMapLoad += OnMapLoad;
    Core.Event.OnPrecacheResource += OnPrecacheResource;
    Core.Event.OnTick += OnTick;
}

// CRITICAL: Unregister in Unload() to prevent memory leaks
private void UnregisterListeners()
{
    Core.Event.OnClientPutInServer -= OnClientPutInServer;
    Core.Event.OnClientDisconnected -= OnClientDisconnected;
    Core.Event.OnMapLoad -= OnMapLoad;
    Core.Event.OnPrecacheResource -= OnPrecacheResource;
    Core.Event.OnTick -= OnTick;
}

public override void Unload()
{
    UnregisterListeners();
}
```

Available `Core.Event` types: `OnClientPutInServer`, `OnClientDisconnected`, `OnClientConnected`, `OnClientSteamAuthorize`, `OnMapLoad`, `OnMapUnload`, `OnPrecacheResource`, `OnTick`, `OnEntityTakeDamage`, `OnEntityCreated`, `OnSteamAPIActivated`, `OnStartupServer`, `OnItemServicesCanAcquireHook`

### D. `[EventListener<T>]` — Declarative Framework Events

Attribute-based approach for framework events. **Unregistered automatically on unload.**

```csharp
[EventListener<EventDelegates.OnClientSteamAuthorize>]
public void OnClientSteamAuthorize(IOnClientSteamAuthorizeEvent @event)
{
    var playerId = @event.PlayerId;
    Task.Run(async () => { await LoadPlayerData(playerId); });
}

[EventListener<EventDelegates.OnClientDisconnected>]
public void OnClientDisconnected(IOnClientDisconnectedEvent @event) { }

[EventListener<EventDelegates.OnStartupServer>]
public void OnStartupServer(IOnStartupServerEvent @event) { }
```

### E. Special Hook Attributes

```csharp
[ClientChatHookHandler]
public HookResult OnClientChat(int playerId, string message, bool teamonly)
{
    // Return HookResult.Handled to block the message
    return HookResult.Continue;
}

[ClientCommandHookHandler]
public HookResult OnClientCommand(int playerId, string commandLine)
{
    return HookResult.Continue;
}
```

### HookResult Values

| Value | Effect |
|-------|--------|
| `HookResult.Continue` | Allow event to proceed normally |
| `HookResult.Handled` | Block/suppress the event |

### Summary: Which to Use?

| Mechanism | Auto-Unregister | Use When |
|-----------|----------------|----------|
| `[GameEventHandler]` | Yes | Standard CS2 game events (declarative) |
| `Core.GameEvent.HookPre/Post` | Yes | Dynamic/conditional game event registration |
| `Core.Event +=/-=` | **No** — manual `-=` required | Framework lifecycle events (imperative) |
| `[EventListener<T>]` | Yes | Framework lifecycle events (declarative, preferred) |

---

## 8. Shared Interfaces

The shared interface system enables cross-plugin APIs via a three-phase lifecycle.

### Phase 1: Define Contract (Separate Project)

```csharp
// MyPlugin.Contract/IMyPluginAPIv1.cs
namespace MyPlugin.Contract;

public interface IMyPluginAPIv1
{
    int GetPlayerScore(IPlayer player);
    void AddPlayerScore(IPlayer player, int amount);
    event Action<ulong, int>? OnScoreChanged;
}
```

### Phase 2: Implement & Register (Provider Plugin)

```csharp
public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
{
    var api = new MyPluginAPIv1(Core, _service);
    interfaceManager.AddSharedInterface<IMyPluginAPIv1, MyPluginAPIv1>(
        "MyPlugin.API.v1", api);
}
```

### Phase 3: Consume (Consumer Plugin)

```csharp
private IMyPluginAPIv1? _myPluginApi;

public override void UseSharedInterface(IInterfaceManager interfaceManager)
{
    // Always check existence first
    if (!interfaceManager.HasSharedInterface("MyPlugin.API.v1"))
    {
        Core.Logger.LogWarning("MyPlugin API not found!");
        return;
    }

    _myPluginApi = interfaceManager.GetSharedInterface<IMyPluginAPIv1>("MyPlugin.API.v1");
}

// Use in OnSharedInterfaceInjected for operations that depend on other shared interfaces
public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
{
    if (_myPluginApi == null) return;
    // Safe to use APIs here — all plugins have registered their interfaces
    _myPluginApi.OnScoreChanged += HandleScoreChanged;
}
```

### Multi-Key Resolution (Version Fallback)

```csharp
private T? ResolveSharedInterface<T>(IInterfaceManager mgr, IEnumerable<string> keys)
    where T : class
{
    foreach (var key in keys.Distinct(StringComparer.Ordinal))
    {
        if (!mgr.HasSharedInterface(key)) continue;
        try { return mgr.GetSharedInterface<T>(key); }
        catch (Exception ex) { Core.Logger.LogError(ex, "Failed to resolve '{Key}'.", key); }
    }
    return null;
}
```

### Known Shared Interface Keys

| Key | Plugin | Contract |
|-----|--------|----------|
| `"Economy.API.v1"` | Economy | `IEconomyAPIv1` |
| `"Cookies.Server.v1"` | Cookies | `IServerCookiesAPIv1` |
| `"Cookies.Player.v1"` | Cookies | `IPlayerCookiesAPIv1` |
| `"ShopCore.API.v2"` | ShopCore | `IShopCoreApiV2` |
| `"AdvancedAdmin.API"` | AdvancedAdmin | `IAdvancedAdmin` |
| `"K4LevelRanks.Api.v1"` | K4-LevelRanks | `IK4LevelRanksApi` |
| `"K4Guilds.Api.v1"` | K4-Guilds | `IK4GuildsApi` |
| `"K4Arena.Api.v1"` | K4-Arenas | `IK4ArenaApi` |
| `"MixScrims.API"` | MixScrims | `IMixScrims` |
| `"audio"` | Audio | `IAudioApi` |
| `"WeaponSkins.API"` | WeaponSkins | `IWeaponSkinsApi` |

---

## 9. Thread Safety & Async Data Access

### Critical Rule

**All game-state data (players, entities, pawns, controllers) is ONLY safe to access from the main thread.** Background threads MUST marshal back to the main thread before touching game data.

### The Canonical Pattern: `Task.Run` + `Core.Scheduler.NextTick`

```csharp
// Database read on background thread → update on main thread
Task.Run(async () =>
{
    try
    {
        using var connection = Core.Database.GetConnection(cfg.DatabaseConnection);
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<PlayerData>(
            "SELECT * FROM players WHERE steamid = @SteamId",
            new { SteamId = steamId });

        // Marshal back to main thread for game-state access
        Core.Scheduler.NextTick(() =>
        {
            if (player == null || !player.IsValid) return;  // Re-validate!
            playerCache[player] = result;
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to load data for {SteamId}", steamId);
    }
});
```

### `NextWorldUpdate` — For Entity/Pawn Modifications

```csharp
Core.Scheduler.NextWorldUpdate(() =>
{
    var pawn = player.PlayerPawn;
    if (pawn != null)
    {
        pawn.GravityScale = 3.0f;
        pawn.GravityScaleUpdated();
    }
});
```

### `NextWorldUpdateAsync` — Async Main-Thread Access

```csharp
// Get thread-unsafe data from main thread in an async context
var players = await Core.Scheduler.NextWorldUpdateAsync(() =>
    Core.PlayerManager.GetAllValidPlayers()
        .Where(p => p != null && p.IsValid && !p.IsFakeClient)
        .ToList()
);

// Now safely iterate on the background thread
foreach (var player in players)
{
    await SavePlayerDataAsync(player.SteamID);
}
```

### Fire-and-Forget Database Writes

```csharp
// Capture values BEFORE entering Task.Run
var steamId = player.SteamID;
var playerName = player.PlayerName;

_ = Task.Run(async () =>
{
    try { await SavePlayerDataAsync(steamId); }
    catch (Exception ex) { logger.LogError(ex, "Failed to save data for {SteamId}", steamId); }
});
```

> **IMPORTANT**: Always capture primitive values (SteamID, slot numbers, names) before `Task.Run`. Never access `IPlayer` or game objects inside background tasks.

### Scheduler Timer API

```csharp
// One-shot delay
Core.Scheduler.DelayBySeconds(5, () => { /* runs once after 5s */ });

// Repeating timer
var timer = Core.Scheduler.RepeatBySeconds(1.0f, () => { /* runs every 1s */ });

// Delay then repeat
var timer = Core.Scheduler.DelayAndRepeatBySeconds(initialDelay, interval, callback);

// Stop timer on map change
Core.Scheduler.StopOnMapChange(timer);

// Cancel manually
timer?.Cancel();  // CancellationTokenSource
```

---

## 10. Data Management & Optimization

### ConcurrentDictionary for Thread-Safe Caches

```csharp
// Per-player data cache
private readonly ConcurrentDictionary<ulong, PlayerData> _playerCache = new();

// Nested concurrent structures for complex data
private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, decimal>> _playerBalances = new();

// Per-player locks for fine-grained synchronization
private readonly ConcurrentDictionary<ulong, object> _playerLocks = new();
private object GetPlayerLock(ulong steamId) => _playerLocks.GetOrAdd(steamId, _ => new object());
```

### Dirty Tracking + Save Queue Pattern

Only write to database when data has actually changed:

```csharp
private readonly ConcurrentDictionary<ulong, bool> _dirtyPlayers = new();
private readonly HashSet<ulong> _saveQueue = new();
private readonly object _saveQueueLock = new();

private void MarkDirty(ulong steamId) => _dirtyPlayers[steamId] = true;
private bool IsDirty(ulong steamId) => _dirtyPlayers.ContainsKey(steamId);
private void ClearDirty(ulong steamId) => _dirtyPlayers.TryRemove(steamId, out _);

internal void EnqueueSave(ulong steamId)
{
    lock (_saveQueueLock) { _saveQueue.Add(steamId); }  // HashSet = idempotent
}
```

### Delta-Based Database Saves

Only write the difference, not the full state:

```csharp
private bool SaveDataInternal(ulong steamId)
{
    if (!IsDirty(steamId)) return false;  // Skip clean players

    foreach (var (key, currentValue) in playerData)
    {
        var delta = currentValue - initialValues[key];
        if (delta != 0)
        {
            var dbValue = repository.GetValue(steamId, key);
            repository.Upsert(steamId, key, dbValue + delta);
            initialValues[key] = dbValue + delta;
        }
    }
    ClearDirty(steamId);
    return true;
}
```

### Periodic Save Queue Processor

```csharp
private void StartSaveQueueProcessor()
{
    _saveTaskCts = Core.Scheduler.RepeatBySeconds(saveInterval, () =>
    {
        Task.Run(() =>
        {
            while (TryDequeueSave(out var steamId))
                SaveData(steamId);
        });
    });
}
```

### Batch Saves at Round End

```csharp
[GameEventHandler(HookMode.Post)]
public HookResult OnRoundEnd(EventRoundEnd @event)
{
    _ = Task.Run(async () =>
    {
        try { await SaveAllOnlinePlayersAsync(); }
        catch (Exception ex) { logger.LogError(ex, "Failed to batch save on round end"); }
    });
    return HookResult.Continue;
}
```

### TTL-Based Cache

```csharp
private const int CACHE_TTL_SECONDS = 300;
private readonly ConcurrentDictionary<int, (MyData Data, DateTime ExpiresAt)> _cache = new();

public async Task<MyData?> GetDataAsync(int id)
{
    if (_cache.TryGetValue(id, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        return cached.Data;

    var data = await repository.GetAsync(id);
    if (data != null)
        _cache[id] = (data, DateTime.UtcNow.AddSeconds(CACHE_TTL_SECONDS));
    return data;
}
```

### Player Cleanup on Disconnect

Always clean up cached data when a player disconnects:

```csharp
private void OnPlayerDisconnect(ulong steamId)
{
    // Save first, then remove
    _ = Task.Run(async () =>
    {
        await SavePlayerDataAsync(steamId);
        _playerCache.TryRemove(steamId, out _);
        _playerBalances.TryRemove(steamId, out _);
        _playerLocks.TryRemove(steamId, out _);
        ClearDirty(steamId);
    });
}
```

### Ordered Lock Acquisition (Prevent Deadlocks)

```csharp
// When locking multiple players (e.g., transfers), always lock in consistent order
var (firstLock, secondLock) = fromSteamId < toSteamId
    ? (GetPlayerLock(fromSteamId), GetPlayerLock(toSteamId))
    : (GetPlayerLock(toSteamId), GetPlayerLock(fromSteamId));

lock (firstLock)
{
    lock (secondLock)
    {
        // Atomic operation across both players
    }
}
```

### Reflection Caching

```csharp
private static readonly ConcurrentDictionary<Type, PropertyInfo?> _propertyCache = new();

var prop = _propertyCache.GetOrAdd(typeof(T), static type =>
    type.GetProperty("Accessor"));
```

---

## 11. Database Patterns

### Getting a Connection

```csharp
// Named connections defined in database.jsonc
var connection = Core.Database.GetConnection("default");
var connection = Core.Database.GetConnection(cfg.DatabaseConnection);

// Always use `using` for disposal
using var connection = Core.Database.GetConnection(cfg.DatabaseConnection);
```

### FluentMigrator Setup

```csharp
// db/Runner.cs
public class MigrationRunner
{
    public static void RunMigrations(IDbConnection dbConnection)
    {
        var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb =>
            {
                ConfigureDatabase(rb, dbConnection);
                rb.WithGlobalConnectionString(dbConnection.ConnectionString)
                  .ScanIn(typeof(MigrationRunner).Assembly).For.Migrations();
            })
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .BuildServiceProvider(false);

        using var scope = serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    private static void ConfigureDatabase(IMigrationRunnerBuilder rb, IDbConnection conn)
    {
        switch (conn)
        {
            case MySqlConnection: rb.AddMySql5(); break;
            case NpgsqlConnection: rb.AddPostgres(); break;
            case SQLiteConnection: rb.AddSQLite(); break;
            default: throw new NotSupportedException($"Unsupported DB: {conn.GetType().Name}");
        }
    }
}
```

### Migration Example

```csharp
[Migration(2026042001, "Initialize player data table")]
public class InitPlayerTable : Migration
{
    public override void Up()
    {
        if (!Schema.Table("my_plugin_players").Exists())
        {
            Create.Table("my_plugin_players")
                .WithColumn("id").AsInt32().PrimaryKey().Identity()
                .WithColumn("steam_id").AsInt64().NotNullable()
                .WithColumn("data").AsString(255).NotNullable().WithDefaultValue("")
                .WithColumn("created_at").AsDateTime().NotNullable()
                    .WithDefault(SystemMethods.CurrentDateTime);

            Create.Index("idx_my_plugin_steam").OnTable("my_plugin_players")
                .OnColumn("steam_id").Ascending().WithOptions().Unique();
        }
    }

    public override void Down() => Delete.Table("my_plugin_players");
}
```

### Run Migrations in Constructor

```csharp
public MyPlugin(ISwiftlyCore core) : base(core)
{
    LoadConfiguration();
    using var connection = core.Database.GetConnection(cfg.DatabaseConnection);
    MigrationRunner.RunMigrations(connection);
}
```

### Database Packages

```xml
<!-- Required for database plugins -->
<PackageReference Include="Dapper" Version="*" />
<PackageReference Include="FluentMigrator" Version="*" />
<PackageReference Include="FluentMigrator.Runner" Version="*" />
<PackageReference Include="FluentMigrator.Runner.MySql" Version="*" />
<PackageReference Include="FluentMigrator.Runner.Postgres" Version="*" />
<PackageReference Include="FluentMigrator.Runner.SQLite" Version="*" />
```

---

## 12. Menus & UI

### Building a Menu

```csharp
var menu = Core.MenusAPI.CreateBuilder()
    .Design.SetMenuTitle("My Menu")
    .Design.SetMenuTitleVisible(true)
    .Design.SetMenuFooterVisible(true)
    .EnableSound()
    .EnableExit()                    // Allow closing via back button
    .SetPlayerFrozen(false)          // Don't freeze player while menu is open
    .SetAutoCloseDelay(0)            // 0 = no auto-close
    .Build();

// Add button options
var button = new ButtonMenuOption("Click Me");
button.Click += (sender, args) =>
{
    var player = args.Player;
    player.SendChat("You clicked!");
    return ValueTask.CompletedTask;
};
menu.AddOption(button);

// Display-only (disabled) options
var info = new ButtonMenuOption("Score: 100");
info.Enabled = false;
menu.AddOption(info);

// Open for player
Core.MenusAPI.OpenMenuForPlayer(player, menu);
```

### Sub-Menus

```csharp
var mainMenu = Core.MenusAPI.CreateBuilder()
    .Design.SetMenuTitle("Main")
    .EnableExit().EnableSound()
    .Build();

var subMenuOption = new ButtonMenuOption("Settings");
subMenuOption.Click += (sender, args) =>
{
    var subMenu = Core.MenusAPI.CreateBuilder()
        .Design.SetMenuTitle("Settings")
        .EnableExit().EnableSound()
        .BindToParent(mainMenu)  // Back button returns to mainMenu
        .Build();
    // ... add sub-menu options ...
    Core.MenusAPI.OpenMenuForPlayer(args.Player, subMenu);
    return ValueTask.CompletedTask;
};
mainMenu.AddOption(subMenuOption);
```

### Submenu Menu Option (Lazy-loaded)

```csharp
var submenu = new SubmenuMenuOption("Category", () =>
{
    var builder = Core.MenusAPI.CreateBuilder();
    builder.Design.SetMenuTitleVisible(false);
    // ... build dynamically ...
    return Task.FromResult(builder.Build());
});
mainMenu.AddOption(submenu);
```

### Close Menu

```csharp
Core.MenusAPI.CloseMenuForPlayer(player, menu);
var currentMenu = Core.MenusAPI.GetCurrentMenu(player);
```

### Updating Player Clan Tags (Scoreboard)

Changing a player's clan tag requires three steps — setting the value, notifying the engine, and forcing a scoreboard refresh:

```csharp
using SwiftlyS2.Shared.GameEventDefinitions;

player.Controller.Clan = newClanTag;
player.Controller.ClanUpdated();
if (Core.GameEvent.IsListeningToEvent<EventNextlevelChanged>(player.PlayerID))
    Core.GameEvent.FireToPlayerAsync<EventNextlevelChanged>(player.PlayerID);
```

> **All three steps are required.** Without `ClanUpdated()` + `EventNextlevelChanged` fire, the scoreboard will not reflect the change until the next natural refresh.

---

## 13. Player Validation

### Standard Validation Pattern

Always validate before any player operation:

```csharp
if (player == null || !player.IsValid || player.IsFakeClient) return;
```

### Helper Methods

```csharp
internal bool IsPlayerValid(IPlayer? player)
{
    return player != null && player.IsValid;
}

private bool IsValidLoaded(IPlayer? player)
{
    if (player == null || !player.IsValid || player.IsFakeClient)
        return false;
    return _playerCache.ContainsKey(player.SteamID);
}
```

### Safe Player Execution

```csharp
public static void ExecuteOnPlayer(ISwiftlyCore core, ulong steamId, Action<IPlayer> action)
{
    core.Scheduler.NextTick(() =>
    {
        var player = core.PlayerManager.GetAllPlayers()
            .FirstOrDefault(x => x.IsValid && x.SteamID == steamId);
        if (player != null)
            action(player);
    });
}
```

### Common LINQ Patterns

```csharp
// All valid non-bot players
Core.PlayerManager.GetAllValidPlayers()
    .Where(p => p.IsValid && !p.IsFakeClient)
    .ToList();

// Find by SteamID
var target = Core.PlayerManager.GetAllPlayers()
    .FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);

// Hot reload — load existing players
if (hotReload)
{
    foreach (var player in Core.PlayerManager.GetAllValidPlayers())
    {
        if (!player.IsFakeClient)
            Task.Run(() => LoadPlayerDataAsync(player));
    }
}
```

---

## 14. Plugin Lifecycle

### Lifecycle Order

```
1. Constructor(ISwiftlyCore core)     → Config loading, DB migrations
2. ConfigureSharedInterface()          → Register your APIs for other plugins
3. UseSharedInterface()                → Consume other plugins' APIs
4. Load(bool hotReload)                → Main initialization, events, commands
5. OnSharedInterfaceInjected()         → Post-injection setup (all APIs ready)
6. [Runtime]                           → Plugin is active
7. Unload()                            → Cleanup, unregister listeners, cancel timers
```

### Full Lifecycle Example

```csharp
[PluginMetadata(Id = "MyPlugin", Version = "1.0.0", Name = "My Plugin",
    Author = "Shmitzas", Description = "Description")]
public partial class MyPlugin : BasePlugin
{
    public static new ISwiftlyCore Core { get; private set; } = null!;
    private ILogger<MyPlugin> logger = null!;
    private Config cfg = null!;
    private CancellationTokenSource? _timerCts;
    private IEconomyAPIv1? _economyApi;

    public MyPlugin(ISwiftlyCore core) : base(core)
    {
        LoadConfig();
        // DB migrations if needed
    }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
        // Register API if this plugin exposes one
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.HasSharedInterface("Economy.API.v1"))
            _economyApi = interfaceManager.GetSharedInterface<IEconomyAPIv1>("Economy.API.v1");
    }

    public override void Load(bool hotReload)
    {
        Core = base.Core;
        RegisterListeners();
        RegisterCommands();

        _timerCts = Core.Scheduler.RepeatBySeconds(30, OnTimerTick);

        if (hotReload)
        {
            foreach (var p in Core.PlayerManager.GetAllValidPlayers())
                if (!p.IsFakeClient) Task.Run(() => LoadPlayerData(p));
        }
    }

    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        // All shared interfaces are now available
    }

    public override void Unload()
    {
        UnregisterListeners();   // Core.Event -= handlers
        _timerCts?.Cancel();     // Stop timers
        // Save any pending data
    }
}
```

---

## 15. Auditing & Bug Prevention

### Pre-Release Checklist

1. **Thread Safety Audit**
   - [ ] No `IPlayer`, `PlayerPawn`, or entity access inside `Task.Run` blocks
   - [ ] All game-state reads/writes happen on main thread (`NextTick`, `NextWorldUpdate`)
   - [ ] Primitive values captured BEFORE `Task.Run` (SteamID, names, slot numbers)
   - [ ] `ConcurrentDictionary` used for shared state, not `Dictionary`

2. **Memory Leak Audit**
   - [ ] All `Core.Event +=` have matching `-=` in `Unload()`
   - [ ] All `CancellationTokenSource` timers are cancelled in `Unload()`
   - [ ] Player data removed from caches on disconnect
   - [ ] No event handler references keeping objects alive

3. **Null Safety Audit**
   - [ ] Player validated (`!= null && IsValid && !IsFakeClient`) before every operation
   - [ ] Re-validate player after `NextTick`/`NextWorldUpdate` (player may have disconnected)
   - [ ] Shared interfaces checked with `HasSharedInterface` before `GetSharedInterface`
   - [ ] Config values have sensible defaults

4. **Database Audit**
   - [ ] Connections use `using` for proper disposal
   - [ ] Migrations check `Schema.Table().Exists()` before creating
   - [ ] All DB operations are async and off main thread
   - [ ] Indexes on frequently queried columns

5. **Logging Audit**
   - [ ] All `LogInformation`/`LogDebug` wrapped in `if (cfg.DetailedLogging)`
   - [ ] `LogWarning`/`LogError` are NEVER behind `DetailedLogging`
   - [ ] Structured logging placeholders used (not string interpolation)
   - [ ] Exception objects passed to `LogError(ex, ...)` not just `ex.Message`

6. **Translation Audit**
   - [ ] Both `en.jsonc` and `lt.jsonc` files exist and have matching keys
   - [ ] All user-facing strings use localization (no hardcoded text in SendChat)
   - [ ] Color tags properly closed with `[default]`

7. **Performance Audit**
   - [ ] No synchronous DB calls on main thread
   - [ ] `OnTick` handlers are minimal (no allocations, no LINQ, no DB calls)
   - [ ] Reflection results cached in static `ConcurrentDictionary`
   - [ ] Save operations use dirty tracking (skip unchanged data)

### Common Bugs to Watch For

```csharp
// BUG: Accessing player inside Task.Run
Task.Run(async () => {
    var name = player.PlayerName;  // WRONG — thread-unsafe!
});

// FIX: Capture before Task.Run
var name = player.PlayerName;
var steamId = player.SteamID;
Task.Run(async () => {
    await SaveAsync(steamId, name);  // CORRECT — primitive values
});

// BUG: Missing re-validation after scheduler callback
Core.Scheduler.NextTick(() => {
    player.SendChat("Hello");  // WRONG — player may have disconnected!
});

// FIX: Re-validate
Core.Scheduler.NextTick(() => {
    if (player == null || !player.IsValid) return;
    player.SendChat("Hello");
});

// BUG: Not unregistering Core.Event listener
Core.Event.OnTick += MyTickHandler;
// Missing: Core.Event.OnTick -= MyTickHandler; in Unload()

// BUG: Dictionary (not concurrent) accessed from multiple threads
private Dictionary<ulong, int> _scores = new();  // WRONG
// FIX:
private ConcurrentDictionary<ulong, int> _scores = new();  // CORRECT
```

---

## 16. Using SwiftlyS2 Docs & CS2 Schema Dumps via @sd MCP

The `@sd` MCP server provides access to SwiftlyS2 API documentation and CS2 schema dumps.

### Available Documentation Categories

| Category | Contents |
|----------|----------|
| `swiftlys2` | ~5658 API docs (classes, interfaces, methods, events) |
| `swiftlys2-plugins` | Plugin-specific docs (MixScrims, ShopCore, Cookies, K4-LevelRanks, Audio) |
| `counter-strike-2` | CS2 schema dumps (~4009 files from DumpSource2) |
| `web-projects` | API & web docs (Rampage API, Stripe, Paysera, SEO) |

### How to Use @sd

#### Step 1: Discover Categories
```
@sd list_documentation_categories
```

#### Step 2: Browse a Category
```
@sd browse_category("swiftlys2")
@sd browse_category("counter-strike-2/DumpSource2")
```

#### Step 3: Search Documentation
Use **single short keywords** or **type/class names** (exact substring matching):
```
@sd search_documentation("ICommandContext")     ← GOOD
@sd search_documentation("BasePlugin")          ← GOOD
@sd search_documentation("database")            ← GOOD
@sd search_documentation("EventPlayerDeath")    ← GOOD
@sd search_documentation("CCSPlayerController") ← GOOD (CS2 schema)
```

> **IMPORTANT**: Do NOT use long natural-language phrases. Use single keywords or type names.

#### Step 4: Read a Document
Use the **exact path** from browse/search results:
```
@sd get_document("swiftlys2/docs-api-plugins-baseplugin.md")
```

> **NEVER** guess document paths. Always get them from `browse_category` or `search_documentation`.

### Common Lookups

| Need | Search Query |
|------|-------------|
| Plugin base class | `BasePlugin` |
| Command context | `ICommandContext` |
| Game events | `GameEventHandler` or specific event like `EventPlayerDeath` |
| Player interface | `IPlayer` |
| Scheduler | `IScheduler` |
| Database | `IDatabase` |
| Configuration | `IConfiguration` |
| ConVars | `IConVar` |
| Menus | `IMenuAPI` |
| CS2 entity schemas | Browse `counter-strike-2/DumpSource2` |

### CS2 Schema Dumps

The `counter-strike-2/DumpSource2` subcategory contains ~4009 schema files. Use these to:
- Find entity property names and types
- Understand CS2 class hierarchies (CCSPlayerController, CCSPlayerPawn, etc.)
- Look up weapon definitions, game rules, and engine classes

---

## 17. CI/CD & Deployment

### Build & Deploy Workflow

Plugins use GitHub Actions for automated deployment:

1. **Initialize**: `initialize-plugin.yaml` auto-scaffolds from template using `dotnet new swplugin`
2. **Build**: `dotnet publish -c Release`
3. **Deploy**: SFTP upload to game servers via `update-plugin-*.yaml`

### Deployment Target

```
/game/csgo/addons/swiftlys2/plugins/{PluginName}/
```

### Required GitHub Secrets

| Secret | Purpose |
|--------|---------|
| `SFTP_SERVERS_CONFIG` | JSON config mapping server names to SFTP hosts |
| `SFTP_PASSWORD_PROD` | Production server SFTP password |
| `SFTP_PASSWORD_TEST` | Test server SFTP password |

### Server Targets

`test`, `test2`, `public`, `mix1`, `mix2`, `awp`, `deagle`, `retake`, `surfgunxp`, `1v1`, `forfun`

---

## Quick Reference: File Structure

```
MyPlugin/
├── .github/
│   ├── copilot-instructions.md          ← This file (shared across plugins)
│   └── workflows/
│       ├── initialize-plugin.yaml
│       ├── update-plugin-all-servers.yaml
│       └── update-plugin-selected-servers.yaml
├── src/
│   ├── Main.cs                          ← Plugin class, Load/Unload, config loading
│   ├── Shared/
│   │   ├── Config.cs                    ← Config model class
│   │   ├── RampageApiConfig.cs          ← (if using Rampage API)
│   │   └── Hooks.cs                     ← Event handlers (optional split)
│   ├── Commands/                        ← Command handlers (optional split)
│   ├── Services/                        ← Business logic services
│   └── Database/                        ← DB migrations, repositories
│       ├── Runner.cs                    ← FluentMigrator runner
│       └── Migrations/
│           └── InitTable.cs
├── resources/
│   └── translations/
│       ├── en.jsonc                     ← English translations (REQUIRED)
│       └── lt.jsonc                     ← Lithuanian translations (REQUIRED)
├── MyPlugin.csproj
├── MyPlugin.sln
└── README.md
```
