# RoslynExpressionEditor Context Menu Solution

## Problem
When right-clicking on the RoslynExpressionEditor component (based on AvalonEdit), the WPF designer's context menu was displayed instead of the editor's custom context menu. Previous attempts to fix this by setting `TextArea.ContextMenu = null` or using `PreviewMouseRightButtonDown` prevented the custom menu from showing.

## Diagnostic Logging

### Overview
Comprehensive diagnostic logging has been added to track all context menu-related events and help identify why the custom menu might not be appearing.

### How to View Diagnostic Output

1. **Enable Debug Output in Visual Studio**:
   - Go to `Debug` → `Windows` → `Output`
   - In the Output window, select "Debug" from the "Show output from:" dropdown

2. **Run Your Application**:
   - Start debugging your WPF application (F5)
   - Navigate to a workflow with a C# expression editor
   - Click on the expression field to enter edit mode
   - Right-click on the editor

3. **Analyze the Output**:
   - Look for boxed log entries in the Output window
   - Events are logged in this order:
     1. `PreviewMouseRightButtonDown` (TextArea)
     2. `PreviewMouseRightButtonDown` (RoslynExpressionEditorInstance)
     3. `ContextMenuOpening` (TextArea)
     4. `MouseRightButtonDown` (RoslynExpressionEditorInstance)
     5. `ContextMenuOpening` (RoslynExpressionEditorInstance)
     6. `ContextMenu.Opened` (TextArea.ContextMenu)

### What to Look For

#### 1. Context Menu Availability
```
║ TextArea.ContextMenu is null: False
║ TextArea.ContextMenu.Items.Count: 7
```
If `TextArea.ContextMenu` is NULL or has 0 items, the menu wasn't created properly.

#### 2. Event Handling Status
```
║ e.Handled (before): False
║ e.Handled (after): True
```
Check if events are being handled at the right stages.

#### 3. Menu Opening Status
```
║ TextArea.ContextMenu.IsOpen (before): False
║ TextArea.ContextMenu.IsOpen (after handler): True
```
Verify the menu is actually opening.

#### 4. Visual Tree Path
```
║ Visual Tree: TextEditor → Border → ScrollViewer → ...
```
Shows the path from the clicked element to its ancestors - helps identify if click is on the right element.

#### 5. Menu Items
```
║ Menu Items:
║   [0] Cut - Command: RoutedUICommand
║   [1] Copy - Command: RoutedUICommand
║   [2] Paste - Command: RoutedUICommand
║   [3] <Separator>
║   [4] Undo - Command: RoutedUICommand
║   [5] Redo - Command: RoutedUICommand
║   [6] <Separator>
║   [7] Select All - Command: RoutedUICommand
```
Confirms all menu items are present and properly configured.

### Common Issues to Diagnose

1. **Menu Not Created**: If `TextArea.ContextMenu is null: True` appears, check the constructor execution
2. **Events Not Firing**: If you don't see the boxed event logs, event handlers aren't attached
3. **Menu Not Opening**: If `IsOpen` stays False, something is preventing the menu from showing
4. **Wrong Source**: Check Visual Tree to ensure click is on the editor, not a parent element
5. **Event Handled Too Early**: If `e.Handled` is True before your handler runs, another handler is intercepting it


## Solution Overview
The solution implements a multi-layered approach to intercept right-click events and prevent them from bubbling to the designer while still allowing the editor's context menu to display:

### 1. RoslynExpressionEditorInstance Changes

**File:** `CSharpExpressionEditor/RoslynExpressionEditorInstance.cs`

- **Custom Context Menu Creation**: Creates a context menu with Cut, Copy, Paste, Undo, Redo, and Select All commands
- **Event Handlers**:
  - `MouseRightButtonDown`: Prevents right-click from bubbling to designer AFTER menu is shown
  - `ContextMenuOpening`: Stops context menu event from propagating to parent controls
  - `TextArea.ContextMenuOpening`: Handles context menu opening on TextArea level

