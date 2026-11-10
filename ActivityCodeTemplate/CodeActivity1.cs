using System.Activities;
using System.ComponentModel;
using ActivityLibraryBase.BaseActivities;

namespace ActivityCodeTemplate
{
    /// <summary>
    /// Шаблон активности. Наследуйтесь от <see cref="TimeoutCodeActivity"/> (таймаут, задержки
    /// и стандартные свойства IsEnabled/IsNeedLogging/IsNeedTrace) или от <see cref="BaseCodeActivity"/>,
    /// если таймаут не нужен.
    /// </summary>
    [Description("Шаблон активности — замените описание.")]
    public sealed class CodeActivity1 : TimeoutCodeActivity
    {
        [RequiredArgument]
        [Description("Первый входной аргумент.")]
        public InArgument<string> Value1 { get; set; }

        [Description("Второй входной аргумент.")]
        public InArgument<string> Value2 { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            if (!IsEnabled)
            {
                return;
            }

            string value1 = Value1.Get(context);
            string value2 = Value2.Get(context);

            if (IsNeedLogging)
            {
                ActivityLogger.Info($"{value1}{value2}");
            }
        }
    }
}