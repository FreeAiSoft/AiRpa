using System;
using System.IO;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

public class ObjectHelperTests
{
    private static string GetTempFile() => Path.Combine(Path.GetTempPath(), "objhelper_" + Guid.NewGuid().ToString("N") + ".json");

    [Test]
    public void SaveObject_ThenInitObject_RoundTrips()
    {
        string path = GetTempFile();
        try
        {
            var src = new Sample { Name = "x", Count = 3 };
            ObjectHelper.SaveObject(path, src);

            var loaded = ObjectHelper.InitObject<Sample>(path);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Name, Is.EqualTo("x"));
            Assert.That(loaded.Count, Is.EqualTo(3));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void InitObject_MissingFile_ReturnsDefault()
    {
        string path = GetTempFile();
        Assert.That(ObjectHelper.InitObject<Sample>(path), Is.Null);
    }

    [Test]
    public void InitObject_InvalidJson_ReturnsDefault()
    {
        string path = GetTempFile();
        try
        {
            File.WriteAllText(path, "{not valid json");
            Assert.That(ObjectHelper.InitObject<Sample>(path), Is.Null);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public class Sample
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }
}
