using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityToolTask;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
const string metaExt = @".meta";

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

//part 1 data: scene hierarchy analysis/dumps

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

//part 2 data: unused scripts

var scriptPaths = new List<string>();

//scan for all scripts in assets
try
{
    foreach (string file in Directory.EnumerateFiles(assetPath, csPattern, SearchOption.AllDirectories))
    {
        scriptPaths.Add(file);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Can't perform search: {ex.Message}");
    return 1;
}

Console.WriteLine("Scripts detected:\n" + string.Join(",\n", scriptPaths));

var scriptsByGuid = new Dictionary<string, ScriptData>();
var scriptsByName = new Dictionary<string, ScriptData>();

//we're just reading the meta files here for guids
foreach (var script in scriptPaths)
{
    var sr = new StreamReader(script + metaExt);
    var name = Path.GetFileNameWithoutExtension(script);
    var metaContent = sr.ReadToEnd();
    sr.Close();

    var guid = metaContent.Split('\n')[1].Split(' ')[1];
    var newScript = new ScriptData()
    {
        Guid = guid,
        relativePath = "Assets" + script.Split("Assets")[^1].Replace('\\', '/'),
        unused = true
    };
    
    scriptsByGuid[guid] = newScript;
    scriptsByName[name] = newScript;
}

//hierarchy file for each scene while scanning for script usage
//parallel.foreach for bonus task #2 ! we can safely do every scene separately, in parallel
Parallel.ForEach(scenePaths, new ParallelOptions { MaxDegreeOfParallelism = 16 }, scene =>
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
        else if (type == 114)
        {
            //MonoBehaviour - using a script. read the guid and set it to used
            var sLine = obj.Split('\n')
                .FirstOrDefault(l => l.Contains("m_Script"));
            
            //don't overlook the guid being hex!!! can't just do \d
            var sGuid = Regex.Match(sLine, @"guid:\s*([0-9a-fA-F]+)").Groups[1].Value;
            scriptsByGuid[sGuid].unused = false;
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
});

//bonus task #1 now - first get syntax trees of every script
var trees = scriptPaths.Select(x => CSharpSyntaxTree.ParseText(File.ReadAllText(x))).ToList();

//still using parallel.foreach for bonus task #2
Parallel.ForEach(trees, new ParallelOptions { MaxDegreeOfParallelism = 16 }, tree =>
{
    var root = tree.GetRoot();

    foreach (var classNode in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
    {
        //mono?
        if (classNode.BaseList == null)
            continue;
        if (!classNode.BaseList.Types.Any(b => b.ToString().Contains("MonoBehaviour")))
            continue;
        //let's see the money (and by money i mean fields)
        foreach (var field in classNode.Members.OfType<FieldDeclarationSyntax>())
        {
            var typeName = field.Declaration.Type.ToString();
            //this is why we have the dict over class/script names as well
            if(scriptsByName.ContainsKey(typeName))
                scriptsByName[typeName].unused = false;
        }
    }
});

var csvString = "Relative Path,GUID";
foreach (var script in scriptsByName.Values)
    if (script.unused)
        csvString += "\n" + script.relativePath + "," + script.Guid;
var sw = new StreamWriter(outputFolder + "\\" + usString);
sw.Write(csvString);

sw.Close();

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

