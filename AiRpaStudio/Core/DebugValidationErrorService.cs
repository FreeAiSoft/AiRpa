using System.Activities.Presentation.Validation;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RPA.Core
{
    internal class DebugValidationErrorService : IValidationErrorService
    {
        public void ShowValidationErrors(IList<ValidationErrorInfo> errors)
        {
            errors.ToList().ForEach(vei => Debug.WriteLine($"Error: {vei.Message}"));
        }
    }
}
