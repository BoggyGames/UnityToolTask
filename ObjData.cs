namespace UnityToolTask
{
    public class ObjData
    {
        public long gameObj;
        public long FileID;
        public List<long> Children;
    }

    public class ScriptData
    {
        public string Guid;
        public string relativePath;
        public bool unused = true;
    }
}