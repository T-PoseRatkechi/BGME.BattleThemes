﻿using BGME.BattleThemes.Configuration;
using BGME.BattleThemes.Interfaces;
using BGME.BattleThemes.Template;
using BGME.BattleThemes.Themes;
using BGME.Framework.Interfaces;
using PersonaModdingMetadata.Shared.Games;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Diagnostics;

namespace BGME.BattleThemes;

public class Mod : ModBase, IExports
{
    private readonly IModLoader modLoader;
    private readonly IReloadedHooks? hooks;
    private readonly ILogger logger;
    private readonly IMod owner;
    private Config configuration;
    private readonly IModConfig modConfig;

    private readonly Game game;
    private readonly BattleThemesService? battleThemesService;

    public Mod(ModContext context)
    {
        this.modLoader = context.ModLoader;
        this.hooks = context.Hooks;
        this.logger = context.Logger;
        this.owner = context.Owner;
        this.configuration = context.Configuration;
        this.modConfig = context.ModConfig;

#if DEBUG
        Debugger.Launch();
#endif

        Log.Logger = this.logger;
        Log.LogLevel = this.configuration.LogLevel;

        this.modLoader.GetController<IBgmeApi>().TryGetTarget(out var bgme);

        try
        {
            this.game = this.GetGame();

            if (game == Game.P5R_PC && this.configuration.BaseBgmId_P5R == 12000)
            {
                this.configuration.BaseBgmId_P5R = 4000;
                Log.Information("P5R: Base BGM ID updated from 12000 to 4000.");
            }

            else if (game == Game.P4G_PC && this.configuration.BaseBgmId_P4G == 693)
            {
                this.configuration.BaseBgmId_P4G = 4000;
                Log.Information("P4G: Base BGM ID updated from 693 to 4000.");
            }

            var modDir = modLoader.GetDirectoryForModId(this.modConfig.ModId);
            var musicRegistry = new MusicRegistry(this.game, bgme!, this.configuration, modDir, this.modLoader.GetAppConfig().EnabledMods);
            this.battleThemesService = new(this.modLoader, bgme!, musicRegistry);
            this.modLoader.AddOrReplaceController<IBattleThemesApi>(this.owner, this.battleThemesService);

            var buildDir = Path.Join(modDir, "build");
            bgme!.BgmeModLoading?.Invoke(new(this.modConfig.ModId, buildDir));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start battle themes service.");
        }
    }

    private Game GetGame()
    {
        var appId = this.modLoader.GetAppConfig().AppId;
        if (appId.Contains("p3r"))
            return Game.P3R_PC;
        else if (appId.Contains("p5r"))
            return Game.P5R_PC;
        else if (appId.Contains("p4g"))
            return Game.P4G_PC;
        else if (appId.Contains("p3p"))
            return Game.P3P_PC;
        else if (appId.Contains("metaphor"))
            return Game.Metaphor;

        throw new Exception($"Unknown game: {appId}");
    }

    #region Standard Overrides
    public override void ConfigurationUpdated(Config configuration)
    {
        // Apply settings from configuration.
        // ... your code here.
        this.configuration = configuration;
        logger.WriteLine($"[{modConfig.ModId}] Config Updated: Applying");
    }
    #endregion

    #region For Exports, Serialization etc.

    public Type[] GetTypes() => new[] { typeof(IBattleThemesApi) };

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public Mod() { }
#pragma warning restore CS8618
    #endregion
}
