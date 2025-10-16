//using YamlDotNet.Serialization;
using System.IO;
using System.Runtime.InteropServices;

/*
 *Important info so far:
 *
 * -ProjectSettings/EditorBuildSettings.asset (yaml) has the full list of scenes and paths of them
 * -All the scenes are yaml, gameobjects are ordered in hierarchy via Transform (4) and RectTransform (224)
 * -m_gameObject tells us what GameObject the transform is linked to, and that GameObject has the m_Name
 */

//strings:
const string ebString = @"\\ProjectSettings\\EditorBuildSettings.asset";
const string usString = @"UnusedStrings.csv";

if (args.Length != 2)
{
    Console.WriteLine("Usage: UnityToolTask <path-to-project-folder> <output-folder>");
    return 0;
}
var projectFolder = args[0];
var outputFolder = args[1];

if (!Directory.Exists(projectFolder))
{
    Console.WriteLine("Project folder does not exist: " + projectFolder);
    return 0;
}

if (!Directory.Exists(outputFolder))
{
    Directory.CreateDirectory(outputFolder);
}

//part 1: scene hierarchy analysis/dumps

var ebPath = projectFolder + ebString;

if (!File.Exists(ebPath))
{
    Console.WriteLine("Are you sure this is a valid Unity project? EditorBuildSettings file does not exist: " + ebPath);
    return 0;
}

var scenePaths = new List<string>();

var sr = new StreamReader(ebPath);
var ebYml = sr.ReadToEnd();
sr.Close();

Console.WriteLine(ebYml);

//no need to deserialize, simple foreach will do in this case
foreach (var line in (ebYml.Split('\n')))
    if (line.Contains("path: "))
        scenePaths.Add(line.Trim().Split(' ')[1].Replace('/','\\'));

Console.WriteLine("Scenes detected:\n" + string.Join(",\n", scenePaths));

return 0;