using System;
using System.IO;
using System.Text;
using AiRpaStudio.Tests.Generation;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Фикстура карточки «ошибка валидации поля выражения видна только после клика по полю»: документ,
/// повторяющий репро пользователя (<c>ActivityLibrary5\Activity3.xaml</c>) — <c>Assign</c> с корректной
/// левой частью и невалидным правым выражением <c>аааа</c>.
/// <para>
/// Почему XAML текстом, а не объектной моделью: <c>sap2010:ExpressionActivityEditor.ExpressionActivityEditor
/// ="C#"</c> — это то, что включает Roslyn-редактор в поле. Без него WF рисует обычный текстовый бокс,
/// и «красного значка в поле» не существует вовсе; сборка документа через <c>WorkflowBuildService</c>
/// такой атрибут не гарантирует.
/// </para>
/// <para>
/// Ошибка ровно одна и в поле «Value»: остальные аргументы корректны, поэтому красный значок на самой
/// активности принадлежит только этому выражению (иначе нельзя понять, чей значок).
/// </para>
/// </summary>
internal static class ExpressionBadgeSamples
{
    /// <summary>Невалидное выражение фикстуры: имени <c>аааа</c> нет ни в переменных, ни в аргументах.</summary>
    internal const string BrokenExpression = "аааа";

    /// <summary>Имя переменной, которая в фикстуре есть (левая часть <c>Assign</c> обязана быть корректной).</summary>
    internal const string KnownVariable = "output";
    /// <summary>
    /// Корректное выражение фикстуры — переменная <c>message</c> в <c>WriteLine.Text</c>: она есть в зоне
    /// видимости, поэтому бейджа у поля быть не должно.
    /// <para>
    /// Почему не правая часть <c>Assign</c>: карточка проверяет бейдж поля, а не правила сравнения типов
    /// правой части <c>Assign</c>. Корректная правая часть (литерал) проверяется отдельным регрессом
    /// <c>ExpressionValidatorRegressionTests</c> по эталонным файлам.
    /// </para>
    /// </summary>
    internal const string ValidExpression = "message";

    private const string Xaml = @"<Activity mc:Ignorable=""sap sap2010"" sap2010:ExpressionActivityEditor.ExpressionActivityEditor=""C#""
 xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
 xmlns:mc=""http://schemas.openxmlformats.org/markup-compatibility/2006""
 xmlns:mca=""clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities""
 xmlns:sap=""http://schemas.microsoft.com/netfx/2009/xaml/activities/presentation""
 xmlns:sap2010=""http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation""
 xmlns:sco=""clr-namespace:System.Collections.ObjectModel;assembly=mscorlib""
 xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <TextExpression.NamespacesForImplementation>
    <sco:Collection x:TypeArguments=""x:String"">
      <x:String>System</x:String>
      <x:String>System.Collections.Generic</x:String>
      <x:String>System.Linq</x:String>
      <x:String>System.Text</x:String>
    </sco:Collection>
  </TextExpression.NamespacesForImplementation>
  <TextExpression.ReferencesForImplementation>
    <sco:Collection x:TypeArguments=""AssemblyReference"">
      <AssemblyReference>mscorlib</AssemblyReference>
      <AssemblyReference>System</AssemblyReference>
      <AssemblyReference>System.Core</AssemblyReference>
    </sco:Collection>
  </TextExpression.ReferencesForImplementation>
  <Sequence DisplayName=""Sequence"">
    <Sequence.Variables>
      <Variable x:TypeArguments=""x:String"" Name=""output"" />
      <Variable x:TypeArguments=""x:String"" Name=""message"" />
    </Sequence.Variables>
    <Assign DisplayName=""Assign"">
      <Assign.To>
        <OutArgument x:TypeArguments=""x:String"">
          <mca:CSharpReference x:TypeArguments=""x:String"">output</mca:CSharpReference>
        </OutArgument>
      </Assign.To>
      <Assign.Value>
        <InArgument x:TypeArguments=""x:String"">
          <mca:CSharpValue x:TypeArguments=""x:String"">аааа</mca:CSharpValue>
        </InArgument>
      </Assign.Value>
    </Assign>
    <WriteLine DisplayName=""Контроль"">
      <WriteLine.Text>
        <InArgument x:TypeArguments=""x:String"">
          <mca:CSharpValue x:TypeArguments=""x:String"">message</mca:CSharpValue>
        </InArgument>
      </WriteLine.Text>
    </WriteLine>
  </Sequence>
</Activity>";

    /// <summary>Пишет фикстуру во временный каталог и возвращает путь (UTF-8 без BOM, как пишет студия).</summary>
    internal static string WriteInvalidExpressionXaml()
    {
        string directory = WorkflowTestSamples.CreateTempDirectory();
        string path = Path.Combine(directory, "Activity3.xaml");
        File.WriteAllText(path, Xaml, new UTF8Encoding(false));
        return path;
    }
}
