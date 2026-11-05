using System.Runtime.CompilerServices;

// Тесты проверяют внутренние правила раннера (`RobotLocator`, разбор framework-ов): держать их
// ради тестов публичными хуже, чем открыть сборку тестам — как это сделано в студии.
[assembly: InternalsVisibleTo("AiRpaStudio.Tests")]
