using System;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Collections.Generic;
using System.ComponentModel;
using RPA.Core.Services;
using RPA.Helpers;

namespace RPA.Core
{
    public class WorkflowDesignerEx : WorkflowDesigner, INotifyPropertyChanged
    {
        internal DesignerVariableContextMenuService DesignerVariableContextMenuService { get; private set; }

        private bool _isEdited;

        public bool IsEdited
        {
            get => _isEdited;
            set
            {
                if (_isEdited != value)
                {
                    _isEdited = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEdited)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Dictionary<object, SourceLocation> DesignerSourceLocationMapping { get; } = new Dictionary<object, SourceLocation>();

        public List<SourceLocation> BreakpointList { get; set; } = new List<SourceLocation>();

        /// <summary>
        /// Путь к файлу, который загружен в дизайнер
        /// </summary>
        public string CurrentWorkflowFile => Context.Items.GetValue<WorkflowFileItem>().LoadedFile;

        public SourceLocation LastBreakpoint { get; set; }

#pragma warning disable IDE0052 // Remove unread private members
        private readonly DesignerValidator _designerCustomValidationService;
#pragma warning restore IDE0052 // Remove unread private members

        public WorkflowDesignerEx()
        {
            DesignerVariableContextMenuService = new DesignerVariableContextMenuService(this);
            ModelChanged += WorkflowDesignerEx_ModelChanged;
            _designerCustomValidationService = new DesignerValidator(this);
        }

        private void WorkflowDesignerEx_ModelChanged(object sender, EventArgs e)
        {
            IsEdited = true;
            try
            {
                DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ErrorText.Of(ex));
            }
        }
    }
}
