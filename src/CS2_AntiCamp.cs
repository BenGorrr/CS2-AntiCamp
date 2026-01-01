using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SwiftlyS2.Shared.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Sounds;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Events;

namespace CS2_AntiCamp;

[PluginMetadata(Id = "CS2_AntiCamp", Version = "1.0.0", Name = "CS2 Anti Camp", Author = "BenGorr", Description = "No description.")]
public partial class CS2_AntiCamp : BasePlugin
{
    // Configuration
    private PluginConfig _config = new();
    private IOptionsMonitor<PluginConfig> _configMonitor = null!;
    private IDisposable _configMonitorListener = null!;
	private Dictionary<ulong, CampingData> _playerCampingData = new();
	private CancellationTokenSource _checkAllPlayersCampingToken = new();

	private class CampingData
	{
		public Vector? CampStartPosition { get; set; }
		public DateTime CampStartTime { get; set; }
		public DateTime LastPunishTime { get; set; } = DateTime.MinValue;
	}

	public CS2_AntiCamp(ISwiftlyCore core) : base(core)
	{
	}

	public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
	{
	}

	public override void UseSharedInterface(IInterfaceManager interfaceManager)
	{
	}

	public override void Load(bool hotReload)
	{
        LoadConfiguration();
		_checkAllPlayersCampingToken = Core.Scheduler.RepeatBySeconds(1, () => {
			var gameRules = Core.EntitySystem.GetGameRules();
			if (gameRules is not null && gameRules is { FreezePeriod: false, WarmupPeriod: false } && _config.Enabled)
			{
				CheckAllPlayersCamping();
			}
		});
	}

	public void LoadConfiguration()
    {
        try
        {
            const string ConfigFileName = "config.jsonc";
            const string ConfigSection = "AntiCamp";
            
            string configPath = Core.Configuration.GetConfigPath(ConfigFileName);

            Core.Configuration.InitializeJsonWithModel<PluginConfig>(ConfigFileName, ConfigSection)
                .Configure(builder => builder.AddJsonFile(
                    configPath,
                    optional: false,
                    reloadOnChange: true));

            ServiceCollection services = new();
            services.AddSwiftly(Core)
                .AddOptionsWithValidateOnStart<PluginConfig>()
                .BindConfiguration(ConfigSection);

            var provider = services.BuildServiceProvider();

            _configMonitor = provider.GetRequiredService<IOptionsMonitor<PluginConfig>>();
            _config = _configMonitor.CurrentValue ?? new PluginConfig();

            _configMonitorListener = _configMonitor?.OnChange(cfg =>
            {
                _config = cfg ?? new PluginConfig();
                Core.Logger.LogInformation("Configuration reloaded.");
            })!;

            // Log loaded configuration with detailed array info
            Core.Logger.LogInformation($"Configuration loaded successfully");
            Core.Logger.LogInformation($"Plugin Enabled: {_config.Enabled}");
            Core.Logger.LogInformation($"Punish Cooldown Seconds: {_config.PunishCooldownSeconds}");
            Core.Logger.LogInformation($"Punish Damage: {_config.PunishDamage}");
            Core.Logger.LogInformation($"Max Camping Distance: {_config.MaxCampingDistance}");
            Core.Logger.LogInformation($"Min Camping Duration Seconds: {_config.MinCampingDurationSeconds}");
            Core.Logger.LogInformation($"Slap Sound: {_config.SlapSound}");
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Failed to load configuration file. Using default values.");
            _config = new PluginConfig();
        }
    }

	public override void Unload()
	{
		_checkAllPlayersCampingToken.Cancel();
        _configMonitorListener?.Dispose();
	}

	private void CheckPlayerCamping(IPlayer player)
	{
		var pawn = player.Pawn;
		var controller = player.Controller;
		if (player.IsFakeClient) 
			return;
		if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
			return;

		if (controller == null || !controller.IsValid)
			return;

		if (!_playerCampingData.TryGetValue(controller.SteamID, out var campingData))
		{
			// Initialize camping data for the player
			_playerCampingData[controller.SteamID] = new CampingData() {
				CampStartPosition = pawn.AbsOrigin,
				CampStartTime = DateTime.Now,
				LastPunishTime = DateTime.MinValue,
			};
			return;
		}
		if (pawn.AbsOrigin == null || campingData.CampStartPosition == null)
			return;

		// Check if player is on cooldown
		var timeSinceLastPunish = DateTime.Now - campingData.LastPunishTime;
		if (timeSinceLastPunish.TotalSeconds < _config.PunishCooldownSeconds)
			return;

		var distance = pawn.AbsOrigin.Value.Distance(campingData.CampStartPosition.Value);

		if (distance <= _config.MaxCampingDistance)
		{
			var campDuration = DateTime.Now - campingData.CampStartTime;
		
			if (campDuration.TotalSeconds >= _config.MinCampingDurationSeconds)
			{
				player.SendChat("Stop camping!");
				PunishPlayer(player);
				
				campingData.LastPunishTime = DateTime.Now;
				campingData.CampStartTime = DateTime.Now;
				return;
			}
			player.SendChat($"You are camping, you will be slapped in {Math.Max(1, (int)(_config.MinCampingDurationSeconds - campDuration.TotalSeconds))} seconds");
		} else {
			campingData.CampStartPosition = pawn.AbsOrigin;
			campingData.CampStartTime = DateTime.Now;
			// Core.Logger.LogInformation($"[AntiCamp] Player {controller.PlayerName} is not camping");
		}


	}

	private void CheckAllPlayersCamping()
	{
		var players = Core.PlayerManager.GetAlive().Where(p => p != null && p.IsValid).ToList();
		foreach (var player in players)
		{
			CheckPlayerCamping(player);
		}
	}
	
	[Command("actoggle", permission: "anti_camp.toggle")]
	public void OnToggleCommand(ICommandContext context)
	{
		_config.Enabled = !_config.Enabled;
		context.Reply($"AntiCamp toggled to {_config.Enabled}");
	}


    [EventListener<EventDelegates.OnClientDisconnected>]
    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
		var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
		if (player == null || player.Controller == null)
			return;
		_playerCampingData.Remove(player.Controller.SteamID);
    }
	private void PunishPlayer(IPlayer player)
	{
		var pawn = player.Pawn;
		if (pawn == null || pawn.Health <= 0)
			return;
		Random random = new();
		Vector vel = new(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, pawn.AbsVelocity.Z);

		vel.X += (random.Next(180) + 50) * (random.Next(2) == 1 ? -1 : 1);
		vel.Y += (random.Next(180) + 50) * (random.Next(2) == 1 ? -1 : 1);
		vel.Z += random.Next(200) + 100;

		pawn.Teleport(pawn.AbsOrigin, pawn.AbsRotation, vel);

		using var soundEvent = new SoundEvent() {
			Name = _config.SlapSound,
		};
		soundEvent.Volume = 0.7f;
		soundEvent.Recipients.AddRecipient(player.PlayerID);
		soundEvent.Emit();
	}
}