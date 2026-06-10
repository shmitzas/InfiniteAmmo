using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace InfiniteAmmo;

[PluginMetadata(Id = "InfiniteAmmo", Version = "1.0.0", Name = "Infinite Ammo", Author = "Shmitzas", Description = "Controls infinite reserve and chamber ammo per player flag.")]
public partial class InfiniteAmmo : BasePlugin
{
    public static new ISwiftlyCore Core { get; private set; } = null!;

    private ILogger<InfiniteAmmo> logger = null!;
    private Config cfg = null!;

    public InfiniteAmmo(ISwiftlyCore core) : base(core)
    {
    }

    private void LoadConfig()
    {
        const string fileName = "config.jsonc";
        const string section = "InfiniteAmmo";

        Core.Configuration
            .InitializeJsonWithModel<Config>(fileName, section)
            .Configure(builder =>
            {
                builder.AddJsonFile(
                    Core.Configuration.GetConfigPath(fileName),
                    optional: false,
                    reloadOnChange: true);
            });

        ServiceCollection services = new();
        services
            .AddSwiftly(Core, addLogger: true, addConfiguration: true)
            .AddOptionsWithValidateOnStart<Config>()
            .BindConfiguration(section);

        var provider = services.BuildServiceProvider();

        logger = provider.GetRequiredService<ILogger<InfiniteAmmo>>();
        cfg = provider.GetRequiredService<IOptions<Config>>().Value;
    }

    public override void Load(bool hotReload)
    {
        Core = base.Core;
        LoadConfig();

        if (cfg.InfiniteChamberAmmo)
        {
            Core.GameEvent.HookPost<EventWeaponFire>(OnWeaponFire);

            if (cfg.DetailedLogging)
                logger.LogInformation("Hooked EventWeaponFire for infinite chamber ammo.");
        }

        if (cfg.InfiniteReserveAmmo)
        {
            Core.GameEvent.HookPre<EventWeaponReload>(OnWeaponReload);

            if (cfg.DetailedLogging)
                logger.LogInformation("Started reserve ammo timer.");
        }
    }

    public override void Unload()
    {
    }

    // ── Chamber ammo (Clip1): restore after each shot ───────────────────────

    private HookResult OnWeaponFire(EventWeaponFire @event)
    {
        var player = @event.UserIdPlayer;
        if (player == null || !player.IsValid || player.IsFakeClient)
            return HookResult.Continue;

        if (!ShouldApplyChamberAmmo(player.SteamID))
            return HookResult.Continue;

        Core.Scheduler.NextWorldUpdate(() =>
        {
            if (player == null || !player.IsValid)
                return;

            var pawn = player.PlayerPawn;
            if (pawn?.WeaponServices == null)
                return;

            var weaponHandle = pawn.WeaponServices.ActiveWeapon;
            if (!weaponHandle.IsValid)
                return;

            var weapon = weaponHandle.Value;
            if (weapon == null)
                return;

            var maxClip1 = weapon.PlayerWeaponVData?.MaxClip1 ?? -1;
            if (maxClip1 <= 0)
                return;

            weapon.Clip1 = maxClip1;
            weapon.Clip1Updated();

            if (cfg.DetailedLogging)
                logger.LogInformation("Restored Clip1={MaxClip1} for player {SteamID}.", maxClip1, player.SteamID);
        });

        return HookResult.Continue;
    }

    // ── Reserve ammo: refill for all weapons ───────────────────────

    private HookResult OnWeaponReload(EventWeaponReload @event)
    {
        if (cfg.DetailedLogging)
            logger.LogInformation("Weapon reload event for player {SteamID}.", @event.UserIdPlayer?.SteamID);

        var player = @event.UserIdPlayer;

        if (player != null && player.IsAlive && player.IsFakeClient && player.Pawn != null)
        {
            HandleAmmo(player.Pawn);
            return HookResult.Continue;
        }

        if (player == null || !player.IsValid || !player.IsAlive)
        {
            if (cfg.DetailedLogging)
                logger.LogInformation("Skipping reserve ammo restore for player {SteamID} due to invalid player state.", player?.SteamID);
            return HookResult.Continue;
        }

        if (!ShouldApplyReserveAmmo(player.SteamID))
        {
            if (cfg.DetailedLogging)
                logger.LogInformation("Skipping reserve ammo restore for player {SteamID} due to missing permissions.", player.SteamID);
            return HookResult.Continue;
        }

        var pawn = player.Pawn;
        if (pawn?.WeaponServices == null){
            if (cfg.DetailedLogging)
                logger.LogInformation("Skipping reserve ammo restore for player {SteamID} due to missing pawn or weapon services.", player.SteamID);
            return HookResult.Continue;
        }

        HandleAmmo(pawn);
        
        return HookResult.Continue;
    }

    private void HandleAmmo(CBasePlayerPawn pawn)
    {
        if (pawn.WeaponServices == null)
            return;

        var player = Core.PlayerManager.GetPlayerFromPawn(pawn);

        foreach (var weapon in pawn.WeaponServices.MyValidWeapons)
        {
            if (cfg.DetailedLogging)
                logger.LogInformation("Checking weapon for player {SteamID}.", player?.SteamID);
            if (weapon == null){
                if (cfg.DetailedLogging)
                    logger.LogInformation("Skipping null weapon for player {SteamID}.", player?.SteamID);
                continue;
            }
            
            Core.Scheduler.NextWorldUpdate(() =>
            {
                if (weapon == null)
                    return;

                weapon.ReserveAmmo[0] = 9;
                weapon.ReserveAmmoUpdated();

                if (cfg.DetailedLogging)
                    logger.LogInformation("Restored ReserveAmmo for player {SteamID}.", player?.SteamID);
            });
        }
    }

    // ── Permission helpers ───────────────────────────────────────────────────

    private bool ShouldApplyChamberAmmo(ulong steamId)
    {
        if (!cfg.InfiniteChamberAmmo)
            return false;

        if (string.IsNullOrEmpty(cfg.InfiniteChamberAmmoFlag))
            return true;

        return Core.Permission.PlayerHasPermissions(steamId, [cfg.InfiniteChamberAmmoFlag]);
    }

    private bool ShouldApplyReserveAmmo(ulong steamId)
    {
        if (!cfg.InfiniteReserveAmmo)
            return false;

        if (string.IsNullOrEmpty(cfg.InfiniteReserveAmmoFlag))
            return true;

        return Core.Permission.PlayerHasPermissions(steamId, [cfg.InfiniteReserveAmmoFlag]);
    }
}
