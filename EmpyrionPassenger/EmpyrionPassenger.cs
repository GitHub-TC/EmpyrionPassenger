using System;
using Eleon.Modding;
using EmpyrionAPITools;
using System.Collections.Generic;
using EmpyrionAPIDefinitions;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

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

    public partial class EmpyrionPassenger : SimpleMod
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

        FileSystemWatcher DBFileChangedWatcher;

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
            verbose = true;
            this.LogLevel = LogLevel.Message;

            log($"**HandleEmpyrionPassenger loaded: {string.Join(" ", Environment.GetCommandLineArgs())}", LogLevel.Message);

            InitializeDB();
            InitializeDBFileWatcher();

            Event_Player_Connected    += EmpyrionPassenger_Event_Player_Connected;
            Event_Player_Disconnected += EmpyrionPassenger_Event_Player_Disconnected;

            ChatCommands.Add(new ChatCommand(@"/pass",                           (I, A) => ExecCommand(SubCommand.Save,      I, A), "Saves Passengers als pilot of vessel"));
            ChatCommands.Add(new ChatCommand(@"/pass (?<Id>\d+)",                (I, A) => ExecCommand(SubCommand.Save,      I, A), "Saves Passengers manually and if not pilot with vessel ID"));
            ChatCommands.Add(new ChatCommand(@"/pass exec",                      (I, A) => ExecCommand(SubCommand.Teleport,  I, A), "Execute teleport"));
            ChatCommands.Add(new ChatCommand(@"/pass help",                      (I, A) => ExecCommand(SubCommand.Help,      I, A), "Display help"));
            ChatCommands.Add(new ChatCommand(@"/pass back",                      (I, A) => ExecCommand(SubCommand.Back,      I, A), "Teleports the player back to the last (good) position"));
            ChatCommands.Add(new ChatCommand(@"/pass delete (?<SourceId>\d+)",   (I, A) => ExecCommand(SubCommand.Delete,    I, A), "Delete all teleportdata from {SourceId}"));
            ChatCommands.Add(new ChatCommand(@"/pass list (?<Id>\d+)",           (I, A) => ExecCommand(SubCommand.List,      I, A), "List all teleportdata from {Id}"));
            ChatCommands.Add(new ChatCommand(@"/pass listall",                   (I, A) => ExecCommand(SubCommand.ListAll,   I, A), "List all teleportdata", PermissionType.Moderator));
            ChatCommands.Add(new ChatCommand(@"/pass cleanup",                   (I, A) => ExecCommand(SubCommand.CleanUp,   I, A), "Removes all teleportdata to deleted structures", PermissionType.Moderator));
        }

        private void EmpyrionPassenger_Event_Player_Disconnected(Id aPlayer)
        {
            SavePassengersDestination(aPlayer.id, 0);
        }

        private void EmpyrionPassenger_Event_Player_Connected(Id aPlayer)
        {
            TeleportPlayer(aPlayer.id);
        }

        private void InitializeDBFileWatcher()
        {
            DBFileChangedWatcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(PassengersDBFilename),
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(PassengersDBFilename)
            };
            DBFileChangedWatcher.Changed += (s, e) => PassengersDB = PassengerDB.ReadDB(PassengersDBFilename);
            DBFileChangedWatcher.EnableRaisingEvents = true;
        }

        private void InitializeDB()
        {
            PassengersDBFilename = Path.Combine(EmpyrionConfiguration.ProgramPath, @"Saves\Games\" + EmpyrionConfiguration.DedicatedYaml.SaveGameName + @"\Mods\EmpyrionPassenger\PassengersDB.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(PassengersDBFilename));

            // Move DB file to new location
            var OldDB = Path.Combine(Directory.GetCurrentDirectory(), @"Content\Mods\EmpyrionPassenger\PassengersDB.xml");
            if (File.Exists(OldDB)) File.Move(OldDB, PassengersDBFilename);

            PassengerDB.LogDB = log;
            PassengersDB = PassengerDB.ReadDB(PassengersDBFilename);
            PassengersDB.SaveDB(PassengersDBFilename);
        }


        enum ChatType
        {
            Global = 3,
            Faction = 5,
        }

        private void ExecCommand(SubCommand aCommand, ChatInfo info, Dictionary<string, string> args)
        {
            log($"**HandleEmpyrionPassenger {info.type}#{aCommand}:{info.msg} {args.Aggregate("", (s, i) => s + i.Key + "/" + i.Value + " ")}", LogLevel.Message);

            if (info.type != (byte)ChatType.Faction) return;

            switch (aCommand)
            {
                case SubCommand.Help    : DisplayHelp(info.playerId); break;
                case SubCommand.Back    : ExecTeleportPlayerBack(info.playerId); break;
                case SubCommand.Delete  : DeletePassengers(info.playerId, getIntParam(args, "SourceId")); break;
                case SubCommand.List    : ListTeleporterRoutes(info.playerId, getIntParam(args, "Id")); break;
                case SubCommand.ListAll : ListAllPassengers(info.playerId); break;
                case SubCommand.CleanUp : CleanUpTeleporterRoutes(info.playerId); break;
                case SubCommand.Save    : SavePassengersDestination(info.playerId, getIntParam(args, "Id")); break;
                case SubCommand.Teleport: TeleportPlayer(info.playerId); break;
            }
        }

        private void CleanUpTeleporterRoutes(int aPlayerId)
        {
            Request_GlobalStructure_List(G => {
                var GlobalFlatIdList = G.globalStructures.Aggregate(new List<int>(), (L, P) => { L.AddRange(P.Value.Select(S => S.id)); return L; });
                var TeleporterFlatIdList = PassengersDB.PassengersDestinations.Aggregate(new List<int>(), (L, P) => { L.Add(P.Destination.Id); return L; });

                var DeleteList = TeleporterFlatIdList.Where(I => !GlobalFlatIdList.Contains(I)).Distinct();
                var DelCount = DeleteList.Aggregate(0, (C, I) => C + PassengersDB.Delete(I));
                log($"CleanUpPassengers: {DelCount} Structures: {DeleteList.Aggregate("", (S, I) => S + "," + I)}", LogLevel.Message);
                InformPlayer(aPlayerId, $"CleanUp: {DelCount} Passengers");

                if (DelCount > 0) SaveTeleporterDB();
            });
        }

        private void TeleportPlayer(int aPlayerId)
        {
            Request_GlobalStructure_List(G =>
                Request_Player_Info(aPlayerId.ToId(), P => {
                    ExecTeleportPlayer(G, P, aPlayerId);
                }));
        }

        private void SavePassengersDestination(int aPlayerId, int aVesselId)
        {
            Request_GlobalStructure_List(G =>
            {
                //log("G:" + G.globalStructures
                //    .Aggregate("", (s, p) => s + p.Key + ":" +
                //    p.Value?.Aggregate("", (ss, pp) => $"{ss} {pp.id}/{pp.name}({pp.pilotId})")), LogLevel.Error);

                Request_Player_Info(aPlayerId.ToId(), (P) =>
                {
                    var PilotVessel = G.globalStructures
                        .Select(GPL => GPL.Value?
                            .FirstOrDefault(S => S.pilotId == P.entityId || (S.factionId == P.factionId && S.id == aVesselId)))
                        .Where(S => S.Value.id != 0)
                        .Select(S => S.Value)
                        .FirstOrDefault();

                    if (PilotVessel.id == 0) {
                        log($"{P.playerName}({P.entityId}): Not pilot of a vessel!");
                        AlertPlayer(P.entityId, $"Not pilot of a vessel! Wait a minute or use '/pass [VesselID]'");
                    }
                    else
                    {
                        PassengersDB.AddPassenderDestination(G, PilotVessel.id, P);
                        SaveTeleporterDB();
                        log($"{P.playerName}({P.entityId}): Passenger set to '{PilotVessel.name}' ({PilotVessel.id})");
                        ShowDialog(aPlayerId, P, "Passengers", $"\nPassenger set to '{PilotVessel.name}' ({PilotVessel.id})");
                    }
                });
            });
        }

        private void SaveTeleporterDB()
        {
            DBFileChangedWatcher.EnableRaisingEvents = false;
            PassengersDB.SaveDB(PassengersDBFilename);
            DBFileChangedWatcher.EnableRaisingEvents = true;
        }

        private void ListAllPassengers(int aPlayerId)
        {
            var Timer = new Stopwatch();
            Timer.Start();

            Request_GlobalStructure_List(G =>
            {
                Timer.Stop();
                Request_Player_Info(aPlayerId.ToId(), (P) =>
                {
                    ShowDialog(aPlayerId, P, $"Passengers (Playfields #{G.globalStructures.Count} Structures #{G.globalStructures.Aggregate(0, (c, p) => c + p.Value.Count)} load {Timer.Elapsed.TotalMilliseconds:N2}ms)", PassengersDB.PassengersDestinations.OrderBy(T => T.PassengerName).Aggregate("\n", (S, T) => S + T.ToString(G) + "\n"));
                });
            });
        }

        private void DeletePassengers(int aPlayerId, int aSourceId)
        {
            Request_Player_Info(aPlayerId.ToId(), (P) =>
            {
                var deletedCount = PassengersDB.Delete(aSourceId);
                SaveTeleporterDB();

                AlertPlayer(P.entityId, $"Delete {deletedCount} passenger from {aSourceId}");
            });
        }

        private void ListTeleporterRoutes(int aPlayerId, int aStructureId)
        {
            Request_GlobalStructure_List(G =>
            {
                Request_Player_Info(aPlayerId.ToId(), (P) =>
                {
                    ShowDialog(aPlayerId, P, "Passengers", PassengersDB.List(aStructureId, P).OrderBy(T => T.PassengerName).Aggregate("\n", (S, T) => S + T.ToString(G) + "\n"));
                });
            });
        }

    private bool ExecTeleportPlayer(GlobalStructureList aGlobalStructureList, PlayerInfo aPlayer, int aPlayerId)
        {
            var FoundRoute = PassengersDB.SearchRoute(aGlobalStructureList, aPlayer);
            if (FoundRoute == null)
            {
                log($"EmpyrionPassenger: Exec: {aPlayer.playerName}/{aPlayer.entityId}/{aPlayer.clientId} -> no logout vessel found for", LogLevel.Message);
                return false;
            }

            if(Math.Abs(Vector3.Distance(FoundRoute.Position, GetVector3(aPlayer.pos))) <= 10)
            {
                log($"EmpyrionPassenger: Exec: {aPlayer.playerName}/{aPlayer.entityId}/{aPlayer.clientId} -> near logout vessel pos={GetVector3(aPlayer.pos).String()} on '{aPlayer.playfield}'", LogLevel.Message);
                PassengersDB.DeletePassenger(aPlayer.entityId);
                SaveTeleporterDB();
                return false;
            }

            log($"EmpyrionPassenger: Exec: {aPlayer.playerName}/{aPlayer.entityId}-> {FoundRoute.Id} on '{FoundRoute.Playfield}' pos={FoundRoute.Position.String()} rot={FoundRoute.Rotation.String()}", LogLevel.Message);

            if (!PlayerLastGoodPosition.ContainsKey(aPlayer.entityId)) PlayerLastGoodPosition.Add(aPlayer.entityId, null);
            PlayerLastGoodPosition[aPlayer.entityId] = new IdPlayfieldPositionRotation(aPlayer.entityId, aPlayer.playfield, aPlayer.pos, aPlayer.rot);

            Action<PlayerInfo> ActionTeleportPlayer = (P) =>
            {
                if (FoundRoute.Playfield == P.playfield) Request_Entity_Teleport         (new IdPositionRotation(aPlayer.entityId, GetVector3(FoundRoute.Position), GetVector3(FoundRoute.Rotation)),                               null, (E) => InformPlayer(aPlayerId, "Entity_Teleport: {E}"));
                else                                     Request_Player_ChangePlayerfield(new IdPlayfieldPositionRotation(aPlayer.entityId, FoundRoute.Playfield, GetVector3(FoundRoute.Position), GetVector3(FoundRoute.Rotation)),null, (E) => InformPlayer(aPlayerId, "Player_ChangePlayerfield: {E}"));
            };

            Request_Player_SetPlayerInfo(new PlayerInfoSet() { entityId = aPlayer.entityId, health = (int)aPlayer.healthMax });

            new Thread(new ThreadStart(() =>
            {
                var TryTimer = new Stopwatch();
                TryTimer.Start();
                while (TryTimer.ElapsedMilliseconds < (PassengersDB.Configuration.PreparePlayerForTeleport * 1000))
                {
                    Thread.Sleep(2000);
                    var WaitTime = PassengersDB.Configuration.PreparePlayerForTeleport - (int)(TryTimer.ElapsedMilliseconds / 1000);
                    InformPlayer(aPlayerId, $"Prepare for teleport in {WaitTime} sec.");
                }

                ActionTeleportPlayer(aPlayer);
                CheckPlayerStableTargetPos(aPlayerId, aPlayer, ActionTeleportPlayer, FoundRoute.Position);
                PassengersDB.DeletePassenger(aPlayer.entityId);
                SaveTeleporterDB();
            })).Start();

            return true;
        }

        private void CheckPlayerStableTargetPos(int aPlayerId, PlayerInfo aCurrentPlayerInfo, Action<PlayerInfo> ActionTeleportPlayer, Vector3 aTargetPos)
        {
            new Thread(new ThreadStart(() =>
            {
                PlayerInfo LastPlayerInfo = aCurrentPlayerInfo;
                var TryTimer = new Stopwatch();
                TryTimer.Start();
                while (TryTimer.ElapsedMilliseconds < (PassengersDB.Configuration.HoldPlayerOnPositionAfterTeleport * 1000))
                {
                    Thread.Sleep(2000);
                    var WaitTime = PassengersDB.Configuration.HoldPlayerOnPositionAfterTeleport - (int)(TryTimer.ElapsedMilliseconds / 1000);
                    Request_Player_Info(aPlayerId.ToId(), P => {
                        LastPlayerInfo = P;
                        if(WaitTime > 0) InformPlayer(aPlayerId, $"Target reached please wait for {WaitTime} sec.");
                    }, (E) => InformPlayer(aPlayerId, "Target reached. {E}"));
                }
                if (Vector3.Distance(GetVector3(LastPlayerInfo.pos), aTargetPos) > 3) ActionTeleportPlayer(LastPlayerInfo);
                Request_Player_SetPlayerInfo(new PlayerInfoSet() { entityId = aCurrentPlayerInfo.entityId, health = (int)aCurrentPlayerInfo.healthMax });
                InformPlayer(aPlayerId, $"Thank you for traveling with the EmpyrionPassenger :-)");
            })).Start();
        }

        private void ExecTeleportPlayerBack(int aPlayerId)
        {
            Request_Player_Info(aPlayerId.ToId(), P => {

                if (!PlayerLastGoodPosition.ContainsKey(P.entityId))
                {
                    InformPlayer(aPlayerId, "No back teleport available.");
                    return;
                }

                var LastGoodPos = PlayerLastGoodPosition[P.entityId];
                PlayerLastGoodPosition.Remove(P.entityId);

                if (LastGoodPos.playfield == P.playfield) Request_Entity_Teleport(new IdPositionRotation(P.entityId, LastGoodPos.pos, LastGoodPos.rot));
                else Request_Player_ChangePlayerfield(LastGoodPos);
            });
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
            log($"{aPrefix} Error: {aError.errorType} {aError.ToString()}", LogLevel.Error);
        }

        private int getIntParam(Dictionary<string, string> aArgs, string aParameterName)
        {
            string valueStr;
            if (!aArgs.TryGetValue(aParameterName, out valueStr)) return 0;

            int value;
            if (!int.TryParse(valueStr, out value)) return 0;

            return value;
        }

        void ShowDialog(int aPlayerId, PlayerInfo aPlayer, string aTitle, string aMessage)
        {
            Request_ShowDialog_SinglePlayer(new DialogBoxData()
            {
                Id      = aPlayerId,
                MsgText = $"{aTitle}: [c][ffffff]{aPlayer.playerName}[-][/c] with permission [c][ffffff]{(PermissionType)aPlayer.permission}[-][/c]\n" + aMessage,
            });
        }

        private void DisplayHelp(int aPlayerId)
        {
            Request_Player_Info(aPlayerId.ToId(), (P) =>
            {
                var CurrentAssembly = Assembly.GetAssembly(this.GetType());
                //[c][hexid][-][/c]    [c][019245]test[-][/c].

                ShowDialog(aPlayerId, P, "Commands",
                    "\n" + String.Join("\n", GetChatCommandsForPermissionLevel((PermissionType)P.permission).Select(C => C.MsgString()).ToArray()) +
                    PassengersDB.Configuration.AllowedStructures.Aggregate("\n\n[c][00ffff]Passengers allowed at:[-][/c]", (s, a) => s + $"\n {a.EntityType}") +
                    $"\n\n[c][c0c0c0]{CurrentAssembly.GetAttribute<AssemblyTitleAttribute>()?.Title} by {CurrentAssembly.GetAttribute<AssemblyCompanyAttribute>()?.Company} Version:{CurrentAssembly.GetAttribute<AssemblyFileVersionAttribute>()?.Version}[-][/c]"
                    );
            });
        }

    }
}
