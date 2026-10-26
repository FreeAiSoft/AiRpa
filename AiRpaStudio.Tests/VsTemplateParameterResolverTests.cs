using System;
using RPA.VsTemplate;

namespace AiRpaStudio.Tests;

public class VsTemplateParameterResolverTests
{
    private static VsTemplateInstantiationRequest CreateRequest()
    {
        return new VsTemplateInstantiationRequest
        {
            ProjectName = "My Project",
            SafeProjectName = "My Project",
            RootNamespace = "MyCompany.MyProject",
            TargetFrameworkVersion = "4.8",
            RegisteredOrganization = "Acme",
            UserName = "tester",
            MachineName = "MACHINE1",
            Year = "2025",
            Time = "12:00:00",
            SafeItemName = "MyActivity",
            FileInputName = "MyActivity"
        };
    }

    [Test]
    public void Resolve_KnownParameters_ReturnsValues()
    {
        var resolver = new VsTemplateParameterResolver(CreateRequest());

        Assert.That(resolver.Resolve("safeprojectname"), Is.EqualTo("My Project"));
        Assert.That(resolver.Resolve("projectname"), Is.EqualTo("My Project"));
        Assert.That(resolver.Resolve("rootnamespace"), Is.EqualTo("MyCompany.MyProject"));
        Assert.That(resolver.Resolve("targetframeworkversion"), Is.EqualTo("4.8"));
        Assert.That(resolver.Resolve("registeredorganization"), Is.EqualTo("Acme"));
        Assert.That(resolver.Resolve("username"), Is.EqualTo("tester"));
        Assert.That(resolver.Resolve("machinename"), Is.EqualTo("MACHINE1"));
        Assert.That(resolver.Resolve("year"), Is.EqualTo("2025"));
        Assert.That(resolver.Resolve("time"), Is.EqualTo("12:00:00"));
        Assert.That(resolver.Resolve("safeitemname"), Is.EqualTo("MyActivity"));
        Assert.That(resolver.Resolve("fileinputname"), Is.EqualTo("MyActivity"));
    }

    [Test]
    public void Resolve_RootNamespace_DefaultsToSafeProjectName()
    {
        var request = CreateRequest();
        request.RootNamespace = null;
        var resolver = new VsTemplateParameterResolver(request);

        Assert.That(resolver.Resolve("rootnamespace"), Is.EqualTo("My Project"));
    }

    [Test]
    public void Resolve_Guid_IsStableAndFormatted()
    {
        var resolver = new VsTemplateParameterResolver(CreateRequest());

        string guid1 = resolver.Resolve("guid1");
        string guid1Again = resolver.Resolve("guid1");
        string guid2 = resolver.Resolve("guid2");

        Assert.That(guid1, Is.EqualTo(guid1Again));
        Assert.That(guid2, Is.Not.EqualTo(guid1));
        Assert.That(guid1, Does.Not.StartWith("{"));
        Assert.That(guid1, Does.Not.EndWith("}"));
        Assert.That(Guid.TryParseExact(guid1, "D", out _), Is.True);
    }

    [Test]
    public void Resolve_UnknownParameter_ReturnsTokenUnchanged()
    {
        var resolver = new VsTemplateParameterResolver(CreateRequest());

        Assert.That(resolver.Resolve("unknownparam"), Is.EqualTo("$unknownparam$"));
    }

    [Test]
    public void MakeSafeProjectName_RemovesInvalidCharacters()
    {
        Assert.That(VsTemplateParameterResolver.MakeSafeProjectName("Bad<Name>:*?"), Is.EqualTo("BadName"));
    }

    [Test]
    public void MakeSafeProjectName_KeepsSpaces()
    {
        Assert.That(VsTemplateParameterResolver.MakeSafeProjectName("My Project 123"), Is.EqualTo("My Project 123"));
    }

    [Test]
    public void MakeSafeProjectName_Empty_ReturnsProject()
    {
        Assert.That(VsTemplateParameterResolver.MakeSafeProjectName("   "), Is.EqualTo("Project"));
    }
}
