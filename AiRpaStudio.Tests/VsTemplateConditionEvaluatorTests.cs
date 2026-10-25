using RPA.VsTemplate;

namespace AiRpaStudio.Tests;

public class VsTemplateConditionEvaluatorTests
{
    private static string Resolve(string name)
    {
        switch (name)
        {
            case "targetframeworkversion": return "4.8";
            default: return "$" + name + "$";
        }
    }

    [Test]
    public void Evaluate_GreaterOrEqual_True()
    {
        Assert.That(VsTemplateConditionEvaluator.Evaluate("($targetframeworkversion$ >= 4.5)", Resolve), Is.True);
    }

    [Test]
    public void Evaluate_LessThan_False()
    {
        Assert.That(VsTemplateConditionEvaluator.Evaluate("($targetframeworkversion$ < 4.5)", Resolve), Is.False);
    }

    [Test]
    public void Evaluate_Equality_WithVersion()
    {
        Assert.That(VsTemplateConditionEvaluator.Evaluate("($targetframeworkversion$ == 4.8)", Resolve), Is.True);
        Assert.That(VsTemplateConditionEvaluator.Evaluate("($targetframeworkversion$ == 4.7)", Resolve), Is.False);
    }

    [Test]
    public void Evaluate_NotEqual_WithVersion()
    {
        Assert.That(VsTemplateConditionEvaluator.Evaluate("($targetframeworkversion$ != 4.7)", Resolve), Is.True);
    }

    [Test]
    public void Evaluate_StringComparison()
    {
        string ResolveType(string name) => name == "projecttype" ? "CSharp" : "$" + name + "$";

        Assert.That(VsTemplateConditionEvaluator.Evaluate("($projecttype$ == \"CSharp\")", ResolveType), Is.True);
    }

    [Test]
    public void Process_InlineCondition_SelectsBranch()
    {
        Assert.That(
            TemplateTextProcessor.Process("x$if$ ($targetframeworkversion$ >= 4.5)y$endif$z", Resolve),
            Is.EqualTo("xyz"));
    }

    [Test]
    public void Process_ElseBranch_SelectedWhenFalse()
    {
        Assert.That(
            TemplateTextProcessor.Process("$if$ ($targetframeworkversion$ < 4.5)A$else$B$endif$", Resolve),
            Is.EqualTo("B"));
    }

    [Test]
    public void Process_NestedConditions()
    {
        string input = "$if$ ($targetframeworkversion$ >= 4.5)outer[$if$ ($targetframeworkversion$ >= 4.7)inner$endif$]$endif$";

        Assert.That(TemplateTextProcessor.Process(input, Resolve), Is.EqualTo("outer[inner]"));
    }

    [Test]
    public void Process_RemovesRemainingParameters()
    {
        Assert.That(
            TemplateTextProcessor.Process("Hello $targetframeworkversion$!", Resolve),
            Is.EqualTo("Hello 4.8!"));
    }
}
