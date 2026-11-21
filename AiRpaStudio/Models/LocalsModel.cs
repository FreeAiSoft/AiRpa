using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ActivitiesTracker.Entities;
using Agent.Mvvm;
using RPA.Locals;

namespace RPA.Models
{
    public class LocalsModel : BaseModel
    {
        private readonly ValueInspector inspector;
        private LocalsNode selectedNode;
        private string activityDisplayName;

        public ObservableCollection<LocalsNode> Roots { get; } = new ObservableCollection<LocalsNode>();

        public LocalsNode SelectedNode
        {
            get => selectedNode;
            set
            {
                if (ReferenceEquals(selectedNode, value))
                {
                    return;
                }

                selectedNode = value;
                OnPropertyChanged();
            }
        }

        public string ActivityDisplayName
        {
            get => activityDisplayName;
            set
            {
                if (string.Equals(activityDisplayName, value, StringComparison.Ordinal))
                {
                    return;
                }

                activityDisplayName = value;
                OnPropertyChanged();
            }
        }

        public LocalsModel()
            : this(new ValueInspector())
        {
        }

        public LocalsModel(ValueInspector inspector)
        {
            this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        }

        public void Update(LocalsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                Clear();
                return;
            }

            IReadOnlyList<LocalsNode> nodes = inspector.BuildNodes(snapshot.Locals);

            Roots.Clear();
            foreach (LocalsNode node in nodes)
            {
                node.IsExpanded = true;
                Roots.Add(node);
            }

            ActivityDisplayName = snapshot.ActivityDisplayName;
            SelectedNode = null;
        }

        public void Clear()
        {
            Roots.Clear();
            ActivityDisplayName = null;
            SelectedNode = null;
        }
    }
}
