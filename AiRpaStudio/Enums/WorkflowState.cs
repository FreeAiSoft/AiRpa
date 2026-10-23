namespace RPA.Enums
{
    public enum WorkflowState
    {
        Sleep,
        Run,
        Debug,
        NeedRunSubWorkflow,
        RunSubWorkflow,
        Pause,
        AwaitDesignerReady,
        Aborted,
        StartingError,
        Complete
    }
}
