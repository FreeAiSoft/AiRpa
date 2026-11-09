using System;

using System.Activities.Presentation.Model;
using System.Collections.Generic;

using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using RPA.Core.Services;

namespace RPA.CSharpExpressionEditor
{
    public class RoslynExpressionValidator
    {
        public static RoslynExpressionValidator Instance { get; } = new RoslynExpressionValidator();

        internal string UsingNamespaces { get; set; } = string.Empty;

        /// <summary>
        /// Ссылки для компиляции выражения. Создаются один раз и только читаются, поэтому безопасны
        /// для параллельных вызовов: раньше компиляция была одна на синглтон (<c>_compilation</c>)
        /// и в неё складывались синтаксические деревья всех вызовов сразу.
        /// </summary>
        private static readonly MetadataReference[] References = CreateReferences();

        private RoslynExpressionValidator()
        {
        }

        private static MetadataReference[] CreateReferences()
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DataTable).GetTypeInfo().Assembly.Location),

            };
        }

        /// <summary>
        /// Своя компиляция на вызов. Общий экземпляр <see cref="CSharpCompilation"/> нельзя держать
        /// в поле синглтона: проверка выражений идёт в фоновой задаче дизайнера, и два прохода
        /// (дизайнер и редактор выражения) писали бы в одну компиляцию одновременно.
        /// </summary>
        private static CSharpCompilation CreateCompilation()
        {
            return CSharpCompilation.Create("DynamicCompilation")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(References);
        }

        /// <summary>
        /// Имя типа в синтаксисе C#: <c>System.Int32</c>,
        /// <c>System.Collections.Generic.List&lt;System.String&gt;</c>.
        /// <para>
        /// Раньше подставлялось <c>Type.Name</c> («Int32», «List`1»): первое работало только при
        /// подходящем блоке <c>using</c>, второе не компилировалось вовсе — выражения с переменными
        /// обобщённых типов давали ложную ошибку. Полное имя снимает обе проблемы и делает проверку
        /// независимой от набора импортов документа.
        /// </para>
        /// </summary>
        internal static string CSharpTypeName(Type type)
        {
            if (type == null)
            {
                return "object";
            }

            if (type.IsArray)
            {
                return CSharpTypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                string definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
                int marker = definition.IndexOf('`');
                if (marker > 0)
                {
                    definition = definition.Substring(0, marker);
                }

                string arguments = string.Join(", ", type.GetGenericArguments().Select(CSharpTypeName));

                return definition.Replace('+', '.') + "<" + arguments + ">";
            }

            string fullName = type.FullName;
            return !string.IsNullOrEmpty(fullName) ? fullName.Replace('+', '.') : type.Name;
        }

        /// <summary>
        /// Параметры метода-обёртки: «Тип имя» для каждого имени окружения (аргументы процесса и
        /// переменные в области видимости). Порядок — порядок объявления, как в прежнем коде.
        /// </summary>
        private static List<string> BuildParameterList(ExpressionEnvironment environment)
        {
            List<string> parameters = new List<string>();
            if (environment == null)
            {
                return parameters;
            }

            foreach (KeyValuePair<string, Type> pair in environment.Names)
            {
                parameters.Add(CSharpTypeName(pair.Value) + " " + pair.Key);
            }

            return parameters;
        }

        /// <summary>
        /// Проверка выражения в окружении активности. Вход для редактора выражений: он работает на
        /// UI-потоке и передаёт сам <see cref="ModelItem"/>.
        /// </summary>
        public (Type type, List<string> errors) CheckExpression(string expressionText, ModelItem ownerActivity)
        {
            ExpressionEnvironment environment = ModelExpressionEnvironment.FromModelItem(ownerActivity, UsingNamespaces);
            return CheckExpression(expressionText, environment);
        }

        /// <summary>
        /// Компиляция выражения по снимку окружения — без <c>ModelItem</c> и без общего состояния.
        /// <para>
        /// Именно этот вход использует фоновая валидация дизайнера (<c>ExpressionErrorEvaluator</c>):
        /// окружение снимается на UI-потоке один раз на активность, а компиляция идёт в фоновой задаче.
        /// Метод статический и не читает ни одного изменяемого поля, поэтому вызывать его можно из
        /// нескольких потоков сразу (раньше блок <c>using</c> и компиляция были полями синглтона).
        /// </para>
        /// </summary>
        internal static (Type type, List<string> errors) CheckExpression(
            string expressionText,
            ExpressionEnvironment environment)
        {
            string parametersCode = string.Join(", ", BuildParameterList(environment));

            // Обертка: используем параметры метода
            string code = (environment?.UsingNamespaces ?? string.Empty) +
                $@"class ExpressionValidator {{ 
                    // Компилятор Roslyn предполагает, что эти параметры будут предоставлены
                    public static object Evaluate({parametersCode}) {{ 
                        return {expressionText}; 
                    }} 
                }}";

            // Оборачиваем выражение в метод, который должен его вернуть
            //string code = $"class Temp {{ object Method() {{ " + variableDeclarations2 + $" return {expressionText}; }} }}";

            // 1. Определяем опции парсинга (в старых версиях Roslyn LanguageVersion задается здесь)
            CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp7_3); // Выберите поддерживаемую версию

            SyntaxTree tree = CSharpSyntaxTree.ParseText(code, parseOptions);
            CSharpCompilation compilation = CreateCompilation().AddSyntaxTrees(tree);

            System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();
            List<Diagnostic> diagnosticErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            if (diagnosticErrors.Any())
            {
                List<string> errorMessages = new List<string>();
                foreach (Diagnostic error in diagnosticErrors)
                {
                    errorMessages.Add(error.GetMessage());
                }
                return (type: null, errors: errorMessages);
            }

            // --- Дополнительный шаг: Определение типа ---
            // 1. Получаем семантическую модель
            SemanticModel model = compilation.GetSemanticModel(tree);

            // 2. Находим узел, который представляет наше выражение внутри дерева.
            // Мы ищем ReturnStatement, а затем его дочернее Expression.
            ReturnStatementSyntax returnStatement = tree.GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .FirstOrDefault();

            ExpressionSyntax expressionSyntax = returnStatement?.Expression;

            if (expressionSyntax != null)
            {
                // 3. Используем GetTypeInfo для определения типа выражения
                Microsoft.CodeAnalysis.TypeInfo typeInfo = model.GetTypeInfo(expressionSyntax);
                ITypeSymbol actualType = typeInfo.Type;

                if (actualType != null)
                {
                    Type type_ = GetSystemTypeFromITypeSymbol(actualType, compilation);
                    return (type: type_, errors: null);
                }
                else
                {
                    // Это может случиться для void-вызовов, например Console.WriteLine()
                }
            }
            return (type: null, errors: null);
        }

        private static Type GetSystemTypeFromITypeSymbol(ITypeSymbol typeSymbol, CSharpCompilation compilation)
        {
            if (typeSymbol == null)
            {
                return null;
            }

            // Handle special types (int, string, bool, etc.)
            if (typeSymbol.SpecialType != SpecialType.None)
            {
                return GetSpecialType(typeSymbol.SpecialType);
            }

            // Handle array types
            if (typeSymbol is IArrayTypeSymbol arrayType)
            {
                Type elementType = GetSystemTypeFromITypeSymbol(arrayType.ElementType, compilation);
                if (elementType == null)
                {
                    return null;
                }

                // Handle multi-dimensional arrays
                return arrayType.Rank == 1 ? elementType.MakeArrayType() : elementType.MakeArrayType(arrayType.Rank);
            }

            // Handle generic types
            if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                // Get the generic type definition
                string fullName = GetFullMetadataName(namedType.OriginalDefinition);
                Type genericTypeDef = FindType(fullName, compilation);

                if (genericTypeDef == null)
                {
                    return null;
                }

                // Get type arguments
                Type[] typeArgs = namedType.TypeArguments
                    .Select(arg => GetSystemTypeFromITypeSymbol(arg, compilation))
                    .ToArray();

                return typeArgs.Any(t => t == null) ? null : genericTypeDef.MakeGenericType(typeArgs);
            }

            // Regular named type
            string typeName = GetFullMetadataName(typeSymbol);
            return FindType(typeName, compilation);
        }

        private static string GetFullMetadataName(ITypeSymbol symbol)
        {
            if (symbol == null)
            {
                return string.Empty;
            }

            INamespaceSymbol containingNamespace = symbol.ContainingNamespace;
            string namespaceName = containingNamespace?.ToDisplayString();

            return string.IsNullOrEmpty(namespaceName) || namespaceName == "<global namespace>"
                ? symbol.MetadataName
                : $"{namespaceName}.{symbol.MetadataName}";
        }

        private static Type FindType(string fullName, CSharpCompilation compilation)
        {
            // Try to find in referenced assemblies from compilation
            foreach (MetadataReference reference in compilation.References)
            {
                if (reference is PortableExecutableReference peRef && !string.IsNullOrEmpty(peRef.FilePath))
                {
                    try
                    {
                        Assembly assembly = Assembly.LoadFrom(peRef.FilePath);
                        Type type = assembly.GetType(fullName);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch
                    {
                        // Skip if assembly can't be loaded
                    }
                }
            }

            // Try Type.GetType as fallback for system types
            return Type.GetType(fullName);
        }

        private static Type GetSpecialType(SpecialType specialType)
        {
            return specialType switch
            {
                SpecialType.System_Object => typeof(object),
                SpecialType.System_Void => typeof(void),
                SpecialType.System_Boolean => typeof(bool),
                SpecialType.System_Char => typeof(char),
                SpecialType.System_SByte => typeof(sbyte),
                SpecialType.System_Byte => typeof(byte),
                SpecialType.System_Int16 => typeof(short),
                SpecialType.System_UInt16 => typeof(ushort),
                SpecialType.System_Int32 => typeof(int),
                SpecialType.System_UInt32 => typeof(uint),
                SpecialType.System_Int64 => typeof(long),
                SpecialType.System_UInt64 => typeof(ulong),
                SpecialType.System_Decimal => typeof(decimal),
                SpecialType.System_Single => typeof(float),
                SpecialType.System_Double => typeof(double),
                SpecialType.System_String => typeof(string),
                SpecialType.System_DateTime => typeof(DateTime),
                SpecialType.None => throw new NotImplementedException(),
                SpecialType.System_Enum => throw new NotImplementedException(),
                SpecialType.System_MulticastDelegate => throw new NotImplementedException(),
                SpecialType.System_Delegate => throw new NotImplementedException(),
                SpecialType.System_ValueType => throw new NotImplementedException(),
                SpecialType.System_IntPtr => throw new NotImplementedException(),
                SpecialType.System_UIntPtr => throw new NotImplementedException(),
                SpecialType.System_Array => throw new NotImplementedException(),
                SpecialType.System_Collections_IEnumerable => throw new NotImplementedException(),
                SpecialType.System_Collections_Generic_IEnumerable_T => throw new NotImplementedException(),
                SpecialType.System_Collections_Generic_IList_T => throw new NotImplementedException(),
                SpecialType.System_Collections_Generic_ICollection_T => throw new NotImplementedException(),
                SpecialType.System_Collections_IEnumerator => throw new NotImplementedException(),
                SpecialType.System_Collections_Generic_IEnumerator_T => throw new NotImplementedException(),
                SpecialType.System_Collections_Generic_IReadOnlyList_T => throw new NotImplementedException(),
                SpecialType.System_Collections_Generic_IReadOnlyCollection_T => throw new NotImplementedException(),
                SpecialType.System_Nullable_T => throw new NotImplementedException(),
                SpecialType.System_Runtime_CompilerServices_IsVolatile => throw new NotImplementedException(),
                SpecialType.System_IDisposable => throw new NotImplementedException(),
                SpecialType.System_TypedReference => throw new NotImplementedException(),
                SpecialType.System_ArgIterator => throw new NotImplementedException(),
                SpecialType.System_RuntimeArgumentHandle => throw new NotImplementedException(),
                SpecialType.System_RuntimeFieldHandle => throw new NotImplementedException(),
                SpecialType.System_RuntimeMethodHandle => throw new NotImplementedException(),
                SpecialType.System_RuntimeTypeHandle => throw new NotImplementedException(),
                SpecialType.System_IAsyncResult => throw new NotImplementedException(),
                SpecialType.System_AsyncCallback => throw new NotImplementedException(),
                SpecialType.System_Runtime_CompilerServices_RuntimeFeature => throw new NotImplementedException(),
                _ => null,
            };
        }
    }
}
