﻿using System;
using Eleon.Modding;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EmpyrionNetAPIAccess;
using EmpyrionNetAPIDefinitions;
using System.Threading.Tasks;
using System.Numerics;
using EmpyrionNetAPITools;
using EmpyrionNetAPITools.Extensions;

namespace EmpyrionPassenger
{
    public static class Extensions{
        public static string String(this PVector3 aVector) => $"{aVector.x:F1},{aVector.y:F1},{aVector.z:F1}";
        public static string String(this Vector3 aVector) => $"{aVector.X:F1},{aVector.Y:F1},{aVector.Z:F1}";

        public static T GetAttribute<T>(this Assembly aAssembly)
        {
            return aAssembly.GetCustomAttributes(typeof(T), false).OfType<T>().FirstOrDefault();
        }

        static Regex GetCommand = new Regex(@"(?<cmd>(\w|\/|\s)+)");

        public static string MsgString(this ChatCommand aCommand)
        {
            var CmdString = GetCommand.Match(aCommand.invocationPattern).Groups["cmd"]?.Value ?? aCommand.invocationPattern;
            return $"[c][ff00ff]{CmdString}[-][/c]{aCommand.paramNames.Aggregate(" ", (S, P) => S + $"<[c][00ff00]{P}[-][/c]> ")}: {aCommand.description}";
        }

    }

    public class EmpyrionPassenger : EmpyrionModBase
    {
        public ModGameAPI GameAPI { get; set; }
        public int SourceId { get; private set; }
        public int TargetId { get; private set; }
        public Vector3 ShiftVector { get; private set; }
        public IdPositionRotation BaseToAlign { get; private set; }
        public IdPositionRotation MainBase { get; private set; }
        public bool WithinAlign { get; private set; }

        public GlobalStructureList GlobalStructureList { get; set; } = new GlobalStructureList() { globalStructures = new Dictionary<string, List<GlobalStructureInfo>>() };
        public PassengerDB PassengersDB { get; set; }
        public IdPlayfieldPositionRotation ExecOnLoadedPlayfield { get; private set; }

        Dictionary<int, IdPlayfieldPositionRotation> PlayerLastGoodPosition = new Dictionary<int, IdPlayfieldPositionRotation>();

        public string PassengersDBFilename { get; set; }

        public EmpyrionPassenger()
        {
            EmpyrionConfiguration.ModName = "EmpyrionPassenger";
        }

        enum SubCommand
        {
            Help,
            Back,
            Delete,
            List,
            ListAll,
            CleanUp,
            Save,
            Teleport
        }

        public override void Initialize(ModGameAPI aGameAPI)
        {
            GameAPI = aGameAPI;

            Log($"**HandleEmpyrionPassenger loaded: {string.Join(" ", Environment.GetCommandLineArgs())}", LogLevel.Message);

            InitializeDB();
            LogLevel = PassengersDB.Configuration.Current.LogLevel;
            ChatCommandManager.CommandPrefix = PassengersDB.Configuration.Current.CommandPrefix;

            Event_Player_Connected += (P) => EmpyrionPassenger_Event_Player_Connected(P);

            ChatCommands.Add(new ChatCommand(@"pass",                           (I, A) => ExecCommand(SubCommand.Save,      I, A), "Saves Passengers als pilot of vessel"));
            ChatCommands.Add(new ChatCommand(@"pass (?<ID>\d+)",                (I, A) => ExecCommand(SubCommand.Save,      I, A), "Saves Passengers manually and if not pilot with vessel ID"));
            ChatCommands.Add(new ChatCommand(@"pass exec",                      (I, A) => ExecCommand(SubCommand.Teleport,  I, A), "Execute teleport"));
            ChatCommands.Add(new ChatCommand(@"pass help",                      (I, A) => ExecCommand(SubCommand.Help,      I, A), "Display help"));
            ChatCommands.Add(new ChatCommand(@"pass back",                      (I, A) => ExecCommand(SubCommand.Back,      I, A), "Teleports the player back to the last (good) position"));
            ChatCommands.Add(new ChatCommand(@"pass delete (?<ID>\d+)",         (I, A) => ExecCommand(SubCommand.Delete,    I, A), "Delete all teleportdata from {ID}"));
            ChatCommands.Add(new ChatCommand(@"pass list (?<ID>\d+)",           (I, A) => ExecCommand(SubCommand.List,      I, A), "List all teleportdata from {ID}"));
            ChatCommands.Add(new ChatCommand(@"pass listall",                   (I, A) => ExecCommand(SubCommand.ListAll,   I, A), "List all teleportdata", PermissionType.Moderator));
            ChatCommands.Add(new ChatCommand(@"pass cleanup",                   (I, A) => ExecCommand(SubCommand.CleanUp,   I, A), "Removes all teleportdata to deleted structures", PermissionType.Moderator));
        }

