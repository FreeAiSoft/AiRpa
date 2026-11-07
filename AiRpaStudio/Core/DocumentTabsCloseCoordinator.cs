using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using AvalonDock.Layout;

namespace RPA.Core
{
    /// <summary>
    /// Активация соседней вкладки после закрытия в панели документов студии.
    /// <para>
    /// В панели лежат вкладки обоих видов — дизайнеры (<c>DocGroupHelper.AddDocument</c>, путь к файлу
    /// в <c>FrameworkElement.Tag</c>) и редакторы кода/XAML (<c>MainView.AddCodeDocumentTab</c>,
    /// <c>ContentId</c> = <c>code:&lt;путь&gt;</c>), поэтому «сосед» считается по фактическому порядку
    /// панели, а не по коллекции дизайнеров. Само правило — <see cref="DocumentTabPolicy"/>.
    /// </para>
    /// <para>
    /// Почему активация отложена: при закрытии вкладки сначала приходит <c>Closing</c> (здесь
    /// запоминается сосед), затем AvalonDock удаляет вкладку и поднимает <c>Closed</c>, из которого
    /// студия убирает документ из модели (<c>CmdRemoveDesigner</c>/<c>CmdCloseCodeDocument</c>), а
    /// модель при этом выбирает вкладку по-своему. Поэтому сосед активируется отдельным шагом —
    /// <see cref="ApplyPendingActivation"/> — после того, как закрытие в модели закончилось: для
    /// вкладки редактора кода сразу после удаления из панели, для вкладки дизайнера — из обработчика
    /// <c>MainView.WorkflowDesigners_CollectionChanged_ActivateNeighbour</c>.
    /// </para>
    /// </summary>
    internal sealed class DocumentTabsCloseCoordinator
    {
        /// <summary>Префикс <c>ContentId</c> вкладки редактора кода/XAML (<c>MainView.AddCodeDocumentTab</c>).</summary>
        internal const string CodeContentIdPrefix = "code:";

        private readonly LayoutDocumentPane pane;
        private LayoutDocument pending;

        /// <summary>
        /// Следит за панелью документов: новые вкладки подписываются на <c>Closing</c>, а удаление
        /// вкладки редактора кода сразу активирует соседнюю (модель у такой вкладки ничего не выбирает).
        /// </summary>
        internal DocumentTabsCloseCoordinator(LayoutDocumentPane pane)
        {
            this.pane = pane ?? throw new ArgumentNullException(nameof(pane));
            pane.Children.CollectionChanged += Children_CollectionChanged;
            AttachAll();
        }

        /// <summary>Вкладки панели в фактическом (визуальном) порядке, включая редакторы кода.</summary>
        internal IReadOnlyList<LayoutDocument> Tabs => pane.Children.OfType<LayoutDocument>().ToList();

        /// <summary>Вкладка, которую надо активировать после закрытия, или <c>null</c>.</summary>
        internal LayoutDocument PendingActivation => pending;

        /// <summary>
        /// Запоминает соседнюю вкладку, если закрывается именно активная вкладка: закрытие фоновой
        /// вкладки активную не меняет (как в Visual Studio).
        /// </summary>
        internal void OnClosing(LayoutDocument closing)
        {
            if (closing == null)
            {
                return;
            }

            if (!IsActive(closing))
            {
                // Закрытие фоновой вкладки активную не меняет: если активации ждала другая вкладка,
                // её отменяем — соседа выбирают только для того закрытия, которое идёт сейчас.
                pending = null;
                return;
            }

            pending = DocumentTabPolicy.NextActive(Tabs, closing);
        }

        /// <summary>
        /// Активирует запомненную соседнюю вкладку. Возвращает активированную вкладку или <c>null</c>,
        /// если активировать нечего (вкладка была последней, соседа уже закрыли, панель сброшена).
        /// </summary>
        internal LayoutDocument ApplyPendingActivation()
        {
            LayoutDocument next = pending;
            pending = null;

            if (next == null || !pane.Children.Contains(next))
            {
                return null;
            }

            // IsSelected — это видимая вкладка: TabItem.IsSelected привязан к нему TwoWay-биндингом
            // (ItemContainerStyle темы AvalonDock). IsActive — активный документ корня раскладки.
            next.IsActive = true;
            next.IsSelected = true;
            return next;
        }

