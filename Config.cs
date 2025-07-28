using CounterStrikeSharp.API.Modules.Utils;

namespace GameflowControl;

public sealed class Config
{
    private static readonly Config _instance = new Config();
    public static Config Instance => _instance;
    public bool readySystemEnabled { get; set; } = true;

    public bool knifeRoundInProgress { get; set; } = false;
    public bool waitingForSideChoice { get; set; } = false;
    public CsTeam knifeRoundWinner { get; set; } = CsTeam.None; // 2 = T, 3 = CT
    public CounterStrikeSharp.API.Modules.Timers.Timer? sideChoiceTimer { get; set; }

    public readonly Dictionary<string, Dictionary<string, int>> damageMatrix = new();
    public readonly Dictionary<string, Dictionary<string, int>> hitMatrix = new();
    public readonly Dictionary<int, bool> playerReady = new();

    // --- Command lists ---
    public readonly string[] ReadyCommands = [".ready", ".r"];
    public readonly string[] NotReadyCommands = [".notready", ".nr"];
    public readonly string[] PauseCommands = [".pause", ".p"];
    public readonly string[] UnpauseCommands = [".unpause", ".unp"];
    public readonly string[] SwitchCommands = [".switch"];
    public readonly string[] StayCommands = [".stay"];
    public readonly string[] StatusCommands = [".status"];
    public readonly string[] DebugCommands = [".debug"];
    public readonly string[] DamageCommands = [".damage"];

}