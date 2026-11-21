using System.Activities;

namespace ActivitiesTracker.Entities
{
    public class LocalVar
    {
        public string VarName { get; set; }

        public object Data { get; set; }

        public string TypeName { get; set; }

        public string Scope { get; set; }

        /// <summary>Категория элемента (переменная / аргумент / аргумент делегата / this).</summary>
        public LocalVarKind Kind { get; set; }

        /// <summary>Направление аргумента; для переменных и <c>this</c> — <c>null</c>.</summary>
        public ArgumentDirection? Direction { get; set; }

        /// <summary>Состояние активности, из которого получен снимок (Executing/Closed/...).</summary>
        public string ActivityState { get; set; }

        /// <summary>Признак «значение ещё не присвоено» (Out-аргумент на состоянии Executing).</summary>
        public bool IsUnassigned { get; set; }

        public LocalVar(
            string varName,
            object data,
            string typeName,
            string scope,
            LocalVarKind kind = LocalVarKind.Variable,
            ArgumentDirection? direction = null,
            string activityState = null,
            bool isUnassigned = false)
        {
            VarName = varName;
            Data = data;
            TypeName = typeName;
            Scope = scope;
            Kind = kind;
            Direction = direction;
            ActivityState = activityState;
            IsUnassigned = isUnassigned;
        }
    }
    /// <summary>
    /// Категория элемента панели «Локальные переменные».
    /// </summary>
    public enum LocalVarKind
    {
        /// <summary>Публичная или implementation-переменная.</summary>
        Variable,

        /// <summary>Аргумент активности (<see cref="System.Activities.RuntimeArgument"/>).</summary>
        Argument,

        /// <summary>Аргумент делегата (<see cref="System.Activities.RuntimeDelegateArgument"/>).</summary>
        DelegateArgument,

        /// <summary>Псевдопеременная <c>this</c>.</summary>
        This
    }


}