        /// <summary>Забыть запомненную вкладку: новый проект, сброс панели документов.</summary>
        internal void Reset()
        {
            pending = null;
        }

        /// <summary>Вкладка активна (выбрана или является активным документом раскладки).</summary>
        internal static bool IsActive(LayoutDocument document)
        {
            return document != null && (document.IsSelected || document.IsActive);
        }

        /// <summary>Вкладка редактора кода/XAML (у неё <c>ContentId</c> с префиксом <c>code:</c>).</summary>
        internal static bool IsCodeTab(LayoutDocument document)
        {
            string contentId = document?.ContentId;
            return !string.IsNullOrEmpty(contentId)
                && contentId.StartsWith(CodeContentIdPrefix, StringComparison.Ordinal);
        }

                /// <summary>
        /// Активная вкладка панели: выбранная, а если два флага разошлись — активный документ раскладки.
        /// <para>
        /// После исправления панели документов (LayoutDocumentPane больше не обнуляет
        /// SelectedContentIndex) IsActive влечёт IsSelected, поэтому расхождение означает унаследованное
        /// сломанное состояние — тогда правдой считается активный документ раскладки: его ставит и клик по
        /// вкладке (LayoutDocumentTabItem), и SetSelectedTab при восстановлении сессии.
        /// </para>
        /// </summary>
        internal static LayoutDocument FindActive(IEnumerable<LayoutDocument> tabs)
        {
            List<LayoutDocument> list = tabs?.ToList() ?? new List<LayoutDocument>();
            LayoutDocument selected = list.FirstOrDefault(document => document.IsSelected);
            LayoutDocument active = list.FirstOrDefault(document => document.IsActive);

            if (selected != null && active != null && !ReferenceEquals(selected, active))
            {
                return active;
            }

            return selected ?? active;
        }

        /// <summary>
        /// Путь к файлу вкладки: у дизайнера — путь в <c>Tag</c> поверхности, у редактора кода —
        /// <c>ContentId</c> без префикса <c>code:</c>; <c>null</c>, если вкладка без файла.
        /// </summary>
        internal static string PathOf(LayoutDocument document)
        {
            if (document == null)
            {
                return null;
            }

            if (document.Content is FrameworkElement view && view.Tag is string loadedFile && loadedFile.Length > 0)
            {
                return loadedFile;
            }

            string contentId = document.ContentId;
            if (IsCodeTab(document) && contentId.Length > CodeContentIdPrefix.Length)
            {
                return contentId.Substring(CodeContentIdPrefix.Length);
            }

            return null;
        }

        private void AttachAll()
        {
            foreach (LayoutDocument document in pane.Children.OfType<LayoutDocument>())
            {
                Attach(document);
            }
        }

        private void Attach(LayoutDocument document)
        {
            if (document == null)
            {
                return;
            }

            // Обработчик ставится ровно один раз на вкладку: повторная подписка дала бы двойной выбор.
            document.Closing -= Document_Closing;
            document.Closing += Document_Closing;
        }

        private void Document_Closing(object sender, CancelEventArgs e)
        {
            OnClosing(sender as LayoutDocument);
        }

        private void Children_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                pending = null;
                AttachAll();
                return;
            }

            if (e.NewItems != null)
            {
                foreach (LayoutDocument added in e.NewItems.OfType<LayoutDocument>())
                {
                    Attach(added);
                }
            }

            if (e.Action != NotifyCollectionChangedAction.Remove || e.OldItems == null)
            {
                return;
            }

            // Вкладка редактора кода: модель её уже убрала (закрытие идёт из Closed → CmdCloseCodeDocument)
            // и активную вкладку больше никто не переставляет — активируем соседа сразу.
            // У вкладки дизайнера последнее слово за моделью (DocClosed → CmdRemoveDesigner), поэтому
            // там соседа активирует MainView после удаления документа из WorkflowDesigners.
            if (e.OldItems.OfType<LayoutDocument>().Any(IsCodeTab))
            {
                _ = ApplyPendingActivation();
            }
        }
    }
}
