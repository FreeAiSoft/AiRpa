using System;
using System.Collections.Generic;
using RPA.Entities;
using RPA.Enums;

namespace RPA.Helpers
{
    /// <summary>
    /// Comparer for sorting FSNodes: folders first (alphabetically), then files (alphabetically)
    /// </summary>
    public class FSNodeComparer : IComparer<FSNode>
    {
        public int Compare(FSNode x, FSNode y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            if (x == null)
            {
                return -1;
            }
            if (y == null)
            {
                return 1;
            }

            // Folders should come before files
            bool xIsFolder = x.FSNodeType == FSNodeType.Dir || x.FSNodeType == FSNodeType.Project;
            bool yIsFolder = y.FSNodeType == FSNodeType.Dir || y.FSNodeType == FSNodeType.Project;

            if (xIsFolder && !yIsFolder)
            {
                return -1; // x (folder) comes before y (file)
            }
            else if (!xIsFolder && yIsFolder)
            {
                return 1; // y (folder) comes before x (file)
            }
            else
            {
                // Both are folders or both are files, sort alphabetically
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
