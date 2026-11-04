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
        SolutionFolder
    }
}

