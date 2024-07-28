using BGME.BattleThemes.Utils;
using BGME.Framework.Interfaces;
using PersonaModdingMetadata.Shared.Games;
using Phos.MusicManager.Library.Audio.Encoders;
using Phos.MusicManager.Library.Audio.Encoders.VgAudio;
using System.Text.Json;

namespace BGME.BattleThemes.Themes;

internal class MusicRegistry
{
    private const int CURRENT_VERSION = 2;

    private readonly Game game;
    private readonly Configuration.Config config;
    private readonly HashSet<ModSong> previousMusic;
    private readonly HashSet<ModSong> currentMusic = new();
    private readonly string[] supportedExts;
    private readonly string[] enabledMods;
    private readonly IEncoder encoder;

    private readonly string modDir;
    private readonly string modGameDir;

    public MusicRegistry(
        Game game,
        IBgmeApi bgme,
        Configuration.Config config,
        string modDir,
        string[] enabledMods)
    {
        this.game = game;
        this.config = config;
        this.enabledMods = enabledMods;

        this.modDir = modDir;
        this.modGameDir = Path.Join(modDir, game.ToString());

        this.encoder = GetEncoder();
        this.supportedExts = this.encoder.InputTypes;

        // Rebuild all music on new versions.
        if (this.IsNewVersion())
        {
            this.ResetMusic();
        }

        this.previousMusic = this.GetPreviousMusic();
        this.RegisterMusic();

        bgme.BgmeModLoading?.Invoke(new("BGME.BattleThemes", this.modGameDir));
    }

    /// <summary>
    /// Gets the list of songs added by the specified mod.
    /// </summary>
    /// <param name="modId">Mod ID to get songs for.</param>
    /// <returns>Array of songs.</returns>
    public ModSong[] GetModSongs(string modId) => this.currentMusic.Where(x => x.ModId == modId).ToArray();

    private void RegisterMusic()
    {
        var modsDir = Path.GetDirectoryName(this.modDir)!;
        foreach (var modDir in Directory.EnumerateDirectories(modsDir))
        {
            var modConfigFile = Path.Join(modDir, "ModConfig.json");
            if (!File.Exists(modConfigFile))
            {
                continue;
            }

            var modConfig = ReloadedConfigParser.Parse(modConfigFile);
            if (this.enabledMods.Contains(modConfig.ModId))
            {
                this.RegisterModMusic(modConfig.ModId, modDir);
            }
        }

        var activeBuildFiles = this.currentMusic.Select(x => x.BuildFilePath).ToArray();

        // Remove any files from songs that are not in current music
        // or whose build file path is no longer used.
        var unusedSongs = this.previousMusic
            .Except(this.currentMusic)
            .Where(x => activeBuildFiles.Contains(x.BuildFilePath) == false)
            .ToArray();

        foreach (var song in unusedSongs)
        {
            if (File.Exists(song.BuildFilePath))
            {
                File.Delete(song.BuildFilePath);
                Log.Debug($"Removed unused song file: {song.Name} || {song.BuildFilePath}");
            }
        }

        this.SaveCurrentMusic();
    }

