using EmpyrionAPIDefinitions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EmpyrionPassenger
{
    public class AllowedStructure
    {
        public EntityType EntityType { get; set; }
    }

    public class Configuration
    {
        public int PreparePlayerForTeleport { get; set; } = 10;
        public int HoldPlayerOnPositionAfterTeleport { get; set; } = 20;
        public AllowedStructure[] AllowedStructures { get; set; } = new AllowedStructure[] 
            {
                new AllowedStructure(){ EntityType = EntityType.HV },
                new AllowedStructure(){ EntityType = EntityType.SV },
                new AllowedStructure(){ EntityType = EntityType.CV },
            };
    }
}
