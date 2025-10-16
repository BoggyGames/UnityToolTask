using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityToolTask;

/*
 *Important info so far:
 *
 * -ProjectSettings/EditorBuildSettings.asset (yaml) has the full list of scenes and paths of them
 * -All the scenes are yaml, gameobjects are ordered in hierarchy via Transform (4) and RectTransform (224)
 * -m_gameObject tells us what GameObject the transform is linked to, and that GameObject has the m_Name
 */

//strings:
const string usString = @"UnusedStrings.csv";
const string scnPattern = @"*.unity";
const string csPattern = @"*.cs";
const string dumpExt = @".unity.dump";

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

var scenePaths = new List<string>();
var assetPath = projectFolder + "\\Assets";

//scan Assets dir for scenes
try
{
    foreach (string file in Directory.EnumerateFiles(assetPath, scnPattern, SearchOption.AllDirectories))
    {
        scenePaths.Add(file);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Can't perform search: {ex.Message}");
    return 1;
}

Console.WriteLine("Scenes detected:\n" + string.Join(",\n", scenePaths));

//hierarchy file for each scene
foreach (var scene in (scenePaths))
{
    var sr = new StreamReader(scene);
    var sceneName = Path.GetFileNameWithoutExtension(scene);
    var sceneContent = sr.ReadToEnd();
    sr.Close();
    
    var objects = Regex.Split(sceneContent, @"(?=^--- !u!\d+ &\d+)", RegexOptions.Multiline);
    
    //make sure you can find them easy by searching for the fileId
    var targets = new Dictionary<long, ObjData>();
    var gameObjs = new Dictionary<long, string>();

    var rootList = new List<ObjData>();
    
    //gameObjs are only used to get the gameObj name of the transform for the final hierarchy

    foreach (var obj in objects)
    {
        var match = Regex.Match(obj, @"^--- !u!(\d+) &(\d+)", RegexOptions.Multiline);
        if (!match.Success) continue;

        int type = int.Parse(match.Groups[1].Value);
        long fileID = long.Parse(match.Groups[2].Value);
        
        //4 is transform and 224 is recttransform - recttransform inherits transform so they behave the same here
        if (type == 4 || type == 224)
        {
            //lines of great importance for the hierarchy!
            var childrenLines = obj.Split("m_Children")[1].Split("m_Father")[0];
            var gameobjLine = obj.Split('\n')
                .FirstOrDefault(l => l.Contains("m_GameObject"));
            var parentLine = obj.Split('\n')
                .FirstOrDefault(l => l.Contains("m_Father"));
            
            //get all children
            var children = new List<long>();
            foreach (Match c in Regex.Matches(childrenLines, @"fileID:\s*(\d+)", RegexOptions.Multiline))
            {
                children.Add(long.Parse(c.Groups[1].Value));
            }
            
            //make sure we know if its at root or not
            var atRoot = (long.Parse(Regex.Match(parentLine, @"fileID:\s*(\d+)").Groups[1].Value)) == 0;
            
            //get corresponding gameobj for name
            var gameObj = long.Parse(Regex.Match(gameobjLine, @"fileID:\s*(\d+)").Groups[1].Value);

            var newObj = new ObjData()
            {
                gameObj = gameObj,
                FileID = fileID,
                Children = children
            };
            
            targets.Add(fileID, newObj);
            
            //if at root, use to start recursion later
            if (atRoot)
                rootList.Add(newObj);
        }
        else if (type == 1)
        {
            //1 is gameobject - get the name for the dictionary
            var nameLine = obj.Split('\n')
                .FirstOrDefault(l => l.Contains("m_Name"));
            
            //no regex needed here, just remove the first part
            var name = nameLine.Trim().Substring(8);
            
            gameObjs.Add(fileID, name);
        }
    }
    
    //finally, make the tree for this scene - recurse over these dicts from root
    var tree = "";
    foreach (var obj in rootList)
        tree += BuildTree(obj, 0, targets, gameObjs);

    //Console.WriteLine(tree);

    var sw = new StreamWriter(outputFolder + "\\" + sceneName + dumpExt);
    sw.Write(tree);
    sw.Close();
}

return 0;

//helper recursive function for writing the tree string
string BuildTree(ObjData current, int depth, Dictionary<long, ObjData> setObj, Dictionary<long, string> names)
{
    var childrenSet = "";
    var childrenDepth = "";
    for (int i = 0; i <= depth; i++)
        childrenDepth += "--";

    foreach (var child in current.Children)
    {
        childrenSet += childrenDepth + BuildTree(setObj[child], depth + 1, setObj, names);
    }
        
    return names[current.gameObj] + '\n' + childrenSet;
}

