using ActivitiesTracker.Enums;
using RPA.Enums;
using RPA.Mcp;

namespace AiRpaStudio.Tests;

public class EnumsTests
{
    [Test]
    public void FSNodeType_Values()
    {
        Assert.That((int)FSNodeType.File, Is.EqualTo(0));
        Assert.That((int)FSNodeType.Dir, Is.EqualTo(1));
        Assert.That((int)FSNodeType.Project, Is.EqualTo(2));
        Assert.That((int)FSNodeType.Solution, Is.EqualTo(3));
        Assert.That((int)FSNodeType.VsProject, Is.EqualTo(4));
    }

    [Test]
    public void ProjectType_Values()
    {
        Assert.That((int)ProjectType.Process, Is.EqualTo(0));
        Assert.That((int)ProjectType.Library, Is.EqualTo(1));
    }

    [Test]
    public void WorkflowState_Values()
    {
        Assert.That((int)WorkflowState.Sleep, Is.EqualTo(0));
        Assert.That((int)WorkflowState.Run, Is.EqualTo(1));
        Assert.That((int)WorkflowState.Debug, Is.EqualTo(2));
        Assert.That((int)WorkflowState.NeedRunSubWorkflow, Is.EqualTo(3));
        Assert.That((int)WorkflowState.RunSubWorkflow, Is.EqualTo(4));
        Assert.That((int)WorkflowState.Pause, Is.EqualTo(5));
        Assert.That((int)WorkflowState.AwaitDesignerReady, Is.EqualTo(6));
        Assert.That((int)WorkflowState.Aborted, Is.EqualTo(7));
        Assert.That((int)WorkflowState.StartingError, Is.EqualTo(8));
        Assert.That((int)WorkflowState.Complete, Is.EqualTo(9));
    }

    [Test]
    public void ActivityState_Values()
    {
        Assert.That((int)ActivityState.Executing, Is.EqualTo(0));
        Assert.That((int)ActivityState.Closed, Is.EqualTo(1));
        Assert.That((int)ActivityState.Canceled, Is.EqualTo(2));
        Assert.That((int)ActivityState.Faulted, Is.EqualTo(3));
        Assert.That((int)ActivityState.Prestart, Is.EqualTo(4));
    }

    [Test]
    public void RpaCommonLogLevel_Values()
    {
        Assert.That((int)RpaCommon.Enums.LogLevel.Trace, Is.EqualTo(0));
        Assert.That((int)RpaCommon.Enums.LogLevel.Debug, Is.EqualTo(1));
        Assert.That((int)RpaCommon.Enums.LogLevel.Info, Is.EqualTo(2));
        Assert.That((int)RpaCommon.Enums.LogLevel.Warn, Is.EqualTo(3));
        Assert.That((int)RpaCommon.Enums.LogLevel.Error, Is.EqualTo(4));
        Assert.That((int)RpaCommon.Enums.LogLevel.Fatal, Is.EqualTo(5));
    }

    [Test]
    public void ValidationErrorType_Values()
    {
        Assert.That((int)ValidationErrorType.MissingRequiredParameter, Is.EqualTo(0));
        Assert.That((int)ValidationErrorType.InvalidParameterType, Is.EqualTo(1));
        Assert.That((int)ValidationErrorType.InvalidParameterValue, Is.EqualTo(2));
        Assert.That((int)ValidationErrorType.InvalidParameterFormat, Is.EqualTo(3));
        Assert.That((int)ValidationErrorType.UnknownParameter, Is.EqualTo(4));
    }
}
