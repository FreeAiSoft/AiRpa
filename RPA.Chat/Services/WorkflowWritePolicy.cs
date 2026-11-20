using Agent.Core.Models;
using Agent.Core.Services;
using RPA.Shared.Models;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Единственная точка перевода трёх переключателей способа написания WF в маску
    /// <see cref="WorkflowWriteWay"/> (план «3 галочки»).
    /// <para>
    /// Текст XAML (инструмент <c>editor</c>) сюда не входит — он остаётся нативным путём чата и
    /// управляется <c>AllowEditFiles</c> + <c>editor</c>. Галочки фильтруют только MCP-способы:
    /// палитру, JSON AST и code-first.
    /// </para>
    /// <para>
    /// RPA-флаги больше не живут в канонических настройках тулкита: их хранит
    /// <see cref="RpaAgentOptions"/>, а <paramref name="settings"/> по-прежнему используется как
    /// страж «настройки не прочитаны» — <c>null</c> даёт пустую маску.
    /// </para>
    /// </summary>
    public static class WorkflowWritePolicy
    {
        /// <summary>
        /// Собирает маску способов из RPA-флагов. <c>null</c> настроек даёт пустую маску:
        /// ранняя инициализация не должна превращаться во «все способы включены».
        /// </summary>
        public static WorkflowWriteWay Resolve(SettingsData settings)
        {
            if (settings == null)
            {
                return WorkflowWriteWay.None;
            }

            var ways = WorkflowWriteWay.None;

            if (RpaAgentOptions.AllowWfPalette) ways |= WorkflowWriteWay.Palette;
            if (RpaAgentOptions.AllowWfJsonAst) ways |= WorkflowWriteWay.JsonAst;
            if (RpaAgentOptions.AllowWfCodeFirst) ways |= WorkflowWriteWay.CodeFirst;

            return ways;
        }
    }
}
