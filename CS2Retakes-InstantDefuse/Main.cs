using System.Numerics;
using CS2Retakes_InstantDefuse.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Extensions.GameEventManager;
using Sharp.Shared;
using Sharp.Shared.Abstractions;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.GameEvents;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Units;

namespace CS2Retakes_InstantDefuse;

public class Main : IModSharpModule, IGameListener
{
    private readonly ISharedSystem _sharedSystem;
    private readonly IServiceProvider _provider;
    private readonly IGameEventManager _gameEventManager;

    public string DisplayName => "CS2Retakes - Instant Defuse";
    public string DisplayAuthor => "Xc_ace";
    
    int IGameListener.ListenerVersion  => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;
    
    private readonly Output _output = new ($"[{ChatColor.Green}Retakes{ChatColor.White}]");

    private float _bombPlantedTime = float.NaN;
    private bool _bombTicking;
    private int _molotovThreat;
    private int _heThreat;

    private List<int> _infernoThreat = [];

    public Main(ISharedSystem sharedSystem, string dllPath, string sharpPath, Version version,
        IConfiguration configuration, bool hotReload)
    {
        _sharedSystem = sharedSystem;
        Config.SharedSystem = sharedSystem;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sharedSystem);
        services.AddGameEventManager();

        _provider = services.BuildServiceProvider();
        _gameEventManager = _provider.GetRequiredService<IGameEventManager>();
    }

    public bool Init()
    {
        _provider.LoadAllSharpExtensions();
        
        _sharedSystem.GetModSharp().InstallGameListener(this);
        
        _gameEventManager.ListenEvent("grenade_thrown", OnGrenadeThrown);
        _gameEventManager.ListenEvent("inferno_startburn", OnInfernoStartburn);
        _gameEventManager.ListenEvent("inferno_extinguish", OnInfernoExtinguish);
        _gameEventManager.ListenEvent("inferno_expire", OnInfernoExpire);
        _gameEventManager.ListenEvent("hegrenade_detonate", OnHeGrenadeDetonate);
        _gameEventManager.ListenEvent("molotov_detonate", OnMolotovDetonate);
        _gameEventManager.ListenEvent("bomb_planted", OnBombPlanted);
        _gameEventManager.ListenEvent("bomb_begindefuse", OnBombBeginDefuse);
        
        return true;
    }

    public void Shutdown()
    {
        _provider.ShutdownAllSharpExtensions();
        
        _sharedSystem.GetModSharp().RemoveGameListener(this);
    }

    private void OnGrenadeThrown(IGameEvent ev)
    {
        if (ev is not IEventGrenadeThrown e)
            return;

        var weapon = e.Grenade;

        if (weapon == "hegrenade")
            _heThreat++;
        else if (weapon is "molotov" or "incgrenade")
            _molotovThreat++;
    }

    private void OnInfernoStartburn(IGameEvent e)
    {
        var infernoPos = new Vector3(e.GetFloat("X"), e.GetFloat("Y"), e.GetFloat("Z"));

        var plantedBomb = FindPlantedBomb();
        if (plantedBomb is null)
            return;

        var plantedBombVector = plantedBomb.GetAbsOrigin();
        var plantedBombVector3 = new Vector3(plantedBombVector.X, plantedBombVector.Y, plantedBombVector.Z);

        var distance = Vector3.Distance(infernoPos, plantedBombVector3);

        if (distance > 250)
            return;
        
        _infernoThreat.Add(e.GetInt("EntityId"));
    }

    private void OnInfernoExtinguish(IGameEvent e)
    {
        _infernoThreat.Remove(e.GetInt("EntityId"));
    }

    private void OnInfernoExpire(IGameEvent e)
    {
        _infernoThreat.Remove(e.GetInt("EntityId"));
    }

    private void OnHeGrenadeDetonate(IGameEvent e)
    {
        if (_heThreat > 0)
            _heThreat--;
    }

    private void OnMolotovDetonate(IGameEvent e)
    {
        if (_molotovThreat > 0)
            _molotovThreat--;
    }

    private void OnBombPlanted(IGameEvent e)
    {
        _bombPlantedTime = _sharedSystem.GetModSharp().GetGlobals().CurTime;
        _bombTicking = true;
    }

    private void OnBombBeginDefuse(IGameEvent ev)
    {
        if (ev is not IEventBombBeginDefuse e)
            return;

        var player = e.UserId;

        AttemptInstantDefuse(player);
    }

    private void AttemptInstantDefuse(UserID player)
    {
        if (!_bombTicking)
            return;
        
        var defuser = _sharedSystem.GetClientManager().GetGameClient(player);
        if (defuser is null)
            return;
        
        var plantedBomb = FindPlantedBomb();
        if (plantedBomb is null)
            return;
        
        if (plantedBomb.GetNetVar<bool>("m_bCannotBeDefused"))
            return;
        
        if (TeamHasAlivePlayers(CStrikeTeam.TE))
            return;
        
        if (_heThreat > 0 || _molotovThreat > 0 || _infernoThreat.Count != 0)
        {
            _output.PrintToChatAll("附近有雷、火，无法立即拆包！");
            return;
        }
        
        var bombTimeUntilDetonation = plantedBomb.GetNetVar<float>("m_flTimerLength") -
                                      (_sharedSystem.GetModSharp().GetGlobals().CurTime - _bombPlantedTime);
        var defuseLength = plantedBomb.GetNetVar<float>("m_flDefuseLength");
        if ((int)defuseLength != 5 && (int)defuseLength != 10)
            defuseLength = _sharedSystem.GetEntityManager().FindPlayerPawnBySlot(defuser.Slot)?.GetItemService()?
                .HasDefuser ?? false
                ? 5.0f
                : 10.0f;

        var timeLeftAfterDefuse = bombTimeUntilDetonation - defuseLength;
        var bombCanBeDefusedTime = timeLeftAfterDefuse >= 0.0f;

        if (!bombCanBeDefusedTime)
        {
            _output.PrintToChatAll($"拆包失败，还需要{ChatColor.Red}{Math.Abs(timeLeftAfterDefuse):n3}{ChatColor.White}秒才能拆除.");
            plantedBomb.SetNetVar("m_flC4Blow", 1.0f);
            
            return;
        }
        
        _sharedSystem.GetModSharp().InvokeFrameAction(() =>
        {
            plantedBomb.SetNetVar("m_flDefuseCountDown", 0.0f);
            _output.PrintToChatAll($"炸弹已被拆除，还剩下{ChatColor.Green}{Math.Abs(bombTimeUntilDetonation):n3}{ChatColor.White}秒.");
        });
    }

    private IBaseEntity? FindPlantedBomb()
    {
        return _sharedSystem.GetEntityManager().FindEntityByClassname(null, "planted_c4");
    }

    private bool TeamHasAlivePlayers(CStrikeTeam team)
    {
        var clients = _sharedSystem.GetModSharp().GetIServer().GetGameClients();

        foreach (var client in clients)
        {
            var pawn = _sharedSystem.GetEntityManager().FindPlayerPawnBySlot(client.Slot);
            if (pawn is null)
                continue;

            if (pawn.IsAlive && pawn.Team == team)
                return true;
        }

        return false;
    }

    public void OnRoundRestart()
    {
        _bombPlantedTime = float.NaN;
        _bombTicking = false;

        _heThreat = 0;
        _molotovThreat = 0;
        _infernoThreat = [];
    }
}