        private void EmpyrionPassenger_Event_Player_Connected(Id aPlayer)
        {
            Task.Run(async () => {
                await Task.Delay(PassengersDB.Configuration.Current.SecDelayAfterPlayerEnterTheGame * 1000);
                await CheckIfPlayerNeedsATeleport(aPlayer);
            });
        }

        private async Task CheckIfPlayerNeedsATeleport(Id aPlayer)
        {
            try
            {
                await TeleportPlayer(aPlayer.id);
            }
            catch (Exception error)
            {
                Log($"Event_Player_Connected(CheckIfPlayerNeedsATeleport:{aPlayer.id}): {error}", LogLevel.Error);
            }
        }

        private void InitializeDB()
        {
            PassengersDBFilename = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "Passengers.json");

            PassengerDB.LogDB     = Log;
            PassengerDB.ModAccess = this;
            PassengersDB = new PassengerDB(PassengersDBFilename);
        }


        enum ChatType
        {
            Global  = 3,
            Faction = 5,
        }

        private async Task ExecCommand(SubCommand aCommand, ChatInfo info, Dictionary<string, string> args)
        {
            Log($"**HandleEmpyrionPassenger {info.type}#{aCommand}:{info.msg} {args.Aggregate("", (s, i) => s + i.Key + "/" + i.Value + " ")}", LogLevel.Message);

            if (info.type != (byte)ChatType.Faction) return;

            switch (aCommand)
            {
                case SubCommand.Help    : await DisplayHelp                   (info.playerId); break;
                case SubCommand.Back    : await ExecTeleportPlayerBack        (info.playerId); break;
                case SubCommand.Delete  : await DeletePassengers              (info.playerId, getIntParam(args, "ID")); break;
                case SubCommand.List    : await ListTeleporterRoutes          (info.playerId, getIntParam(args, "ID")); break;
                case SubCommand.ListAll : await ListAllPassengers             (info.playerId); break;
                case SubCommand.CleanUp : await CleanUpTeleporterRoutes       (info.playerId); break;
                case SubCommand.Save    : await SavePassengersDestination     (info.playerId, getIntParam(args, "ID")); break;
                case SubCommand.Teleport: await TeleportPlayer                (info.playerId); break;
            }
        }

        private async Task CleanUpTeleporterRoutes(int aPlayerId)
        {
            var TeleporterFlatIdList = PassengersDB.Configuration.Current.PassengersDestinations.Aggregate(new List<int>(), (L, P) => { L.Add(P.Destination.Id); return L; });

            for (int i = TeleporterFlatIdList.Count - 1; i >= 0; i--)
            {
                if((await Request_GlobalStructure_Info(TeleporterFlatIdList[i].ToId())).id == 0) TeleporterFlatIdList.Remove(i);
            }

            var DeleteList = TeleporterFlatIdList.Distinct();
            var DelCount = DeleteList.Aggregate(0, (C, I) => C + PassengersDB.Delete(I));
            Log($"CleanUpPassengers: {DelCount} Structures: {DeleteList.Aggregate("", (S, I) => S + "," + I)}", LogLevel.Message);
            InformPlayer(aPlayerId, $"CleanUp: {DelCount} Passengers");

            if (DelCount > 0) PassengersDB.Configuration.Save();
        }

