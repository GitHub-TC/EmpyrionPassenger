using System;
using Eleon.Modding;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using EmpyrionNetAPIDefinitions;
using EmpyrionNetAPITools;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Threading.Tasks;

namespace EmpyrionPassenger
{
    public class PassengerDB
    {
        public class TeleporterData
        {
            public int Id { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Rotation { get; set; }
            public override string ToString()
            {
                return $"Id:[c][ffffff]{Id}[-][/c] relpos=[c][ffffff]{Position.String()}[-][/c]";
            }
        }

        public class TeleporterRoute
        {
            public int PassengerId { get; set; }
            public string PassengerName { get; set; }
            public TeleporterData Destination { get; set; }

            public override string ToString()
            {
                return $"{PassengerName}/{PassengerId}: {Destination}";
            }

            public string ToInfoString()
            {
                var Sa = SearchEntity(Destination.Id).GetAwaiter().GetResult();

                return $"[c][ff0000]{PassengerName}/{PassengerId}[-][/c]: " +
                       (Sa == null ? Destination.ToString() : $"[c][ff00ff]{Sa.Data.name}[-][/c] [[c][ffffff]{Sa.Data.id}[-][/c]/[c][ffffff]{Sa.Playfield}[-][/c]]");
            }
        }

        public class AllowedStructure
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public EntityType EntityType { get; set; }
        }

        public static EmpyrionPassenger ModAccess { get; internal set; }

        public class ConfigurationAndDB
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public LogLevel LogLevel { get; set; } = LogLevel.Message;
            public string CommandPrefix { get; set; } = "/\\";
            public int PreparePlayerForTeleport { get; set; } = 10;
            public int HoldPlayerOnPositionAfterTeleport { get; set; } = 20;
            public float NoTeleportNearVesselDistance { get; set; } = 10;
            public int SecDelayAfterPlayerEnterTheGame { get; set; } = 60;
            public int HealthPack { get; set; } = 4437;
            public AllowedStructure[] AllowedStructures { get; set; } = new AllowedStructure[]
                {
                new AllowedStructure(){ EntityType = EntityType.HV },
                new AllowedStructure(){ EntityType = EntityType.SV },
                new AllowedStructure(){ EntityType = EntityType.CV },
                };
            public List<TeleporterRoute> PassengersDestinations { get; set; } = new List<TeleporterRoute>();
        }

        public ConfigurationManager<ConfigurationAndDB> Configuration { get; set; }

        public PassengerDB(string configurationFilename)
        {
            Configuration = new ConfigurationManager<ConfigurationAndDB>() {
                ConfigFilename = configurationFilename
            };

            Configuration.Load();
            Configuration.Save();
        }

        public class TeleporterTargetData : TeleporterData
        {
            public string Playfield { get; set; }
            public override string ToString()
            {
                return $"Id:[c][ffffff]{Id}/[c][ffffff]{Playfield}[-][/c] relpos=[c][ffffff]{Position.String()}[-][/c]";
            }
        }

        public static Action<string, LogLevel> LogDB { get; set; }

        private static void log(string aText, LogLevel aLevel)
        {
            LogDB?.Invoke(aText, aLevel);
        }

        public async Task AddPassenderDestination(int aVesselId, PlayerInfo aPlayer)
        {
            var FoundEntity = await SearchEntity(aVesselId);
            if (FoundEntity == null) return;

            var RelativePos = GetVector3(aPlayer.pos) - GetVector3(FoundEntity.Data.pos);
            var NormRot     = GetVector3(aPlayer.rot) - GetVector3(FoundEntity.Data.rot);

            var EntityRot = GetMatrix4x4(GetVector3(FoundEntity.Data.rot)).Transpose();

            RelativePos = Vector3.Transform(RelativePos, EntityRot);
            RelativePos = new Vector3(RelativePos.X, ((float)Math.Round(RelativePos.Y + 1.9) - 1), RelativePos.Z);

            var Target = Configuration.Current.PassengersDestinations.FirstOrDefault(P => P.PassengerId == aPlayer.entityId);
            if (Target == null) Configuration.Current.PassengersDestinations.Add(Target = new TeleporterRoute());

            Target.PassengerId   = aPlayer.entityId;
            Target.PassengerName = aPlayer.playerName;
            Target.Destination   = new TeleporterData() { Id = aVesselId, Position = RelativePos, Rotation = NormRot };

            Configuration.Current.PassengersDestinations = Configuration.Current.PassengersDestinations.OrderBy(T => T.PassengerName).ToList();
        }

