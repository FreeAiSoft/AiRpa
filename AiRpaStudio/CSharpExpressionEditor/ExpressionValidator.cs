using System;
using System.Activities.Presentation.Model;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Windows.Threading;
using RpaCommon;

namespace RPA.CSharpExpressionEditor
{
    //UselocationExpression
    //        Что такое Location Expression?
    //В контексте Windows Workflow Foundation существует два типа выражений:
    //1. Value Expression(обычное выражение) - UseLocationExpression = false
    //Вычисляется до значения(r-value)
    //Используется для чтения данных
    //Примеры: x + 5, DateTime.Now, "Hello " + userName
    //Применение: InArgument, вычисляемые значения
    //2. Location Expression(выражение-местоположение) - UseLocationExpression = true
    //Представляет место в памяти(l-value), куда можно записать значение
    //Используется для записи данных
    //Примеры: myVariable, myObject.Property, array[0]
    //Применение: OutArgument, InOutArgument, переменные для записи результата

    internal class ExpressionValidator
    {
        private static readonly List<Type> _primitiveTypes = new List<Type>
        {
            typeof(string),
            typeof(int),
            typeof(decimal),
            typeof(double),
            typeof(float),
            typeof(byte),
            typeof(long),
            typeof(short),
            typeof(char),
            typeof(bool),
        };

        /// <summary>
        /// Если возвращает true - это ошибка
        /// </summary>
        /// <param name="ownerActivity"></param>
        /// <param name="dispatcher"></param>
        /// <param name="expressionText"></param>
        /// <param name="expressionType"></param>
        /// <param name="useLocationExpression"></param>
        /// <param name="errorMessage"></param>
        /// <returns></returns>
        public static bool Validate(ModelItem ownerActivity, Dispatcher dispatcher, string expressionText, Type expressionType, bool useLocationExpression, out string errorMessage)
        {
            errorMessage = null;

            //validate
            //if the text is empty we clear the error message
            if (string.IsNullOrEmpty(expressionText))
            {
                return false;
            }

            try
            {
                //TODO: Add logic to validate expression
                string error = null;
                dispatcher.Invoke(() =>
                {
                    if (!useLocationExpression)
                    {
                        // этой ветке чекаем только правую часть, т.к. !UseLocationExpression
                        if (ownerActivity.ItemType == typeof(Assign))
                        {
                            error = ValidateRightType(ownerActivity, expressionText);
                        }
                        else if (ownerActivity.ItemType == typeof(Delay))
                        {
                            CallResult literalResult = CSharpExpressionHelper.TryCreateLiteral(typeof(TimeSpan), expressionText);
                            if (literalResult.Success)
                            {
                                return;
                            }
                            // если из литерала не получилось, то ищем в переменных
                            Dictionary<string, Type> availableParametersAndVars = CSharpExpressionHelper.GetEnvironment(ownerActivity);
                            bool isExist = availableParametersAndVars.TryGetValue(expressionText.Trim(), out Type envType);
                            if (isExist && typeof(TimeSpan).IsAssignableFrom(envType))
                            {
                                return;
                            }
                            error = literalResult.Error.Message;
                        }
                        else
                        {
                            try
                            {
                                // Пытаемся создать выражение как object, чтобы проверить корректность синтаксиса
                                (Type type, List<string> errors) = RoslynExpressionValidator.Instance.CheckExpression(expressionText, ownerActivity);

                                // Если создание прошло успешно, считаем что это допустимое выражение типа object
                                error = type != null && expressionType.IsAssignableFrom(type) ? null : string.Join(Environment.NewLine, errors);
                            }
                            catch (Exception ex)
                            {
                                error = $"Не удалось определить тип выражения: {ex}";
                            }
                        }
                    }
                    else
                    {
                        if (ownerActivity.ItemType == typeof(Assign))
                        {
                            ModelItem to = ownerActivity.Properties["To"].Value;
                            Type leftType = null;
                            if (to != null)
                            {
                                string variableCandidatName = (string)to.Properties["Expression"].Value.Properties["ExpressionText"].Value.GetCurrentValue();
                                leftType = CSharpExpressionHelper.GetVarOrArgType(ownerActivity, variableCandidatName);
                                if (leftType == null)
                                {
                                    error = $"Переменной с именем {variableCandidatName} в зоне видимости не найдено";
                                }
                            }
                        }
                    }
                });
                if (error != null)
                {
                    errorMessage = error;
                    return true;
                }
            }
            catch (Exception err)
            {
                errorMessage = err.Message;
                return true;
            }

            return false;
        }

        private static string ValidateRightType(ModelItem ownerActivity, string expressionText)
        {
            string error = null;
            // в Assign левый и правый типы всегда String
            // левая часть валидна только если она есть в переменных или в аргументах
            ModelItem to = ownerActivity.Properties["To"].Value;
            Type leftType = null;
            if (to != null)
            {
                string variableName = to.Properties["Expression"].Value.Properties["ExpressionText"].Value.GetCurrentValue().ToString().Trim();
                leftType = CSharpExpressionHelper.GetVarOrArgType(ownerActivity, variableName);
            }

            // Определяем тип правой части выражения
            Type rightType = null;
            string rightTypeError = null;

            // 1. Сначала пробуем определить тип как литерал для примитивных типов
            foreach (Type primitiveType in _primitiveTypes)
            {
                CallResult literalResult = CSharpExpressionHelper.TryCreateLiteral(primitiveType, expressionText);
                if (literalResult.Success)
                {
                    rightType = primitiveType;
                    break;
                }
            }

            // 2. Если не литерал, проверяем, является ли это переменной или аргументом
            if (rightType == null)
            {
                // Проверяем переменные
                rightType = CSharpExpressionHelper.GetVarOrArgType(ownerActivity, expressionText);
            }

            // 3. Если не смогли определить тип, пробуем создать C# выражение 
            if (rightType == null)
            {
                try
                {
                    // Пытаемся создать выражение как object, чтобы проверить корректность синтаксиса
                    (Type type, List<string> errors) = RoslynExpressionValidator.Instance.CheckExpression(expressionText, ownerActivity);

                    // Если создание прошло успешно, считаем что это допустимое выражение типа object
                    if (type != null)
                    {
                        rightType = type;
                    }
                    else
                    {
                        rightTypeError = string.Join(Environment.NewLine, errors);
                    }
                }
                catch (Exception ex)
                {
                    rightTypeError = $"Не удалось определить тип выражения: {ex}";
                }
            }

            // 4. Валидация совместимости типов
            if (rightType != null && leftType != null)
            {
                // Проверяем, совместимы ли типы
                if (!leftType.IsAssignableFrom(rightType) && rightType != typeof(object))
                {
                    error = $"Несовместимые типы: нельзя присвоить значение типа '{rightType.Name}' переменной типа '{leftType.Name}'";
                }
            }
            else if (rightTypeError != null)
            {
                error = rightTypeError;
            }

            return error;
        }
    }
}
