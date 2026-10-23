using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Policy;

namespace RPA.Helpers
{
    internal class AssembliesHelper
    {
        public static void ShowAssemblies()
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            //Provide the current application domain evidence for the assembly.
            Evidence asEvidence = currentDomain.Evidence;
            //Load the assembly from the application directory using a simple name.

            //Create an assembly called CustomLibrary to run this sample.
            //currentDomain.Load("CustomLibrary", asEvidence);

            //Make an array for the list of assemblies.
            Assembly[] assems = currentDomain.GetAssemblies().OrderBy(a => a.FullName).ToArray();

            //List the assemblies in the current application domain.
            Console.WriteLine("List of assemblies loaded in current appdomain:");
            foreach (Assembly assem in assems)
            {
                Debug.WriteLine(assem.ToString());
            }
        }
    }
}
