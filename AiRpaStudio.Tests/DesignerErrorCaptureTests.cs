using System;
using System.Collections.Generic;
using System.Xml;
using System.Activities.Presentation;
using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Тесты захвата ошибки загрузки XAML в конструкторе WF (карточка «отправка ошибки дизайнера в чат»).
/// Проверяют главное: точная позиция берётся из <see cref="XmlException"/> цепочки исключений, а не из
/// внешнего <c>XamlObjectWriterException.LineNumber/LinePosition</c> (обманка объектного writer'а).
/// </summary>
public class DesignerErrorCaptureTests
{
    private const string SampleXaml =
        "line1" + "\n" +
        "line2" + "\n" +
        "line3" + "\n" +
        "line4" + "\n" +
        "line5";

    [Test]
    public void Capture_ExtractsPositionFromInnerXmlException()
    {
        var xml = new XmlException("Префикс \"aWWom\" необъявленный.", null, 27, 6);
        var outer = new Exception("outer", xml);

        DesignerErrorSnapshot snapshot = DesignerErrorCapture.Capture("file.xaml", outer, SampleXaml);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.LineNumber, Is.EqualTo(27));
            Assert.That(snapshot.LinePosition, Is.EqualTo(6));
            Assert.That(snapshot.HasPosition, Is.True);
            Assert.That(snapshot.InnerMessages, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Capture_UsesXamlExceptionAsFallback()
    {
        var xaml = new System.Xaml.XamlException("bad member", new Exception("inner"), 5, 3);

        DesignerErrorSnapshot snapshot = DesignerErrorCapture.Capture("file.xaml", xaml, SampleXaml);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.LineNumber, Is.EqualTo(5));
            Assert.That(snapshot.LinePosition, Is.EqualTo(3));
            Assert.That(snapshot.HasPosition, Is.True);
        });
    }

    [Test]
    public void Capture_NullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DesignerErrorCapture.Capture("file.xaml", null, null));
    }

    [Test]
    public void CaptureFromLoadErrors_ExtractsPositionAndMessage()
    {
        var errors = new List<XamlLoadErrorInfo>
        {
            new XamlLoadErrorInfo("Префикс \"aWWom\" необъявленный.", 27, 6)
        };

        DesignerErrorSnapshot snapshot = DesignerErrorCapture.CaptureFromLoadErrors("file.xaml", errors);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.LineNumber, Is.EqualTo(27));
            Assert.That(snapshot.LinePosition, Is.EqualTo(6));
            Assert.That(snapshot.HasPosition, Is.True);
            Assert.That(snapshot.Message, Is.EqualTo("Префикс \"aWWom\" необъявленный."));
        });
    }

    [Test]
    public void CaptureFromLoadErrors_NullErrors_ReturnsUnavailableSnapshot()
    {
        DesignerErrorSnapshot snapshot = DesignerErrorCapture.CaptureFromLoadErrors("file.xaml", null);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HasPosition, Is.False);
            Assert.That(snapshot.Message, Is.EqualTo("Ошибка загрузки XAML"));
        });
    }

    [Test]
    public void BuildPromptText_ContainsFilePathPositionAndOffendingLine()
    {
        var xml = new XmlException("bad", null, 3, 2);
        DesignerErrorSnapshot snapshot = DesignerErrorCapture.Capture(@"C:\x.xaml", new Exception("boom", xml), SampleXaml);

        string prompt = snapshot.BuildPromptText();

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(@"C:\x.xaml"));
            Assert.That(prompt, Does.Contain("строка 3"));
            Assert.That(prompt, Does.Contain("line3"));
            Assert.That(prompt, Does.Contain("```xml"));
        });
    }

    [Test]
    public void CapturingXamlLoadErrorService_ReportsAndDelegates()
    {
        DesignerErrorSnapshot captured = null;
        EventHandler<DesignerErrorSnapshot> handler = (_, snapshot) => captured = snapshot;
        DesignerErrorReporter.Captured += handler;

        var inner = new RecordingXamlLoadErrorService();
        var service = new CapturingXamlLoadErrorService("file.xaml", inner);
        var errors = new List<XamlLoadErrorInfo> { new XamlLoadErrorInfo("boom", 7, 2) };

        try
        {
            service.ShowXamlLoadErrors(errors);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured.LineNumber, Is.EqualTo(7));
                Assert.That(captured.LinePosition, Is.EqualTo(2));
                Assert.That(inner.Received, Is.SameAs(errors), "штатное поведение дизайнера обязано сохраняться");
            });
        }
        finally
        {
            DesignerErrorReporter.Captured -= handler;
        }
    }

    private sealed class RecordingXamlLoadErrorService : IXamlLoadErrorService
    {
        public IList<XamlLoadErrorInfo> Received { get; private set; }

        public void ShowXamlLoadErrors(IList<XamlLoadErrorInfo> errors)
        {
            Received = errors;
        }
    }
}
