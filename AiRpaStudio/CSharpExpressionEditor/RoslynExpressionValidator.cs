using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PuppeteerSharp;

namespace RPA.CSharpExpressionEditor
{
    public class RoslynExpressionValidator
    {
        public static RoslynExpressionValidator Instance { get; } = new RoslynExpressionValidator();

        internal string UsingNamespaces { get; set; } = string.Empty;

        private readonly CSharpCompilation _compilation;

        private RoslynExpressionValidator()
        {
            List<MetadataReference> references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DataTable).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IBrowser).GetTypeInfo().Assembly.Location)
            };

            _compilation = CSharpCompilation.Create("DynamicCompilation")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(references);
        }

        public (Type type, List<string> errors) CheckExpression(string expressionText, ModelItem ownerActivity)
        {
            // Генерируем код сигнатуры метода из словаря (например: "RPA.Order orders, System.Int32 index")
            Dictionary<string, Type> availableParameters = new Dictionary<string, Type>();
            KeyedCollection<string, DynamicActivityProperty> declaredArguments = CSharpExpressionHelper.GetArguments(ownerActivity);
            foreach (DynamicActivityProperty argItem in declaredArguments)
            {
                Type type = argItem.Type;
                Type rightType = typeof(Argument).IsAssignableFrom(type) ? type.GenericTypeArguments[0] : type;
                availableParameters.Add(argItem.Name, rightType);
            }

            List<string> parametersList = availableParameters
                .Select(kvp => $"{kvp.Value.Name} {kvp.Key}")
                .ToList();

            List<ModelItem> declaredVariables = CSharpExpressionHelper.GetVariablesInScope(ownerActivity);
            Dictionary<string, Type> availableVariables = new Dictionary<string, Type>();
            foreach (ModelItem modelItemVar in declaredVariables)
            {
                Type inferredType = ((LocationReference)modelItemVar.GetCurrentValue()).Type;
                string varName = ((LocationReference)modelItemVar.GetCurrentValue()).Name;
                availableVariables.Add(varName, inferredType);
            }

            List<string> variablesList = availableVariables
                .Select(kvp => $"{kvp.Value.Name} {kvp.Key}")
                .ToList();

            // Combine parameters and variables, only adding comma if both lists have items
            List<string> allParams = new List<string>();
            if (parametersList.Count > 0)
            {
                allParams.AddRange(parametersList);
            }
            if (variablesList.Count > 0)
            {
                allParams.AddRange(variablesList);
            }
            string parametersCode = string.Join(", ", allParams);

            // Обертка: используем параметры метода
            string code = UsingNamespaces +
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
            CSharpCompilation compilation = _compilation.AddSyntaxTrees(tree);

            System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();
            List<Diagnostic> diagnosticErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            if (diagnosticErrors.Any())
            {
                List<string> errorMessages = new List<string>();
                foreach (Diagnostic error in diagnosticErrors)
                {
                    errorMessages.Add(error.GetMessage());
                }
                _ = _compilation.RemoveAllSyntaxTrees();
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
                    _ = _compilation.RemoveAllSyntaxTrees();
                    return (type: type_, errors: null);
                }
                else
                {
                    // Это может случиться для void-вызовов, например Console.WriteLine()
                    _ = _compilation.RemoveAllSyntaxTrees();
                }
            }
            _ = _compilation.RemoveAllSyntaxTrees();
            return (type: null, errors: null);
        }

        private Type GetSystemTypeFromITypeSymbol(ITypeSymbol typeSymbol, CSharpCompilation compilation)
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

        private string GetFullMetadataName(ITypeSymbol symbol)
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

        private Type FindType(string fullName, CSharpCompilation compilation)
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

        private Type GetSpecialType(SpecialType specialType)
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
