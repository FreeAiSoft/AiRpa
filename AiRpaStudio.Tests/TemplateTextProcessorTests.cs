using RPA.VsTemplate;

namespace AiRpaStudio.Tests;

public class TemplateTextProcessorTests
{
    private static string IdentityResolver(string p) => "$" + p + "$";

    [Test]
    public void ReplaceParameters_Null_ReturnsNull()
    {
        Assert.That(TemplateTextProcessor.ReplaceParameters(null, IdentityResolver), Is.Null);
    }

    [Test]
    public void ReplaceParameters_Empty_ReturnsEmpty()
    {
        Assert.That(TemplateTextProcessor.ReplaceParameters("", IdentityResolver), Is.EqualTo(""));
    }

    [Test]
    public void ReplaceParameters_NoPlaceholder_ReturnsUnchanged()
    {
        Assert.That(TemplateTextProcessor.ReplaceParameters("no params", IdentityResolver), Is.EqualTo("no params"));
    }

    [Test]
    public void ReplaceParameters_Single_Replaces()
    {
        string Resolver(string p) => p == "name" ? "World" : p;
        Assert.That(TemplateTextProcessor.ReplaceParameters("Hello $name$!", Resolver), Is.EqualTo("Hello World!"));
    }

    [Test]
    public void ReplaceParameters_Multiple_ReplacesAll()
    {
        string Resolver(string p) => p == "a" ? "1" : p == "b" ? "2" : p;
        Assert.That(TemplateTextProcessor.ReplaceParameters("$a$-$b$", Resolver), Is.EqualTo("1-2"));
    }

    [Test]
    public void ReplaceParameters_UnmatchedDollar_ReturnsUnchanged()
    {
        int calls = 0;
        string Resolver(string p)
        {
            calls++;
            return p;
        }

        Assert.That(TemplateTextProcessor.ReplaceParameters("a$b", Resolver), Is.EqualTo("a$b"));
        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public void ProcessConditions_Null_ReturnsNull()
    {
        Assert.That(TemplateTextProcessor.ProcessConditions(null, IdentityResolver), Is.Null);
    }

    [Test]
    public void ProcessConditions_TrueCondition_KeepsBranch()
    {
        Assert.That(TemplateTextProcessor.ProcessConditions("$if$(true)yes$endif$", IdentityResolver), Is.EqualTo("yes"));
    }

    [Test]
    public void ProcessConditions_FalseCondition_DropsBranch()
    {
        Assert.That(TemplateTextProcessor.ProcessConditions("$if$(false)yes$endif$", IdentityResolver), Is.EqualTo(""));
    }

    [Test]
    public void ProcessConditions_ElseBranch_SelectedWhenFalse()
    {
        Assert.That(TemplateTextProcessor.ProcessConditions("$if$(false)a$else$b$endif$", IdentityResolver), Is.EqualTo("b"));
        Assert.That(TemplateTextProcessor.ProcessConditions("$if$(true)a$else$b$endif$", IdentityResolver), Is.EqualTo("a"));
    }

    [Test]
    public void ProcessConditions_NestedConditions_Resolved()
    {
        Assert.That(TemplateTextProcessor.ProcessConditions("$if$(true)$if$(false)a$else$b$endif$$endif$", IdentityResolver), Is.EqualTo("b"));
    }

    [Test]
    public void ProcessConditions_MissingEndif_ReturnsInputWithoutThrowing()
    {
        Assert.That(TemplateTextProcessor.ProcessConditions("$if$(true)a", IdentityResolver), Is.EqualTo("$if$(true)a"));
    }

    [Test]
    public void Process_CombinesParametersAndConditions()
    {
        string Resolver(string p) => p == "flag" ? "true" : p == "name" ? "X" : p;
        Assert.That(TemplateTextProcessor.Process("$if$($flag$)$name$$endif$", Resolver), Is.EqualTo("X"));
    }
}
