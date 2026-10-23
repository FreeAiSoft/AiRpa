using RPA.Entities;
using RPA.Enums;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

public class HelpersTests
{
    private readonly FSNodeComparer _comparer = new FSNodeComparer();

    [Test]
    public void FSNodeComparer_BothNull_ReturnsZero()
    {
        Assert.That(_comparer.Compare(null, null), Is.EqualTo(0));
    }

    [Test]
    public void FSNodeComparer_XNull_ReturnsMinusOne()
    {
        var node = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        Assert.That(_comparer.Compare(null, node), Is.EqualTo(-1));
    }

    [Test]
    public void FSNodeComparer_YNull_ReturnsOne()
    {
        var node = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        Assert.That(_comparer.Compare(node, null), Is.EqualTo(1));
    }

    [Test]
    public void FSNodeComparer_FolderComesBeforeFile()
    {
        var folder = new FSNode(@"C:\dir\sub", FSNodeType.Dir);
        var file = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        Assert.That(_comparer.Compare(folder, file), Is.EqualTo(-1));
        Assert.That(_comparer.Compare(file, folder), Is.EqualTo(1));
    }

    [Test]
    public void FSNodeComparer_ProjectCountsAsFolder()
    {
        var project = new FSNode(@"C:\dir", FSNodeType.Project);
        var file = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        Assert.That(_comparer.Compare(project, file), Is.EqualTo(-1));
    }

    [Test]
    public void FSNodeComparer_BothFiles_SortsAlphabeticallyIgnoreCase()
    {
        var a = new FSNode(@"C:\dir\a.xaml", FSNodeType.File);
        var b = new FSNode(@"C:\dir\B.xaml", FSNodeType.File);
        Assert.That(_comparer.Compare(a, b), Is.EqualTo(-1));
        Assert.That(_comparer.Compare(b, a), Is.EqualTo(1));
    }
}
