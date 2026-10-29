using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using RoslynPad.Roslyn;

namespace RPA.CSharpExpressionEditor
{
    internal class DocumentViewModel : INotifyPropertyChanged
    {
        private bool _isReadOnly;
        private readonly RoslynHost _host;
        private string _result;

#pragma warning disable IDE0052 // Remove unread private members
        private readonly RoslynExpressionEditor _roslynExpressionEditor;
#pragma warning restore IDE0052 // Remove unread private members

        public DocumentViewModel(RoslynHost host, RoslynExpressionEditor roslynExpressionEditor)
        {
            _host = host;
            _roslynExpressionEditor = roslynExpressionEditor;
        }

        internal void Initialize(DocumentId id, string text)
        {
            Id = id;
            _text = text; // Устанавливаем начальное значение
            OnPropertyChanged(nameof(Text));
        }

        public DocumentId Id { get; private set; }

        public bool IsReadOnly
        {
            get => _isReadOnly;
            private set => SetProperty(ref _isReadOnly, value);
        }

        public Script<object> Script { get; private set; }

        private string _text;
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        public bool HasError { get; private set; }

        public string Result
        {
            get => _result;
            private set => SetProperty(ref _result, value);
        }

        private static MethodInfo HasSubmissionResult { get; } =
            typeof(Compilation).GetMethod(nameof(HasSubmissionResult), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static PrintOptions PrintOptions { get; } =
            new PrintOptions { MemberDisplayFormat = MemberDisplayFormat.SeparateLines };

        public async Task<bool> TrySubmit()
        {
            Result = null;

            Script = CSharpScript.Create(Text, ScriptOptions.Default
                .WithReferences(_host.DefaultReferences)
                .WithImports(_host.DefaultImports));

            Compilation compilation = Script.GetCompilation();
            bool hasResult = (bool)HasSubmissionResult.Invoke(compilation, null);
            System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics = Script.Compile();
            if (diagnostics.Any(t => t.Severity == DiagnosticSeverity.Error))
            {
                Result = string.Join(Environment.NewLine, diagnostics.Select(FormatObject));
                return false;
            }

            IsReadOnly = true;

            await Execute(hasResult);

            return true;
        }

        private async Task Execute(bool hasResult)
        {
            try
            {
                ScriptState<object> result = await Script.RunAsync();

                if (result.Exception != null)
                {
                    HasError = true;
                    Result = FormatException(result.Exception);
                }
                else
                {
                    Result = hasResult ? FormatObject(result.ReturnValue) : null;
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                Result = FormatException(ex);
            }
        }

        private static string FormatException(Exception ex)
        {
            return CSharpObjectFormatter.Instance.FormatException(ex);
        }

        private static string FormatObject(object o)
        {
            return CSharpObjectFormatter.Instance.FormatObject(o, PrintOptions);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                // ReSharper disable once ExplicitCallerInfoArgument
                OnPropertyChanged(propertyName);
                return true;
            }
            return false;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