        private async Task TeleportPlayer(int aPlayerId)
        {
            try
            {
                var P = await Request_Player_Info(Timeouts.Wait1m, aPlayerId.ToId());

                if (P != null) await ExecTeleportPlayer(P, aPlayerId);
                else           Log($"Player {aPlayerId} not found.", LogLevel.Message);
            }
            catch (Exception error)
            {
                Log($"TeleportPlayer:{error}", LogLevel.Error);
            }
        }

        private async Task SavePassengersDestination(int aPlayerId, int aVesselId)
        {
            var P = await Request_Player_Info(aPlayerId.ToId());

            GlobalStructureInfo PilotVessel = new GlobalStructureInfo();

            if (aVesselId != 0) PilotVessel = await Request_GlobalStructure_Info(aVesselId.ToId());
            else {
                try
                {
                    var G = await Request_GlobalStructure_List(Timeouts.Wait1m);

                    PilotVessel = G.globalStructures
                        .Select(GPL => GPL.Value?
                            .FirstOrDefault(S => (S.pilotId == P.entityId && aVesselId == 0) ||
                                                 ((S.factionId == P.factionId || P.permission >= (int)PermissionType.Moderator) && S.id == aVesselId)))
                        .Where(S => S.Value.id != 0)
                        .Select(S => S.Value)
                        .FirstOrDefault();
                }
                catch (Exception error)
                {
                    Log($"SavePassengersDestination:Request_GlobalStructure_List:{error}", LogLevel.Message);
                }
            }

            if (PilotVessel.id == 0) {
                if (aVesselId == 0)
                {
                    Log($"{P.playerName}({P.entityId}): Not pilot of a vessel found!");
                    AlertPlayer(P.entityId, $"Not pilot of a vessel found! Wait a minute or use '/pass [VesselID]'");
                }
                else
                {
                    Log($"{P.playerName}({P.entityId}): vessel {aVesselId} not found");
                    AlertPlayer(P.entityId, $"{P.playerName}({P.entityId}): vessel {aVesselId} not found");
                }
            }
            else if (PilotVessel.factionId != P.factionId && P.permission < (int)PermissionType.Moderator)
            {
                Log($"{P.playerName}({P.entityId}): vessel '{PilotVessel.name}' [{PilotVessel.id}] has different faction!");
                AlertPlayer(P.entityId, $"{P.playerName}({P.entityId}): vessel '{PilotVessel.name}' [{PilotVessel.id}] has different faction!");
            }
            else
            {
                await PassengersDB.AddPassenderDestination(aVesselId, P);
                PassengersDB.Configuration.Save();
                Log($"{P.playerName}({P.entityId}): Passenger set to '{PilotVessel.name}' [{PilotVessel.id}]");
                await ShowDialog(aPlayerId, P, "Passengers", $"\nPassenger set to '{PilotVessel.name}' [{PilotVessel.id}]");
            }
        }

        private async Task ListAllPassengers(int aPlayerId)
        {
            var P = await Request_Player_Info(aPlayerId.ToId());
            await ShowDialog(aPlayerId, P, $"Passengers", PassengersDB.Configuration.Current.PassengersDestinations.OrderBy(T => T.PassengerName).Aggregate("\n", (S, T) => S + T.ToInfoString() + "\n"));
        }

        private async Task DeletePassengers(int aPlayerId, int aSourceId)
        {
            var P = await Request_Player_Info(aPlayerId.ToId());
            var deletedCount = PassengersDB.Delete(aSourceId);
            PassengersDB.Configuration.Save();

            AlertPlayer(P.entityId, $"Delete {deletedCount} passenger from {aSourceId}");
        }

        private async Task ListTeleporterRoutes(int aPlayerId, int aStructureId)
        {
            var P = await Request_Player_Info(aPlayerId.ToId());
            await ShowDialog(aPlayerId, P, "Passengers", PassengersDB.List(aStructureId, P).OrderBy(T => T.PassengerName).Aggregate("\n", (S, T) => S + T.ToInfoString() + "\n"));
        }

