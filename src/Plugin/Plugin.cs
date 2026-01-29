using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Translation;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4BombWires;

public enum WireColor
{
	Red,
	Blue,
	Yellow,
	Green,
	Max
}

public sealed class PluginConfig
{
	/// <summary>
	/// Timeout in seconds for the wire selection menu
	/// </summary>
	public int MenuTimeout { get; set; } = 5;
}

[PluginMetadata(Id = "k4.bombwires", Version = "1.0.1", Name = "K4 - Bomb Wires", Author = "K4ryuu", Description = "A bomb defuse minigame where CT must guess the correct wire color set by T.")]
public sealed class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
	public static new ISwiftlyCore Core { get; private set; } = null!;

	private PluginConfig _config = null!;
	private WireColor _currentWire = WireColor.Max;
	private IMenuAPI? _wireMenu;

	public override void Load(bool hotReload)
	{
		Core = base.Core;

		LoadConfiguration();
		RegisterEventHandlers();
	}

	public override void Unload()
	{
		// Nothing to clean up
	}

	private void LoadConfiguration()
	{
		const string ConfigFileName = "config.json";
		const string ConfigSection = "K4BombWires";

		Core.Configuration
			.InitializeJsonWithModel<PluginConfig>(ConfigFileName, ConfigSection)
			.Configure(cfg => cfg.AddJsonFile(Core.Configuration.GetConfigPath(ConfigFileName), optional: false, reloadOnChange: true));

		ServiceCollection services = new();
		services.AddSwiftly(Core)
			.AddOptionsWithValidateOnStart<PluginConfig>()
			.BindConfiguration(ConfigSection);

		var provider = services.BuildServiceProvider();
		_config = provider.GetRequiredService<IOptions<PluginConfig>>().Value;
	}

	private void RegisterEventHandlers()
	{
		Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
		Core.GameEvent.HookPost<EventRoundPrestart>(OnRoundPrestart);
		Core.GameEvent.HookPost<EventBombBeginplant>(OnBombBeginPlant);
		Core.GameEvent.HookPost<EventBombPlanted>(OnBombPlanted);
		Core.GameEvent.HookPost<EventBombBegindefuse>(OnBombBeginDefuse);
		Core.GameEvent.HookPost<EventBombDefused>(OnBombDefused);
		Core.GameEvent.HookPost<EventBombExploded>(OnBombExploded);
		Core.GameEvent.HookPost<EventBombAbortdefuse>(OnDefuseAborted);
		Core.GameEvent.HookPost<EventBombAbortplant>(OnPlantAborted);
	}

	private HookResult OnRoundStart(EventRoundStart @event)
	{
		_currentWire = WireColor.Max;
		return HookResult.Continue;
	}

	private HookResult OnRoundPrestart(EventRoundPrestart @event)
	{
		// Close wire menus for all players when round ends
		if (_wireMenu != null)
		{
			Core.MenusAPI.CloseMenu(_wireMenu);
			_wireMenu = null;
		}

		return HookResult.Continue;
	}

	private HookResult OnBombBeginPlant(EventBombBeginplant @event)
	{
		var player = @event.Accessor.GetPlayer("userid");
		if (player?.IsValid != true || player.IsFakeClient)
			return HookResult.Continue;

		ShowWireMenu(player, true);
		return HookResult.Continue;
	}

	private HookResult OnBombPlanted(EventBombPlanted @event)
	{
		var player = @event.Accessor.GetPlayer("userid");
		if (player?.IsValid != true || player.IsFakeClient)
			return HookResult.Continue;

		// If T didn't select a wire, pick random
		if (_currentWire == WireColor.Max)
		{
			_currentWire = (WireColor)Random.Shared.Next(0, (int)WireColor.Max);
		}

		return HookResult.Continue;
	}

	private HookResult OnBombBeginDefuse(EventBombBegindefuse @event)
	{
		var player = @event.Accessor.GetPlayer("userid");
		if (player?.IsValid != true || player.IsFakeClient)
			return HookResult.Continue;

		ShowWireMenu(player, false);
		return HookResult.Continue;
	}

	private HookResult OnBombDefused(EventBombDefused @event)
	{
		var player = @event.Accessor.GetPlayer("userid");
		ClearMenuIfValidPlayer(player);
		return HookResult.Continue;
	}

	private HookResult OnBombExploded(EventBombExploded @event)
	{
		var player = @event.Accessor.GetPlayer("userid");
		ClearMenuIfValidPlayer(player);
		return HookResult.Continue;
	}

	private HookResult OnDefuseAborted(EventBombAbortdefuse @event)
	{
		var player = @event.Accessor.GetPlayer("userid");
		ClearMenuIfValidPlayer(player);
		return HookResult.Continue;
	}

	private HookResult OnPlantAborted(EventBombAbortplant @event)
	{
		_currentWire = WireColor.Max;

		var player = @event.Accessor.GetPlayer("userid");
		ClearMenuIfValidPlayer(player);
		return HookResult.Continue;
	}

	private void ShowWireMenu(IPlayer player, bool planting)
	{
		var localizer = Core.Translation.GetPlayerLocalizer(player);

		// Helper message
		player.SendChatAsync($" {localizer["k4.general.prefix"]} {localizer[planting ? "k4.menu.helper_t" : "k4.menu.helper_ct"]}");

		var menuBuilder = Core.MenusAPI
			.CreateBuilder()
			.Design.SetMenuTitle(localizer["k4.menu.title"])
			.Design.SetMenuTitleVisible(true)
			.Design.SetMenuFooterVisible(true)
			.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
			.SetAutoCloseDelay(_config.MenuTimeout + 2)
			.SetSelectButton(KeyBind.Space)
			.SetPlayerFrozen(false);

		// Red wire
		var redBtn = new ButtonMenuOption($"<font color='#FF0000'>{localizer["k4.menu.red"]}</font>");
		redBtn.Click += (_, _) =>
		{
			HandleWireSelection(player, WireColor.Red, planting, localizer);
			return ValueTask.CompletedTask;
		};
		menuBuilder.AddOption(redBtn);

		// Blue wire
		var blueBtn = new ButtonMenuOption($"<font color='#00BFFF'>{localizer["k4.menu.blue"]}</font>");
		blueBtn.Click += (_, _) =>
		{
			HandleWireSelection(player, WireColor.Blue, planting, localizer);
			return ValueTask.CompletedTask;
		};
		menuBuilder.AddOption(blueBtn);

		// Yellow wire
		var yellowBtn = new ButtonMenuOption($"<font color='#FFFF00'>{localizer["k4.menu.yellow"]}</font>");
		yellowBtn.Click += (_, _) =>
		{
			HandleWireSelection(player, WireColor.Yellow, planting, localizer);
			return ValueTask.CompletedTask;
		};
		menuBuilder.AddOption(yellowBtn);

		// Green wire
		var greenBtn = new ButtonMenuOption($"<font color='#00FF00'>{localizer["k4.menu.green"]}</font>");
		greenBtn.Click += (_, _) =>
		{
			HandleWireSelection(player, WireColor.Green, planting, localizer);
			return ValueTask.CompletedTask;
		};
		menuBuilder.AddOption(greenBtn);

		_wireMenu = menuBuilder.Build();
		Core.MenusAPI.OpenMenuForPlayer(player, _wireMenu);

		// Auto-close menu after timeout and select random if T didn't pick
		Core.Scheduler.DelayBySeconds(_config.MenuTimeout, () =>
		{
			if (!player.IsValid)
				return;

			ClearMenuIfValidPlayer(player);

			// If T closed without selecting, pick random
			if (planting && _currentWire == WireColor.Max)
			{
				_currentWire = (WireColor)Random.Shared.Next(0, (int)WireColor.Max);
			}
		});
	}

	private void HandleWireSelection(IPlayer player, WireColor selectedWire, bool planting, ILocalizer localizer)
	{
		ClearMenuIfValidPlayer(player);

		var wireName = GetWireTranslation(selectedWire, localizer);

		if (planting)
		{
			// T is setting the wire
			_currentWire = selectedWire;
			player.SendChatAsync($" {localizer["k4.general.prefix"]} {localizer["k4.chat.select", wireName]}");
		}
		else
		{
			// CT is guessing the wire
			var bomb = Core.EntitySystem.GetAllEntitiesByClass<CPlantedC4>().FirstOrDefault();
			if (bomb == null)
				return;

			var playerName = player.Controller?.PlayerName ?? "Unknown";

			if (_currentWire == selectedWire)
			{
				// Correct! Instant defuse
				Core.PlayerManager.SendChatAsync($" {localizer["k4.general.prefix"]} {localizer["k4.chat.success", playerName, wireName]}");
				bomb.DefuseCountDown.Value = 0;
				bomb.DefuseCountDownUpdated();
			}
			else
			{
				// Wrong! Instant explosion
				Core.PlayerManager.SendChatAsync($" {localizer["k4.general.prefix"]} {localizer["k4.chat.failure", playerName, wireName]}");
				bomb.C4Blow.Value = 1.0f;
				bomb.C4BlowUpdated();
			}
		}
	}

	private static string GetWireTranslation(WireColor wire, ILocalizer localizer)
	{
		return wire switch
		{
			WireColor.Red => $"[red]{localizer["k4.menu.red"]}",
			WireColor.Blue => $"[lightblue]{localizer["k4.menu.blue"]}",
			WireColor.Yellow => $"[yellow]{localizer["k4.menu.yellow"]}",
			WireColor.Green => $"[green]{localizer["k4.menu.green"]}",
			_ => localizer["k4.menu.red"]
		};
	}

	private void ClearMenuIfValidPlayer(IPlayer? player)
	{
		if (player?.IsValid == true && !player.IsFakeClient && _wireMenu != null)
		{
			Core.MenusAPI.CloseMenuForPlayer(player, _wireMenu);
		}
	}
}
