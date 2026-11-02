using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using RPA.Entities;
using RPA.Helpers;

namespace RPA.Controls
{
    public class TextBoxFocusBehavior : Behavior<TextBox>
    {
        private bool _isRightMouseButtonDown;

        protected override void OnAttached()
        {
            Debug.WriteLine("[TextBoxFocusBehavior] OnAttached");
            AssociatedObject.PreviewMouseRightButtonDown += AssociatedObject_PreviewMouseRightButtonDown;
            AssociatedObject.PreviewMouseRightButtonUp += AssociatedObject_PreviewMouseRightButtonUp;
            AssociatedObject.MouseRightButtonDown += AssociatedObject_MouseRightButtonDown;
            AssociatedObject.PreviewLostKeyboardFocus += AssociatedObject_PreviewLostKeyboardFocus;
            AssociatedObject.LostFocus += AssociatedObject_LostFocus;
            AssociatedObject.LostKeyboardFocus += AssociatedObject_LostKeyboardFocus;
            base.OnAttached();
        }

        protected override void OnDetaching()
        {
            Debug.WriteLine("[TextBoxFocusBehavior] OnDetaching");
            AssociatedObject.PreviewMouseRightButtonDown -= AssociatedObject_PreviewMouseRightButtonDown;
            AssociatedObject.PreviewMouseRightButtonUp -= AssociatedObject_PreviewMouseRightButtonUp;
            AssociatedObject.MouseRightButtonDown -= AssociatedObject_MouseRightButtonDown;
            AssociatedObject.PreviewLostKeyboardFocus -= AssociatedObject_PreviewLostKeyboardFocus;
            AssociatedObject.LostFocus -= AssociatedObject_LostFocus;
            AssociatedObject.LostKeyboardFocus -= AssociatedObject_LostKeyboardFocus;
            base.OnDetaching();
        }

        private void AssociatedObject_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("[TextBoxFocusBehavior] PreviewMouseRightButtonDown - Setting flag");
            // Set flag to prevent focus loss handling
            _isRightMouseButtonDown = true;

            // Keep focus on the TextBox
            if (sender is TextBox)
            {
                e.Handled = false; // Allow event to bubble for context menu
            }
        }

        private void AssociatedObject_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("[TextBoxFocusBehavior] MouseRightButtonDown");
            _isRightMouseButtonDown = true;
        }

        private void AssociatedObject_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("[TextBoxFocusBehavior] PreviewMouseRightButtonUp - Resetting flag");
            // Reset flag after right-click with a small delay
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    _isRightMouseButtonDown = false;
                    Debug.WriteLine("[TextBoxFocusBehavior] Flag reset (delayed)");
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void AssociatedObject_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Debug.WriteLine($"[TextBoxFocusBehavior] PreviewLostKeyboardFocus - RightButtonDown: {_isRightMouseButtonDown}");

            // Cancel focus loss if right mouse button is down
            if (_isRightMouseButtonDown)
            {
                Debug.WriteLine("[TextBoxFocusBehavior] Canceling focus loss due to right click");
                e.Handled = true;

                // Re-focus after a short delay to ensure TextBox keeps focus
                if (sender is TextBox textBox)
                {
                    _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        new System.Action(() =>
                        {
                            _ = textBox.Focus();
                            Debug.WriteLine("[TextBoxFocusBehavior] Re-focused TextBox");
                        }),
                        System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        private void AssociatedObject_LostFocus(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"[TextBoxFocusBehavior] LostFocus - RightButtonDown: {_isRightMouseButtonDown}");
            HandleFocusLoss(sender);
        }

        private void AssociatedObject_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Debug.WriteLine($"[TextBoxFocusBehavior] LostKeyboardFocus - RightButtonDown: {_isRightMouseButtonDown}");
            HandleFocusLoss(sender);
        }

        private void HandleFocusLoss(object sender)
        {
            // Don't handle focus loss if it's caused by right mouse button
            if (_isRightMouseButtonDown)
            {
                Debug.WriteLine("[TextBoxFocusBehavior] Skipping HandleFocusLoss due to right click");
                return;
            }

            if (sender is TextBox textBox && textBox.Tag is FSNode fSNode)
            {
                Debug.WriteLine($"[TextBoxFocusBehavior] HandleFocusLoss - Node: {fSNode.Name}, IsEditing: {fSNode.IsEditing}");
                if (fSNode.IsEditing)
                {
                    Debug.WriteLine("[TextBoxFocusBehavior] Committing rename");
                    // Find the parent control to call CommitRename
                    ProjectManagerControl parentControl = FindVisualParent<ProjectManagerControl>(textBox);
                    parentControl?.CommitRename(fSNode, textBox.Text);
                }
            }
        }

        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            return XamlHelper.FindVisualParent<T>(child);
        }
    }
}