        private async Task<bool> ExecTeleportPlayer(PlayerInfo aPlayer, int aPlayerId)
        {
            var FoundDestination = await PassengersDB.SearchRoute(aPlayer);
            if (FoundDestination == null)
            {
                Log($"EmpyrionPassenger: Exec: {aPlayer.playerName}/{aPlayer.entityId}/{aPlayer.clientId} -> no logout vessel found for", LogLevel.Message);
                return false;
            }

            if(Math.Abs(Vector3.Distance(FoundDestination.Position, GetVector3(aPlayer.pos))) <= PassengersDB.Configuration.Current.NoTeleportNearVesselDistance)
            {
                Log($"EmpyrionPassenger: Exec: {aPlayer.playerName}/{aPlayer.entityId}/{aPlayer.clientId} -> near logout vessel pos={GetVector3(aPlayer.pos).String()} on '{aPlayer.playfield}'", LogLevel.Message);
                PassengersDB.DeletePassenger(aPlayer.entityId);
                PassengersDB.Configuration.Save();
                return false;
            }

            Log($"EmpyrionPassenger: Exec: {aPlayer.playerName}/{aPlayer.entityId}-> {FoundDestination.Id} on '{FoundDestination.Playfield}' pos={FoundDestination.Position.String()} rot={FoundDestination.Rotation.String()}", LogLevel.Message);

            if (!PlayerLastGoodPosition.ContainsKey(aPlayer.entityId)) PlayerLastGoodPosition.Add(aPlayer.entityId, null);
            PlayerLastGoodPosition[aPlayer.entityId] = new IdPlayfieldPositionRotation(aPlayer.entityId, aPlayer.playfield, aPlayer.pos, aPlayer.rot);

            Action<PlayerInfo> ActionTeleportPlayer = async (P) =>
            {
                if (FoundDestination.Playfield == P.playfield)
                    try
                    {
                        await Request_Entity_Teleport(new IdPositionRotation(aPlayer.entityId, GetVector3(FoundDestination.Position), GetVector3(FoundDestination.Rotation)));
                    }
                    catch (Exception error)
                    {
                        InformPlayer(aPlayerId, $"Entity_Teleport: {error.Message}");
                    }
                else
                {
                    try
                    {
                        await Request_Player_ChangePlayerfield(new IdPlayfieldPositionRotation(aPlayer.entityId, FoundDestination.Playfield, GetVector3(FoundDestination.Position), GetVector3(FoundDestination.Rotation)));
                    }
                    catch (Exception error)
                    {
                        InformPlayer(aPlayerId, $"Player_ChangePlayerfield: {error.Message}");
                    }
                }
            };

            try
            {
                await Request_Player_SetPlayerInfo(new PlayerInfoSet() { entityId = aPlayer.entityId, health = (int)aPlayer.healthMax });
            }
            catch (Exception error)
            {
                InformPlayer(aPlayerId, $"Player_SetHealth: {error.Message}");
            }

            new Thread(new ThreadStart(() =>
            {
                var TryTimer = new Stopwatch();
                TryTimer.Start();
                while (TryTimer.ElapsedMilliseconds < (PassengersDB.Configuration.Current.PreparePlayerForTeleport * 1000))
                {
                    var WaitTime = PassengersDB.Configuration.Current.PreparePlayerForTeleport - (int)(TryTimer.ElapsedMilliseconds / 1000);
                    InformPlayer(aPlayerId, WaitTime > 1 ? $"Prepare for teleport in {WaitTime} sec." : $"Prepare for teleport now.");
                    if (WaitTime > 0) Thread.Sleep(2000);
                }

                ActionTeleportPlayer(aPlayer);
                CheckPlayerStableTargetPos(aPlayerId, aPlayer, ActionTeleportPlayer, FoundDestination.Position);
                PassengersDB.DeletePassenger(aPlayer.entityId);
                PassengersDB.Configuration.Save();
            })).Start();

            return true;
        }

