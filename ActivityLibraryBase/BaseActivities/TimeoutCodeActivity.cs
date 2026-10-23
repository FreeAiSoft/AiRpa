using System;
using System.Activities;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Operations;
using ActivityLibraryBase.Resources;

namespace ActivityLibraryBase.BaseActivities
{
    public abstract class TimeoutCodeActivity : BaseCodeActivity
    {
        [LocalizedCategory("activities_base_timings", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_delay_after_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_delay_after", typeof(ActivityLibraryBaseResorces))]
        public InArgument<int> DelayAfter { get; set; } = 0;

        [LocalizedCategory("activities_base_timings", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_delay_before_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_delay_before", typeof(ActivityLibraryBaseResorces))]
        public InArgument<int> DelayBefore { get; set; } = 0;

        [LocalizedCategory("activities_base_timings", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_timeout_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_timeout", typeof(ActivityLibraryBaseResorces))]
        public InArgument<int> Timeout { get; set; } = 30;

        public abstract void RunTimeoutOperation(CodeActivityContext context);

        public override void Run(CodeActivityContext context)
        {
            int delayAfter = DelayAfter.Get(context);
            int delayBefore = DelayBefore.Get(context);
            int timeout = Timeout.Get(context);

            IOperationInvoker invoker = new TimingOperation();
            Timing timing = new Timing()
            {
                DelayAfter = TimeSpan.FromSeconds(delayAfter),
                DelayBefore = TimeSpan.FromSeconds(delayBefore),
                Timeout = TimeSpan.FromSeconds(timeout)
            };
            invoker.InvokeAction(
                () => RunTimeoutOperation(context),
                timing,
                nameof(Run),
                false);
        }
    }
}