    private void RegisterModMusic(string modId, string modDir)
    {
        var musicDir = Path.Join(modDir, "battle-themes", "music");
        if (!Directory.Exists(musicDir))
        {
            return;
        }

        var modSongs = Directory.GetFiles(musicDir, "*", SearchOption.AllDirectories)
            .Where(file => this.supportedExts
            .Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(file =>
            {
                var bgmId = this.GetNextBgmId();
                var buildFile = Path.Join(this.modGameDir, this.GetReplacementPath(bgmId));
                var song = new ModSong(modId, Path.GetFileNameWithoutExtension(file), bgmId, file, buildFile);
                this.currentMusic.Add(song);
                return song;
            })
            .ToArray();

        //Task.WhenAll(modSongs.Select(this.RegisterSong)).Wait();
        foreach (var song in modSongs)
        {
            this.RegisterSong(song).Wait();
        }
    }

    private async Task RegisterSong(ModSong song)
    {
        // Don't rebuild songs that haven't changed.
        if (this.previousMusic.Contains(song) && File.Exists(song.BuildFilePath))
        {
            Log.Debug($"Song already built: {song.Name}");
            return;
        }

        Log.Debug($"Building song: {song.FilePath}");

        var outputFile = new FileInfo(song.BuildFilePath);
        outputFile.Directory!.Create();

        await this.encoder.Encode(song.FilePath, outputFile.FullName);

        Log.Debug($"Built song: {song.BuildFilePath}");
        Log.Information($"Registered song: {song.Name} || Mod: {song.ModId} || BGM ID: {song.BgmId}");
    }

    private void SaveCurrentMusic()
    {
        var musicFileList = Path.Join(this.modGameDir, "music.json");
        File.WriteAllText(musicFileList, JsonSerializer.Serialize(this.currentMusic, new JsonSerializerOptions { WriteIndented = true }));

        var versionFile = Path.Join(this.modGameDir, "version.txt");
        File.WriteAllText(versionFile, CURRENT_VERSION.ToString());
    }

    private HashSet<ModSong> GetPreviousMusic()
    {
        var musicFileList = Path.Join(this.modGameDir, "music.json");
        if (File.Exists(musicFileList))
        {
            try
            {
                return JsonSerializer.Deserialize<HashSet<ModSong>>(File.ReadAllText(musicFileList)) ?? new();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse previous music.");
            }
        }

        return new();
    }

    private void ResetMusic()
    {
        Log.Information("New version, rebuilding all music.");

        var cachedFolder = Path.Join(this.modGameDir, "cached");
        if (Directory.Exists(cachedFolder))
        {
            foreach (var file in Directory.EnumerateFiles(cachedFolder, $"*{this.encoder.EncodedExt}"))
            {
                File.Delete(file);
                Log.Debug($"Cleared cached file: {file}");
            }
        }

        // Remove built files.
        var music = this.GetPreviousMusic();
        foreach (var song in music)
        {
            if (File.Exists(song.BuildFilePath))
            {
                File.Delete(song.BuildFilePath);
            }
        }

        var musicFile = Path.Join(this.modGameDir, "music.json");
        File.Delete(musicFile);
        Log.Debug($"Cleared music file: {musicFile}");
    }

    private bool IsNewVersion()
    {
        var versionFile = Path.Join(this.modGameDir, "version.txt");
        if (!File.Exists(versionFile))
        {
            return true;
        }

        try
        {
            var version = int.Parse(File.ReadAllText(versionFile));
            if (version == CURRENT_VERSION)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get saved version.");
            return true;
        }

        return true;
    }

    private string GetReplacementPath(int bgmId) => this.game switch
    {
        Game.P3P_PC => Path.Join("BGME", "P3P", $"{bgmId}.hca"),
        Game.P4G_PC => Path.Join("BGME", "P4G", $"{bgmId}.hca"),
        Game.P5R_PC => Path.Join("BGME", "P5R", $"{bgmId}.hca"),
        Game.P3R_PC => Path.Join("BGME", "P3R", $"{bgmId}.hca"),
        _ => throw new Exception("Unknown game."),
    };

    private int GetNextBgmId() => this.GetBaseBgmId() + this.currentMusic.Count;

    private int GetBaseBgmId() => this.game switch
    {
        Game.P3P_PC => this.config.BaseBgmId_P3P,
        Game.P4G_PC => this.config.BaseBgmId_P4G,
        Game.P5R_PC => this.config.BaseBgmId_P5R,
        Game.P3R_PC => this.config.BaseBgmId_P3R,
        _ => throw new Exception("Unknown game."),
    };

    private IEncoder GetEncoder()
    {
        var cachedDir = Directory.CreateDirectory(Path.Join(this.modGameDir, "cached")).FullName;

        return game switch
        {
            Game.P4G_PC => new CachedEncoder(new VgAudioEncoder(new() { OutContainerFormat = "hca" }), Directory.CreateDirectory(Path.Join(this.modDir, "P4G_P3P_cache")).FullName),
            Game.P3P_PC => new CachedEncoder(new VgAudioEncoder(new() { OutContainerFormat = "hca" }), Directory.CreateDirectory(Path.Join(this.modDir, "P4G_P3P_cache")).FullName),
            Game.P5R_PC => new CachedEncoder(new VgAudioEncoder(new() { OutContainerFormat = "hca", KeyCode = 9923540143823782 }), cachedDir),
            Game.P3R_PC => new CachedEncoder(new VgAudioEncoder(new() { OutContainerFormat = "hca", KeyCode = 11918920 }), cachedDir),
            _ => throw new Exception("Unknown game."),
        };
    }
}

internal record ModSong(string ModId, string Name, int BgmId, string FilePath, string BuildFilePath);