        private void CheckPlayerStableTargetPos(int aPlayerId, PlayerInfo aCurrentPlayerInfo, Action<PlayerInfo> ActionTeleportPlayer, Vector3 aTargetPos)
        {
            new Thread(new ThreadStart(async () =>
            {
                try
                {
                    PlayerInfo LastPlayerInfo = aCurrentPlayerInfo;

                    await Request_Player_SetPlayerInfo(new PlayerInfoSet() { entityId = aCurrentPlayerInfo.entityId, health = (int)aCurrentPlayerInfo.healthMax });
                    if (PassengersDB.Configuration.Current.HealthPack > 0) await Request_Player_AddItem(new IdItemStack(aCurrentPlayerInfo.entityId, new ItemStack(PassengersDB.Configuration.Current.HealthPack, 1))); // Bandages ;-)

                    var TryTimer = new Stopwatch();
                    TryTimer.Start();
                    while (TryTimer.ElapsedMilliseconds < (PassengersDB.Configuration.Current.HoldPlayerOnPositionAfterTeleport * 1000))
                    {
                        Thread.Sleep(2000);
                        var WaitTime = PassengersDB.Configuration.Current.HoldPlayerOnPositionAfterTeleport - (int)(TryTimer.ElapsedMilliseconds / 1000);
                        try
                        {
                            var P = await Request_Player_Info(aPlayerId.ToId());
                            LastPlayerInfo = P;
                            if (WaitTime > 0) InformPlayer(aPlayerId, $"Target reached please wait for {WaitTime} sec.");
                        }
                        catch (Exception error)
                        {
                            InformPlayer(aPlayerId, $"Target reached. {error}");
                        }
                    }
                    if (Vector3.Distance(GetVector3(LastPlayerInfo.pos), aTargetPos) > 3) ActionTeleportPlayer(LastPlayerInfo);
                    await Request_Player_SetPlayerInfo(new PlayerInfoSet() { entityId = aCurrentPlayerInfo.entityId, health = (int)aCurrentPlayerInfo.healthMax });
                    InformPlayer(aPlayerId, $"Thank you for traveling with the EmpyrionPassenger :-)");
                }
                catch (Exception error)
                {
                    Log($"CheckPlayerStableTargetPos: {error}", LogLevel.Error);
                }
            })).Start();
        }

        private async Task ExecTeleportPlayerBack(int aPlayerId)
        {
            var P = await Request_Player_Info(aPlayerId.ToId());

            if (!PlayerLastGoodPosition.ContainsKey(P.entityId))
            {
                InformPlayer(aPlayerId, "No back teleport available.");
                return;
            }

            var LastGoodPos = PlayerLastGoodPosition[P.entityId];
            PlayerLastGoodPosition.Remove(P.entityId);

            if (LastGoodPos.playfield == P.playfield) await Request_Entity_Teleport(new IdPositionRotation(P.entityId, LastGoodPos.pos, LastGoodPos.rot));
            else                                      await Request_Player_ChangePlayerfield(LastGoodPos);
        }

        public static Vector3 GetVector3(PVector3 aVector)
        {
            return new Vector3(aVector.x, aVector.y, aVector.z);
        }

        public static PVector3 GetVector3(Vector3 aVector)
        {
            return new PVector3(aVector.X, aVector.Y, aVector.Z);
        }

        private void LogError(string aPrefix, ErrorInfo aError)
        {
            Log($"{aPrefix} Error: {aError.errorType} {aError.ToString()}", LogLevel.Error);
        }

        private int getIntParam(Dictionary<string, string> aArgs, string aParameterName)
        {
            string valueStr;
            if (!aArgs.TryGetValue(aParameterName, out valueStr)) return 0;

            int value;
            if (!int.TryParse(valueStr, out value)) return 0;

            return value;
        }

        private async Task DisplayHelp(int aPlayerId)
        {
            await DisplayHelp(aPlayerId, PassengersDB.Configuration.Current.AllowedStructures.Aggregate("\n\n[c][00ffff]Passengers allowed at:[-][/c]", (s, a) => s + $"\n {a.EntityType}"));
        }

    }
}
