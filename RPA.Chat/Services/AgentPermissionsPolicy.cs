using Agent.Core.Models;

namespace RPA.Chat.Services
{
    /// <summary>
    /// AG-10: единственная точка перевода переключателей настроек в маску разрешений агента.
    /// <para>
    /// Маска разрешений решает две вещи сразу: какие инструменты попадают в промпт и какие вообще
    /// исполняются (<c>AgentToolRegistry.ForMode(mode, allowed)</c>). Пока это правило жило приватным
    /// методом в <c>ChatViewModel</c>, его нельзя было проверить без экземпляра модели чата, поэтому
    /// «выключил правку файлов, а мутирующие WF-инструменты остались» ловилось только руками.
    /// </para>
    /// <para>
    /// Правила: отсутствие настроек — «ничего не разрешено» (никогда не полный доступ), выключенный
    /// переключатель не оставляет категорию в маске, включённый добавляет ровно одну категорию.
    /// Страж — <c>AiRpaStudio.Tests\Workflow\WorkflowToolRegistryGuardTests.cs</c> и
    /// <c>AiRpaStudio.Tests\ChatViewModelTests.cs</c>.
    /// </para>
    /// </summary>
    public static class AgentPermissionsPolicy
    {
        /// <summary>
        /// Собирает маску разрешений из настроек чата. <c>null</c> настроек даёт пустую маску:
        /// ранняя инициализация не должна превращаться в полный доступ к инструментам.
        /// </summary>
        public static AgentPermissions Resolve(SettingsData settings)
        {
            if (settings == null)
            {
                return (AgentPermissions)0;
            }

            var allowed = (AgentPermissions)0;

            if (settings.AllowReadFiles) allowed |= AgentPermissions.ReadFiles;
            if (settings.AllowEditFiles) allowed |= AgentPermissions.EditFiles;
            if (settings.AllowExecuteCommand) allowed |= AgentPermissions.ExecuteCommand;
            if (settings.AllowWebFetch) allowed |= AgentPermissions.WebFetch;
            if (settings.AllowSubagents) allowed |= AgentPermissions.Subagents;

            return allowed;
        }
    }
}
