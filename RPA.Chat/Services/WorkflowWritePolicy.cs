using Agent.Core.Models;

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
    /// </summary>
    public static class WorkflowWritePolicy
    {
        /// <summary>
        /// Собирает маску способов из настроек чата. <c>null</c> настроек даёт пустую маску:
        /// ранняя инициализация не должна превращаться во «все способы включены».
        /// </summary>
        public static WorkflowWriteWay Resolve(SettingsData settings)
        {
            if (settings == null)
            {
                return WorkflowWriteWay.None;
            }

            var ways = WorkflowWriteWay.None;

            if (settings.AllowWfPalette) ways |= WorkflowWriteWay.Palette;
            if (settings.AllowWfJsonAst) ways |= WorkflowWriteWay.JsonAst;
            if (settings.AllowWfCodeFirst) ways |= WorkflowWriteWay.CodeFirst;

            return ways;
        }
    }
}
