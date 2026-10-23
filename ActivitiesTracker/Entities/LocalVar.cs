namespace ActivitiesTracker.Entities
{
    public class LocalVar
    {
        public string VarName { get; set; }

        public object Data { get; set; }

        public string TypeName { get; set; }

        public string Scope { get; set; }

        public LocalVar(string varName, object data, string typeName, string scope)
        {
            VarName = varName;
            Data = data;
            TypeName = typeName;
            Scope = scope;
        }
    }
}