        public class PlayfieldStructureInfo
        {
            public string Playfield { get; set; }
            public GlobalStructureInfo Data { get; set; }
        }

        public static async Task<PlayfieldStructureInfo> SearchEntity(int aSourceId)
        {
            var FoundEntity = await ModAccess.Request_GlobalStructure_Info(new Id(aSourceId));
            if (FoundEntity.id != 0) return new PlayfieldStructureInfo() { Playfield = FoundEntity.PlayfieldName, Data = FoundEntity };

            return null;
        }

        public int Delete(int aSourceId)
        {
            var OldCount = Configuration.Current.PassengersDestinations.Count();
            Configuration.Current.PassengersDestinations = Configuration.Current.PassengersDestinations
                .Where(T => T.Destination.Id != aSourceId)
                .ToList();

            return OldCount - Configuration.Current.PassengersDestinations.Count();
        }

        public int DeletePassenger(int aPlayerId)
        {
            var OldCount = Configuration.Current.PassengersDestinations.Count();
            Configuration.Current.PassengersDestinations = Configuration.Current.PassengersDestinations
                .Where(T => T.PassengerId != aPlayerId)
                .ToList();

            return OldCount - Configuration.Current.PassengersDestinations.Count();
        }

        public IEnumerable<TeleporterRoute> List(int aStructureId, PlayerInfo aPlayer)
        {
            return Configuration.Current.PassengersDestinations.Where(T => (T.Destination.Id == aStructureId));
        }

        async Task<TeleporterTargetData> GetCurrentTeleportTargetPosition(TeleporterData aTarget)
        {
            var StructureInfo = await SearchEntity(aTarget.Id);
            if (StructureInfo == null)
            {
                log($"TargetStructure missing:{aTarget.Id} pos={aTarget.Position.String()}", LogLevel.Error);
                return null;
            }

            var StructureInfoRot  = GetVector3(StructureInfo.Data.rot);
            var StructureRotation = GetMatrix4x4(StructureInfoRot);
            var TeleportTargetPos = Vector3.Transform(aTarget.Position, StructureRotation) + GetVector3(StructureInfo.Data.pos);

            log($"CurrentPassengerTargetPosition:{StructureInfo.Data.id}/{(EntityType)StructureInfo.Data.type} pos={StructureInfo.Data.pos.String()} TeleportPos={TeleportTargetPos.String()}", LogLevel.Message);

            return new TeleporterTargetData() { Id = aTarget.Id, Playfield = StructureInfo.Playfield, Position = TeleportTargetPos, Rotation = aTarget.Rotation + StructureInfoRot};
        }

        bool IsZero(PVector3 aVector)
        {
            return aVector.x == 0 && aVector.y == 0 && aVector.z == 0;
        }

        public async Task<TeleporterTargetData> SearchRoute(PlayerInfo aPlayer)
        {
            //log($"T:{TeleporterRoutes.Aggregate("", (s, t) => s + " " + t.ToString())} => {aGlobalStructureList.globalStructures.Aggregate("", (s, p) => s + p.Key + ":" + p.Value.Aggregate("", (ss, pp) => ss + " " + pp.id + "/" + pp.name))}");

            return await Configuration.Current.PassengersDestinations
                .Where(D => D.PassengerId == aPlayer.entityId)
                .Select(async I => await GetCurrentTeleportTargetPosition(I.Destination))
                .FirstOrDefault();
        }

        public static Vector3 GetVector3(PVector3 aVector)
        {
            return new Vector3(aVector.x, aVector.y, aVector.z);
        }

        public static PVector3 GetVector3(Vector3 aVector)
        {
            return new PVector3(aVector.X, aVector.Y, aVector.Z);
        }

        public static Matrix4x4 GetMatrix4x4(Vector3 aVector)
        {
            return Matrix4x4.CreateFromYawPitchRoll(
                aVector.Y.ToRadians(),
                aVector.X.ToRadians(),
                aVector.Z.ToRadians());
        }

    }
}
