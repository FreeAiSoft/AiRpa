using System;
using System.IO;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

public class VsSolutionParserTests
{
    private const string SampleSln =
        "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
        "# Visual Studio Version 17\r\n" +
        "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"MyLib\", \"MyLib\\MyLib.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
        "EndProject\r\n" +
        "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Solution Items\", \"Solution Items\", \"{22222222-2222-2222-2222-222222222222}\"\r\n" +
        "EndProject\r\n";

    [Test]
    public void Parse_ExtractsProjectEntries()
    {
        string sln = WriteTemp(SampleSln, "Sample.sln");
        try
        {
            var solution = VsSolutionParser.Parse(sln);

            Assert.That(solution.Name, Is.EqualTo("Sample"));
            Assert.That(solution.Projects.Count, Is.EqualTo(2));

            var first = solution.Projects[0];
            Assert.That(first.Name, Is.EqualTo("MyLib"));
            Assert.That(first.TypeGuid, Is.EqualTo("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"));
            Assert.That(first.RelativePath, Is.EqualTo("MyLib\\MyLib.csproj"));
            Assert.That(first.ProjectGuid, Is.EqualTo("{11111111-1111-1111-1111-111111111111}"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(sln), true);
        }
    }

    [Test]
    public void IsCsproj_ReturnsTrueOnlyForCsproj()
    {
        var csproj = new VsSolutionProject { RelativePath = @"MyLib\MyLib.csproj" };
        var folder = new VsSolutionProject { RelativePath = "Solution Items" };

        Assert.That(VsSolutionParser.IsCsproj(csproj), Is.True);
        Assert.That(VsSolutionParser.IsCsproj(folder), Is.False);
        Assert.That(VsSolutionParser.IsCsproj(null), Is.False);
    }

    [Test]
    public void Parse_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => VsSolutionParser.Parse(@"C:\no\such\file.sln"));
    }

    private static string WriteTemp(string content, string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "vs_sln_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
