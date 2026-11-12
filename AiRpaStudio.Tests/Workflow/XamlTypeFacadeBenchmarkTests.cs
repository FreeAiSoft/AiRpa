using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Генератор реалистичного XAML вложенной активности: 20 переменных, 300 блоков, 10 аргументов.
    /// Нужен стенду, чтобы мерить не «пустую строку», а настоящий по размеру документ.
    /// </summary>
    internal static class RealisticWorkflowXaml
    {
        internal const string ActivityNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        internal const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        internal static string Build(string xClass, int variableCount, int blockCount, int argumentCount)
        {
            var sb = new StringBuilder(4096);

            sb.Append("<Activity x:Class=\"").Append(xClass).Append('"');
            sb.Append(" xmlns=\"").Append(ActivityNamespace).Append('"');
            sb.Append(" xmlns:x=\"").Append(XamlNamespace).Append("\">");

            sb.Append("<x:Members>");
            for (int i = 0; i < argumentCount; i++)
            {
                sb.Append("<x:Property Name=\"Arg").Append(i).Append("\" Type=\"InArgument(x:String)\" />");
            }
            sb.Append("</x:Members>");

            sb.Append("<Sequence>");
            sb.Append("<Sequence.Variables>");
            for (int i = 0; i < variableCount; i++)
            {
                sb.Append("<Variable x:TypeArguments=\"x:String\" Name=\"v").Append(i).Append("\" />");
            }
            sb.Append("</Sequence.Variables>");
            for (int i = 0; i < blockCount; i++)
            {
                sb.Append("<Sequence DisplayName=\"Block ").Append(i).Append("\" />");
            }
            sb.Append("</Sequence>");
            sb.Append("</Activity>");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Стенд производительности лёгкого сервиса типов вложенных XAML-активностей.
    /// <para>
    /// Целевой показатель: 100 активностей, каждая с 20 переменными, 300 блоками и 10 аргументами,
    /// сервис обязан предоставить быстрее 1 секунды. Для сравнения замеряется полная WF-загрузка тела —
    /// она строится только как справочный контраст и на скорость сервиса не влияет.
    /// </para>
    /// </summary>
    [TestFixture]
    public class XamlTypeFacadeBenchmarkTests
    {
        private const int ArgumentCount = 10;
        private const int VariableCount = 20;
        private const int BlockCount = 300;
        private const int ActivityCount = 100;
        private const long TargetMilliseconds = 1000;
        private const int BatchCount = 3;

        [Test]
        public void FacadeBuilder_Serves100RealisticActivities_UnderOneSecond()
        {
            string sample = RealisticWorkflowXaml.Build("Perf.Robot0", VariableCount, BlockCount, ArgumentCount);

            Assert.That(CountOf(sample, "<Variable "), Is.EqualTo(VariableCount),
                "стенд обязан генерировать реалистичное число переменных");
            Assert.That(CountOf(sample, "<Sequence DisplayName=\"Block"), Is.EqualTo(BlockCount),
                "стенд обязан генерировать реалистичное число блоков");

            XamlTypeFacade first = XamlTypeFacadeBuilder.Build(sample);
            Assert.That(first.FullName, Is.EqualTo("Perf.Robot0"));
            Assert.That(first.Namespace, Is.EqualTo("Perf"));
            Assert.That(first.Name, Is.EqualTo("Robot0"));
            Assert.That(first.Arguments, Has.Count.EqualTo(ArgumentCount));
            Assert.That(first.Arguments[0].Name, Is.EqualTo("Arg0"));
            Assert.That(first.Arguments[0].Type, Is.EqualTo("InArgument(x:String)"));

            string[] xamls = Enumerable.Range(0, ActivityCount)
                .Select(i => RealisticWorkflowXaml.Build("Perf.Robot" + i, VariableCount, BlockCount, ArgumentCount))
                .ToArray();

            // Прогрев JIT, чтобы цифры не содержали одноразовую стоимость первого прогона.
            for (int i = 0; i < 3; i++)
            {
                _ = XamlTypeFacadeBuilder.Build(xamls[i]);
            }

            var timings = new List<double>(BatchCount);
            for (int batch = 0; batch < BatchCount; batch++)
            {
                Stopwatch clock = Stopwatch.StartNew();
                for (int i = 0; i < ActivityCount; i++)
                {
                    _ = XamlTypeFacadeBuilder.Build(xamls[i]);
                }
                clock.Stop();
                timings.Add(clock.Elapsed.TotalMilliseconds);
            }

            timings.Sort();
            double median = timings[timings.Count / 2];

            TestContext.WriteLine(
                $"FacadeBuilder: {ActivityCount} активностей × ({VariableCount} переменных, {BlockCount} блоков, {ArgumentCount} аргументов) — " +
                "батчи (ms): " + string.Join(", ", timings.Select(t => t.ToString("F1", CultureInfo.InvariantCulture))) +
                $"; медиана {median.ToString("F1", CultureInfo.InvariantCulture)} ms");

            Assert.That(median, Is.LessThan(TargetMilliseconds),
                "лёгкий сервис типов обязан предоставлять 100 активностей быстрее 1 секунды");
        }

        [Test]
        public void FullWfLoad_IsMeasuredAsReferenceContrast()
        {
            var settings = new System.Activities.XamlIntegration.ActivityXamlServicesSettings
            {
                CompileExpressions = false
            };

            string sample = RealisticWorkflowXaml.Build("Perf.Robot0", VariableCount, BlockCount, ArgumentCount);

            // Прогрев.
            using (var warmup = new MemoryStream(Encoding.UTF8.GetBytes(sample)))
            {
                _ = System.Activities.XamlIntegration.ActivityXamlServices.Load(warmup, settings);
            }

            const int iterations = 3;
            double best = double.MaxValue;
            for (int i = 0; i < iterations; i++)
            {
                Stopwatch clock = Stopwatch.StartNew();
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sample)))
                {
                    System.Activities.Activity activity = System.Activities.XamlIntegration.ActivityXamlServices.Load(stream, settings);
                    Assert.That(activity, Is.Not.Null, "полная WF-загрузка реалистичного XAML должна выполняться");
                }
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }

            TestContext.WriteLine(
                $"FullWfLoad (контраст): 1 активность ({VariableCount} переменных, {BlockCount} блоков) лучший прогон " +
                $"{best.ToString("F1", CultureInfo.InvariantCulture)} ms → 100 активностей ≈ " +
                $"{(best * ActivityCount).ToString("F0", CultureInfo.InvariantCulture)} ms");

            // Здесь нет утверждения о скорости: это справочный контраст, почему тело разбирать нельзя.
        }

        private static int CountOf(string text, string token)
        {
            int count = 0;
            int start = 0;
            while ((start = text.IndexOf(token, start, StringComparison.Ordinal)) >= 0)
            {
                count++;
                start += token.Length;
            }
            return count;
        }
    }
}
