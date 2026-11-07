using System;
using System.Collections.Generic;
using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Правило «какая вкладка активируется при закрытии» (баг «при закрытии открывается не та вкладка»).
/// <para>
/// Эталон поведения — Visual Studio: активной становится правая соседняя вкладка (после удаления
/// закрытой она встаёт на её место), для последней вкладки — левая соседняя. Закрытие единственной
/// вкладки оставляет панель без активной вкладки, закрытие фоновой вкладки активную не меняет.
/// </para>
/// <para>
/// Раньше студия вообще не применяла правило: <c>CustomWfDesigner.RemoveDesignerByLoadedFile</c>
/// выбирал <c>WorkflowDesigners[0]</c> — всегда первую вкладку, из-за чего пользователь попадал
/// в чужой документ.
/// </para>
/// </summary>
public class DocumentTabPolicyTests
{
    [TestCase(1, 3, 2, Description = "средняя вкладка: активной становится правая соседняя")]
    [TestCase(0, 3, 1, Description = "первая вкладка: активной становится вторая, а не первая заново")]
    [TestCase(2, 3, 1, Description = "последняя вкладка: активной становится левая соседняя")]
    [TestCase(0, 2, 1, Description = "первая из двух: правая соседняя")]
    [TestCase(1, 2, 0, Description = "последняя из двух: левая соседняя")]
    [TestCase(1, 4, 2)]
    [TestCase(3, 4, 2)]
    public void NextActiveIndex_ReturnsNeighbourLikeVisualStudio(int closedIndex, int countBeforeClose, int expected)
    {
        Assert.That(DocumentTabPolicy.NextActiveIndex(closedIndex, countBeforeClose), Is.EqualTo(expected));
    }

    [Test]
    public void NextActiveIndex_SingleTab_NothingToActivate()
    {
        Assert.That(DocumentTabPolicy.NextActiveIndex(0, 1), Is.Null,
            "закрыли единственную вкладку — активировать нечего");
    }

    [Test]
    public void NextActiveIndex_EmptyPane_NothingToActivate()
    {
        Assert.That(DocumentTabPolicy.NextActiveIndex(0, 0), Is.Null);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(int.MaxValue)]
    public void NextActiveIndex_IndexOutOfRange_DoesNotThrowAndReturnsNull(int closedIndex)
    {
        int? next = null;
        Assert.DoesNotThrow(() => next = DocumentTabPolicy.NextActiveIndex(closedIndex, 3));
        Assert.That(next, Is.Null, "индекс вне списка — закрывали не вкладку этой панели");
    }

    [Test]
    public void NextActive_DesignerTabs_ReturnsRightNeighbour()
    {
        object a = new object();
        object b = new object();
        object c = new object();

        Assert.Multiple(() =>
        {
            Assert.That(DocumentTabPolicy.NextActive(new[] { a, b, c }, b), Is.SameAs(c));
            Assert.That(DocumentTabPolicy.NextActive(new[] { a, b, c }, c), Is.SameAs(b));
            Assert.That(DocumentTabPolicy.NextActive(new[] { a, b, c }, a), Is.SameAs(b));
            Assert.That(DocumentTabPolicy.NextActive(new[] { a }, a), Is.Null);
        });
    }

    [Test]
    public void NextActive_ClosingIsNotInTheList_ReturnsNull()
    {
        object a = new object();
        Assert.That(DocumentTabPolicy.NextActive(new[] { a }, new object()), Is.Null,
            "вкладки нет в списке — соседа определить нельзя");
    }

    [Test]
    public void NextActive_ComparesTabsByReference()
    {
        // Два дизайнера на один файл: закрывать надо конкретный экземпляр, а не «такой же путь».
        object first = new object();
        object second = new object();

        Assert.That(DocumentTabPolicy.NextActive(new[] { first, second }, second), Is.SameAs(first),
            "сосед ищется по ссылке, иначе закрывалась бы не та вкладка");
    }

    [Test]
    public void NextActive_EmptyOrNullList_DoesNotThrow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DocumentTabPolicy.NextActive<object>(null, new object()), Is.Null);
            Assert.That(DocumentTabPolicy.NextActive(Array.Empty<object>(), new object()), Is.Null);
            Assert.That(DocumentTabPolicy.NextActive(new[] { new object() }, (object)null), Is.Null);
        });
    }
}