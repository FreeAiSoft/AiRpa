using System;
using NUnit.Framework;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Юнит-тесты быстрого разбора фасада вложенной XAML-активности (<see cref="XamlTypeFacadeBuilder"/>).
    /// Ключевой контракт: builder читает только <c>x:Class</c> и <c>x:Members</c> и <b>не касается
    /// тела workflow</b> — поэтому скорость не зависит от размера тела, а битое тело не мешает фасаду.
    /// </summary>
    [TestFixture]
    public class XamlTypeFacadeBuilderTests
    {
        private const string ActivityNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Test]
        public void Build_ReadsClassAndArguments()
        {
            string xaml = Activity(
                "RPA_1.SubProcess1",
                "<x:Property Name=\"Input\" Type=\"InArgument(x:String)\" />" +
                "<x:Property Name=\"Count\" Type=\"InArgument(x:Int32)\" />",
                "<Sequence />");

            XamlTypeFacade facade = XamlTypeFacadeBuilder.Build(xaml);

            Assert.That(facade.FullName, Is.EqualTo("RPA_1.SubProcess1"));
            Assert.That(facade.Namespace, Is.EqualTo("RPA_1"));
            Assert.That(facade.Name, Is.EqualTo("SubProcess1"));
            Assert.That(facade.Arguments, Has.Count.EqualTo(2));
            Assert.That(facade.Arguments[0].Name, Is.EqualTo("Input"));
            Assert.That(facade.Arguments[0].Type, Is.EqualTo("InArgument(x:String)"));
            Assert.That(facade.Arguments[1].Name, Is.EqualTo("Count"));
            Assert.That(facade.Arguments[1].Type, Is.EqualTo("InArgument(x:Int32)"));
        }

        [Test]
        public void Build_WithoutMembers_ReturnsNoArguments()
        {
            string xaml = Activity("RPA_1.SubProcess3", null, "<Sequence />");

            XamlTypeFacade facade = XamlTypeFacadeBuilder.Build(xaml);

            Assert.That(facade.FullName, Is.EqualTo("RPA_1.SubProcess3"));
            Assert.That(facade.Arguments, Is.Empty);
        }

        [Test]
        public void Build_WithoutClass_ReturnsEmptyIdentity()
        {
            string xaml = "<Activity xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\"><Sequence /></Activity>";

            XamlTypeFacade facade = XamlTypeFacadeBuilder.Build(xaml);

            Assert.That(facade.FullName, Is.Null);
            Assert.That(facade.Namespace, Is.Null);
            Assert.That(facade.Name, Is.Null);
        }

        [Test]
        public void Build_DoesNotTouchBody_WhenBodyIsMalformed()
        {
            // Тело намеренно битое (незакрытый <Sequence>): если builder не заглядывает за x:Members,
            // фасад строится без исключения. Это и есть контракт «цена не зависит от тела».
            string xaml =
                "<Activity x:Class=\"RPA_1.Sub\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\">" +
                "<x:Members><x:Property Name=\"A\" Type=\"InArgument(x:String)\" /></x:Members>" +
                "<Sequence><Assign>";

            XamlTypeFacade facade = XamlTypeFacadeBuilder.Build(xaml);

            Assert.That(facade.FullName, Is.EqualTo("RPA_1.Sub"));
            Assert.That(facade.Arguments, Has.Count.EqualTo(1));
        }

        [Test]
        public void Build_PropertyWithChildren_IsSkipped()
        {
            string xaml = Activity(
                "RPA_1.Sub",
                "<x:Property Name=\"A\" Type=\"InArgument(x:String)\"><x:Property.Attributes /></x:Property>",
                "<Sequence />");

            XamlTypeFacade facade = XamlTypeFacadeBuilder.Build(xaml);

            Assert.That(facade.Arguments, Has.Count.EqualTo(1));
            Assert.That(facade.Arguments[0].Name, Is.EqualTo("A"));
        }

        [Test]
        public void Build_EmptyXaml_Throws()
        {
            Assert.That(() => XamlTypeFacadeBuilder.Build(null), Throws.ArgumentException);
            Assert.That(() => XamlTypeFacadeBuilder.Build("   "), Throws.ArgumentException);
        }

        private static string Activity(string xClass, string members, string body)
        {
            string membersBlock = members == null ? string.Empty : "<x:Members>" + members + "</x:Members>";

            return "<Activity" +
                   (xClass == null ? string.Empty : " x:Class=\"" + xClass + "\"") +
                   " xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\">" +
                   membersBlock +
                   body +
                   "</Activity>";
        }
    }
}
