using System;
using System.Activities.Presentation;
using System.Activities.Presentation.Hosting;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Открытие loose-XAML в настоящем <c>WorkflowDesigner</c>: публикует
    /// <see cref="LooseXamlDocumentPersistenceService"/> до <see cref="WorkflowDesigner.Load(string)"/>
    /// и вручную выставляет <see cref="WorkflowFileItem"/> (при <see cref="IDocumentPersistenceService"/>
    /// дизайнер сам его в <c>Context.Items</c> не кладёт).
    /// <para>
    /// Единая точка для студии и тестов: иначе «работает в студии, ломается в стенде» станет вопросом времени.
    /// </para>
    /// </summary>
    internal static class LooseXamlDesignerLoader
    {
        internal static void Open(WorkflowDesigner designer, string sourceFile, WorkflowTypeRegistry registry)
        {
            if (designer == null)
            {
                throw new ArgumentNullException(nameof(designer));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var persistence = new LooseXamlDocumentPersistenceService(
                registry,
                text => designer.Text = text);

            designer.Context.Services.Publish<IDocumentPersistenceService>(persistence);
            designer.Load(sourceFile);
            designer.Context.Items.SetValue(new WorkflowFileItem { LoadedFile = sourceFile });
        }
    }
}