Key code:
```csharp
// Create and set context menu for the TextArea
var editorContextMenu = CreateEditorContextMenu();
TextArea.ContextMenu = editorContextMenu;

// Handle MouseRightButtonDown (not Preview) to show menu and prevent bubbling
MouseRightButtonDown += OnMouseRightButtonDown;

// Handle ContextMenuOpening to prevent bubbling to parent/designer
ContextMenuOpening += OnContextMenuOpening;
TextArea.ContextMenuOpening += OnTextAreaContextMenuOpening;
```

### 2. XAML Changes

**File:** `CSharpExpressionEditor/RoslynExpressionEditor.xaml`

- Added `MouseRightButtonDown` handlers on Border elements (NOT PreviewMouseRightButtonDown)
- This allows the click to propagate to child controls first, then stops it from going to designer

Key changes:
```xml
<!-- Main border -->
<Border ... MouseRightButtonDown="OnMainBorderRightClick">

<!-- Editor border in expressioneditor template -->
<Border ... MouseRightButtonDown="OnEditorBorderRightClick" />
```

### 3. Code-Behind Changes

**File:** `CSharpExpressionEditor/RoslynExpressionEditor.xaml.cs`

Event handlers that prevent bubbling to designer:

```csharp
private void OnMainBorderRightClick(object sender, MouseButtonEventArgs e)
{
    // Only mark as handled if the click originated on this border
    if (e.Source == sender)
    {
        e.Handled = true;
    }
}

private void OnEditorBorderRightClick(object sender, MouseButtonEventArgs e)
{
    // Prevent right-click from bubbling to designer
    e.Handled = true;
}

private void OnBorderContextMenuOpening(object sender, ContextMenuEventArgs e)
{
    // Prevent context menu from bubbling up to designer
    e.Handled = true;
}
```

## How It Works

1. **User right-clicks on editor**:
   - Click event propagates through the visual tree
   - TextArea shows its context menu (because ContextMenu property is set)
   
2. **Context menu opening**:
   - `TextArea.ContextMenuOpening` fires and is handled (e.Handled = true)
   - This prevents the event from bubbling up to parent controls
   
3. **After menu is shown**:
   - `MouseRightButtonDown` event fires on RoslynExpressionEditorInstance
   - Handler marks event as handled, preventing designer from receiving it
   
4. **Border level protection**:
   - If click somehow reaches Border elements, their handlers stop it from going further
   - `OnMainBorderRightClick` checks if source is the border itself to avoid blocking child controls

## Key Differences from Previous Attempts

### What Didn't Work:
- ❌ Setting `TextArea.ContextMenu = null` - prevented menu from showing
- ❌ Using `PreviewMouseRightButtonDown` - blocked menu before it could open
- ❌ Setting `ContextMenuService.IsEnabled="False"` - completely disabled menu functionality

### What Works:
- ✅ Using `MouseRightButtonDown` instead of Preview version - allows menu to show first
- ✅ Setting `TextArea.ContextMenu` to custom menu - provides the menu to display
- ✅ Handling `ContextMenuOpening` event - stops propagation to designer
- ✅ Multi-layer event handling - ensures designer menu never shows

## Testing

To verify the solution works:

1. Open a workflow in the designer
2. Add an activity with C# expression editor (e.g., Assign activity)
3. Click on expression field to enter edit mode
4. Right-click on the editor
5. **Expected result**: Custom context menu with Cut/Copy/Paste/Undo/Redo/Select All appears
6. **Not expected**: Designer's context menu should NOT appear
7. Editor should remain in focus after right-click

## Benefits

- ✅ Custom context menu displays correctly
- ✅ Designer context menu is blocked
- ✅ Editor maintains focus on right-click
- ✅ No loss of editing functionality
- ✅ Clean event handling without side effects

## Technical Notes

- Event routing order: PreviewMouseRightButtonDown → MouseRightButtonDown → ContextMenuOpening
- By handling events at the right stages, we allow menu to show but prevent designer interaction
- The solution is compatible with WPF event bubbling/tunneling model
- No modification to AvalonEdit library required