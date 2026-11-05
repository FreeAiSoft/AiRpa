using System;
using System.Collections.Generic;
using RPA.CSharpExpressionEditor;

namespace RPA.Core.Services
{
    /// <summary>
    /// <see cref="IExpressionCompiler"/> поверх <see cref="RoslynExpressionValidator"/>.
    /// <para>
    /// Единственная точка, где фоновая валидация касается Roslyn. Тонкая намеренно: правила
    /// «выражение корректно» живут в <see cref="ExpressionErrorEvaluator"/> и проверяются тестами
    /// без компилятора, а сюда попадает только сам вызов компиляции.
    /// </para>
    /// </summary>
    internal sealed class RoslynExpressionCompiler : IExpressionCompiler
    {
        internal static RoslynExpressionCompiler Instance { get; } = new RoslynExpressionCompiler();

        public (Type Type, List<string> Errors) Check(string expressionText, ExpressionEnvironment environment)
        {
            (Type type, List<string> errors) = RoslynExpressionValidator.CheckExpression(
                expressionText,
                environment ?? ExpressionEnvironment.Empty);

            return (type, errors);
        }
    }
}
