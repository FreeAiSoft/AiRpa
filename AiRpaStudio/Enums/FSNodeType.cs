namespace RPA.Enums
{
    public enum FSNodeType
    {
        File,
        Dir,
        Project,
        Solution,
        VsProject,

        /// <summary>Solution folder inside .sln (GUID {2150E333-...}): it has no project file (CP-06).</summary>
        SolutionFolder,

        /// <summary>Virtual "References" container of a Visual Studio project (dependencies).</summary>
        References,

        /// <summary>A single dependency (package/assembly/project reference) under "References".</summary>
        Reference
    }
}

