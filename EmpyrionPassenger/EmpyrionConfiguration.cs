using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EmpyrionPassenger
{
    public class EmpyrionConfiguration
    {
        public static string[] MyProperty { get; set; } = Environment.GetCommandLineArgs();
        public static string ProgramPath { get; private set; } = Directory.GetCurrentDirectory();
        public static string ModPath { get; private set; } = Path.Combine(ProgramPath, @"Content\Mods");
        public static string DedicatedFilename { get; private set; } = Environment.GetCommandLineArgs().Contains("-dedicated") 
                                                                            ? Environment.GetCommandLineArgs().SkipWhile(A => string.Compare(A, "-dedicated", StringComparison.InvariantCultureIgnoreCase) != 0).Skip(1).FirstOrDefault() 
                                                                            : "dedicated.yaml";

        public static DedicatedYamlStruct DedicatedYaml { get; set; } = new DedicatedYamlStruct(Path.Combine(ProgramPath, DedicatedFilename));

        public class DedicatedYamlStruct
        {
            public string SaveGameName { get; private set; }
            public string CustomScenarioName { get; private set; }

            public DedicatedYamlStruct(string aFilename)
            {
                if (!File.Exists(aFilename)) return;

                using (var input = File.OpenText(aFilename))
                {
                    var yaml = new YamlStream();
                    yaml.Load(input);

                    var Root = (YamlMappingNode)yaml.Documents[0].RootNode;

                    var GameConfigNode = Root.Children[new YamlScalarNode("GameConfig")] as YamlMappingNode;

                    SaveGameName       = GameConfigNode?.Children[new YamlScalarNode("GameName"      )]?.ToString();
                    CustomScenarioName = GameConfigNode?.Children[new YamlScalarNode("CustomScenario")]?.ToString();
                }

            }

        }
    }
}
