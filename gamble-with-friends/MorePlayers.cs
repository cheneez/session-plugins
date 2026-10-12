using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Mirror;
using Steamworks;
using UnityEngine;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: AssemblyVersion("0.0.0.0")]
namespace MorePlayers
{
	[BepInPlugin("com.local.moreplayers", "MorePlayers", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		public static ConfigEntry<int> MaxPlayers;

		private static Plugin _inst;

		private void Awake()
		{
			_inst = this;
			MaxPlayers = Config.Bind("General", "MaxPlayers", 16, new ConfigDescription("Lobby size. Only the host needs this plugin.", new AcceptableValueRange<int>(2, 32)));
			new Harmony("com.local.moreplayers").PatchAll();
			ApplyCap();
			Logger.LogInfo("MorePlayers " + MaxPlayers.Value + " (host only)");
		}

		private void Start()
		{
			ApplyCap();
		}

		internal static int Cap()
		{
			int num = ((MaxPlayers != null) ? MaxPlayers.Value : 16);
			if (num < 2)
			{
				num = 2;
			}
			if (num > 32)
			{
				num = 32;
			}
			return num;
		}

		internal static void ApplyCap()
		{
			int num = Cap();
			try
			{
				LobbySettings lobbySettings = Resources.Load<LobbySettings>("LobbySettings");
				if (lobbySettings != null)
				{
					lobbySettings.maxPlayers = num;
				}
			}
			catch
			{
			}
			try
			{
				LobbySettings[] array = Resources.FindObjectsOfTypeAll<LobbySettings>();
				if (array != null)
				{
					for (int i = 0; i < array.Length; i++)
					{
						if (array[i] != null)
						{
							array[i].maxPlayers = num;
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				if (NetworkManager.singleton != null)
				{
					NetworkManager.singleton.maxConnections = num;
				}
			}
			catch
			{
			}
		}

		internal static void LimitFrom(object host)
		{
			if (host == null)
			{
				return;
			}
			try
			{
				FieldInfo fieldInfo = AccessTools.Field(host.GetType(), "lobbySettings");
				LobbySettings lobbySettings = ((fieldInfo != null) ? (fieldInfo.GetValue(host) as LobbySettings) : null);
				if (lobbySettings != null)
				{
					LimitSteamLobby(lobbySettings.steamLobbyID);
				}
			}
			catch
			{
			}
		}

		internal static void LimitSteamLobby(CSteamID id)
		{
			if (!id.IsValid())
			{
				return;
			}
			try
			{
				SteamMatchmaking.SetLobbyMemberLimit(id, Cap());
			}
			catch
			{
			}
		}

		internal static void WakeLater(PlayerController pc)
		{
			if (!(_inst == null) && !(pc == null) && NetworkServer.active)
			{
				_inst.StartCoroutine(WakeStuck(pc));
			}
		}

		private static IEnumerator WakeStuck(PlayerController pc)
		{
			yield return new WaitForSeconds(0.8f);
			if (pc == null || !NetworkServer.active)
			{
				yield break;
			}
			bool rag = false;
			try
			{
				rag = pc.State == PlayerController.PlayerState.Ragdoll;
			}
			catch
			{
			}
			if (rag)
			{
				try
				{
					pc.ServerLock(false);
				}
				catch
				{
				}
				try
				{
					pc.LocalLock(false);
				}
				catch
				{
				}
				try
				{
					pc.ServerWakeUp();
				}
				catch
				{
				}
			}
		}
	}
	[HarmonyPatch(typeof(NetworkServer), "Listen")]
	internal static class PatchListen
	{
		private static void Prefix(ref int maxConns)
		{
			Plugin.ApplyCap();
			maxConns = Plugin.Cap();
		}
	}
	[HarmonyPatch(typeof(SteamMatchmaking), "CreateLobby")]
	internal static class PatchCreateLobby
	{
		private static void Prefix(ref int cMaxMembers)
		{
			Plugin.ApplyCap();
			cMaxMembers = Plugin.Cap();
		}
	}
	[HarmonyPatch(typeof(LobbyManager), "CreateNewLobby")]
	internal static class PatchLobbyCreate
	{
		private static void Prefix()
		{
			Plugin.ApplyCap();
		}
	}
	[HarmonyPatch(typeof(LobbyManager), "OnNewLobbyCreated")]
	internal static class PatchLobbyCreated
	{
		private static void Postfix(LobbyManager __instance)
		{
			Plugin.ApplyCap();
			Plugin.LimitFrom(__instance);
		}
	}
	[HarmonyPatch(typeof(GoOnlineClient), "CreateLobby")]
	internal static class PatchOnlineCreate
	{
		private static void Prefix()
		{
			Plugin.ApplyCap();
		}
	}
	[HarmonyPatch(typeof(GoOnlineClient), "SetupLobbyData")]
	internal static class PatchOnlineSetup
	{
		private static void Postfix(GoOnlineClient __instance)
		{
			Plugin.LimitFrom(__instance);
		}
	}
	[HarmonyPatch(typeof(CustomNetworkManager), "OnStartServer")]
	internal static class PatchHostStart
	{
		private static void Prefix()
		{
			Plugin.ApplyCap();
		}
	}
	[HarmonyPatch(typeof(SpawnBoxPlayerRagdollTrigger), "AssignPlayer")]
	internal static class PatchBoxWake
	{
		private static void Postfix(PlayerController player)
		{
			Plugin.WakeLater(player);
		}
	}
}
