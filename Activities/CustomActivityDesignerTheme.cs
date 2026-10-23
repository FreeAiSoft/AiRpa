using System.Drawing;
using System.Drawing.Drawing2D;
using System.Workflow.ComponentModel.Design;

namespace Activities
{
    public class CustomActivityDesignerTheme : ActivityDesignerTheme
    {
        public CustomActivityDesignerTheme(WorkflowTheme theme)
            : base(theme)
        {
            base.Initialize();
            BorderColor = Color.FromArgb(0, 0, 0);
            BackColorStart = Color.Black;
            BackColorEnd = Color.Black;
            BackgroundStyle = LinearGradientMode.Vertical;
            ForeColor = Color.Black;
        }
    }
}
