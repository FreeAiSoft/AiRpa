namespace AiRpaStudio.Tests;

public class MainProjectRestoreTests
{
    [Test]
    public void IsVsProjectPath_Solution_ReturnsTrue()
    {
        Assert.That(RPA.Models.Main.IsVsProjectPath(@"C:\repos\Demo.sln"), Is.True);
    }

    [Test]
    public void IsVsProjectPath_Project_ReturnsTrue()
    {
        Assert.That(RPA.Models.Main.IsVsProjectPath(@"C:\repos\Demo.csproj"), Is.True);
    }

    [Test]
    public void IsVsProjectPath_SimpleProjectFolder_ReturnsFalse()
    {
        Assert.That(RPA.Models.Main.IsVsProjectPath(@"C:\repos\MybotStudio\Process"), Is.False);
        Assert.That(RPA.Models.Main.IsVsProjectPath(@"C:\repos\project.json"), Is.False);
    }

    [Test]
    public void IsVsProjectPath_NullOrWhitespace_ReturnsFalse()
    {
        Assert.That(RPA.Models.Main.IsVsProjectPath(null), Is.False);
        Assert.That(RPA.Models.Main.IsVsProjectPath(string.Empty), Is.False);
        Assert.That(RPA.Models.Main.IsVsProjectPath("   "), Is.False);
    }

    [Test]
    public void IsVsProjectPath_CaseInsensitive()
    {
        Assert.That(RPA.Models.Main.IsVsProjectPath(@"C:\repos\Demo.SLN"), Is.True);
        Assert.That(RPA.Models.Main.IsVsProjectPath(@"C:\repos\Demo.CSPROJ"), Is.True);
    }
}
