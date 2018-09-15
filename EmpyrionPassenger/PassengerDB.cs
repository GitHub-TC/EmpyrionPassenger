using System;
using Eleon.Modding;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.Xml;
using System.Linq;
using System.Numerics;
using EmpyrionAPIDefinitions;

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

        public class TeleporterTargetData : TeleporterData
        {
            public string Playfield { get; set; }
            public override string ToString()
            {
                return $"Id:[c][ffffff]{Id}/[c][ffffff]{Playfield}[-][/c] relpos=[c][ffffff]{Position.String()}[-][/c]";
            }
        }

        public class TeleporterRoute
        {
            public int PassengerId { get; set; }
            public string PassengerName { get; set; }
            public TeleporterData Destination { get; set; }

            public override string ToString()
            {
                return $"{PassengerName}/{PassengerId}: {Destination.ToString()}";
            }

            public string ToString(GlobalStructureList G)
            {
                var Sa = SearchEntity(G, Destination.Id);

                return $"[c][ff0000]{PassengerName}/{PassengerId}[-][/c]: " +
                       (Sa == null ? Destination.ToString() : $"[c][ff00ff]{Sa.Data.name}[-][/c] [[c][ffffff]{Sa.Data.id}[-][/c]/[c][ffffff]{Sa.Playfield}[-][/c]]");
            }
        }

        public Configuration Configuration { get; set; } = new Configuration();
        public List<TeleporterRoute> PassengersDestinations { get; set; } = new List<TeleporterRoute>();
        public static Action<string, LogLevel> LogDB { get; set; }

        private static void log(string aText, LogLevel aLevel)
        {
            LogDB?.Invoke(aText, aLevel);
        }

        public void AddPassenderDestination(GlobalStructureList aGlobalStructureList, int aVesselId, PlayerInfo aPlayer)
        {
            var FoundEntity = SearchEntity(aGlobalStructureList, aVesselId);
            if (FoundEntity == null) return;

            var RelativePos = GetVector3(aPlayer.pos) - GetVector3(FoundEntity.Data.pos);
            var NormRot     = GetVector3(aPlayer.rot) - GetVector3(FoundEntity.Data.rot);

            var EntityRot = GetMatrix4x4(GetVector3(FoundEntity.Data.rot)).Transpose();

            RelativePos = Vector3.Transform(RelativePos, EntityRot);
            RelativePos = new Vector3(RelativePos.X, ((float)Math.Round(RelativePos.Y + 1.9) - 1), RelativePos.Z);

            var Target = PassengersDestinations.FirstOrDefault(P => P.PassengerId == aPlayer.entityId);
            if (Target == null) PassengersDestinations.Add(Target = new TeleporterRoute());

            Target.PassengerId   = aPlayer.entityId;
            Target.PassengerName = aPlayer.playerName;
            Target.Destination   = new TeleporterData() { Id = aVesselId, Position = RelativePos, Rotation = NormRot };

            PassengersDestinations = PassengersDestinations.OrderBy(T => T.PassengerName).ToList();
        }

        public class PlayfieldStructureInfo
        {
            public string Playfield { get; set; }
            public GlobalStructureInfo Data { get; set; }
        }

        public static PlayfieldStructureInfo SearchEntity(GlobalStructureList aGlobalStructureList, int aSourceId)
        {
            foreach (var TestPlayfieldEntites in aGlobalStructureList.globalStructures)
            {
                var FoundEntity = TestPlayfieldEntites.Value.FirstOrDefault(E => E.id == aSourceId);
                if (FoundEntity.id != 0) return new PlayfieldStructureInfo() { Playfield = TestPlayfieldEntites.Key, Data = FoundEntity };
            }
            return null;
        }

        public int Delete(int aSourceId)
        {
            var OldCount = PassengersDestinations.Count();
            PassengersDestinations = PassengersDestinations
                .Where(T => T.Destination.Id != aSourceId)
                .ToList();

            return OldCount - PassengersDestinations.Count();
        }

        public int DeletePassenger(int aPlayerId)
        {
            var OldCount = PassengersDestinations.Count();
            PassengersDestinations = PassengersDestinations
                .Where(T => T.PassengerId != aPlayerId)
                .ToList();

            return OldCount - PassengersDestinations.Count();
        }

        public IEnumerable<TeleporterRoute> List(int aStructureId, PlayerInfo aPlayer)
        {
            return PassengersDestinations.Where(T => (T.Destination.Id == aStructureId));
        }

        TeleporterTargetData GetCurrentTeleportTargetPosition(GlobalStructureList aGlobalStructureList, TeleporterData aTarget)
        {
            var StructureInfo = SearchEntity(aGlobalStructureList, aTarget.Id);
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

        public TeleporterTargetData SearchRoute(GlobalStructureList aGlobalStructureList, PlayerInfo aPlayer)
        {
            //log($"T:{TeleporterRoutes.Aggregate("", (s, t) => s + " " + t.ToString())} => {aGlobalStructureList.globalStructures.Aggregate("", (s, p) => s + p.Key + ":" + p.Value.Aggregate("", (ss, pp) => ss + " " + pp.id + "/" + pp.name))}");

            return PassengersDestinations
                .Where(D => D.PassengerId == aPlayer.entityId)
                .Select(I => GetCurrentTeleportTargetPosition(aGlobalStructureList, I.Destination))
                .FirstOrDefault();
        }

        public void SaveDB(string DBFileName)
        {
            var serializer = new XmlSerializer(typeof(PassengerDB));
            Directory.CreateDirectory(Path.GetDirectoryName(DBFileName));
            using (var writer = XmlWriter.Create(DBFileName, new XmlWriterSettings() { Indent = true, IndentChars = "  " }))
            {
                serializer.Serialize(writer, this);
            }
        }

        public static PassengerDB ReadDB(string DBFileName)
        {
            if (!File.Exists(DBFileName))
            {
                log($"PassengerDB ReadDB not found '{DBFileName}'", LogLevel.Error);
                return new PassengerDB();
            }

            try
            {
                log($"PassengerDB ReadDB load '{DBFileName}'", LogLevel.Message);
                var serializer = new XmlSerializer(typeof(PassengerDB));
                using (var reader = XmlReader.Create(DBFileName))
                {
                    return (PassengerDB)serializer.Deserialize(reader);
                }
            }
            catch(Exception Error)
            {
                log("PassengerDB ReadDB" + Error.ToString(), LogLevel.Error);
                return new PassengerDB();
            }
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
            return Matrix4x4.CreateFromYawPitchRoll(aVector.Y * (float)(Math.PI / 180), aVector.Z * (float)(Math.PI / 180), aVector.X * (float)(Math.PI / 180));
        }

    }
}
