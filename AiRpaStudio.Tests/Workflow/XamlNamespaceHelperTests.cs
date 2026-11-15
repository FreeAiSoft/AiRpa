using System;
using System.Activities.Statements;
using NUnit.Framework;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Тесты <see cref="XamlNamespaceHelper"/>: добавление <c>xmlns</c> для CLR-типа активности
    /// в корень XAML, без дублей и с уникализацией префикса.
    /// </summary>
    [TestFixture]
    public class XamlNamespaceHelperTests
    {
        private const string ActivityNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Test]
        public void BuildClrNamespace_UsesNamespaceAndAssemblyName()
        {
            string clrNamespace = XamlNamespaceHelper.BuildClrNamespace(typeof(PaletteRefreshProbeActivity));

            Assert.That(clrNamespace, Is.EqualTo(
                "clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests"));
        }

        [Test]
        public void BuildPreferredPrefix_UsesFirstLettersOfNamespaceSegments()
        {
            string prefix = XamlNamespaceHelper.BuildPreferredPrefix(typeof(PaletteRefreshProbeActivity));

            Assert.That(prefix, Is.EqualTo("atw"),
                "префикс строится из первых букв сегментов namespace (AiRpaStudio.Tests.Workflow)");
        }

        [Test]
        public void EnsureClrNamespace_AddsMissingNamespace()
        {
            string xaml = "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\"><Sequence /></Activity>";

            string result = XamlNamespaceHelper.EnsureClrNamespace(
                xaml,
                "clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests",
                "atw");

            Assert.That(result, Does.Contain(
                "xmlns:atw=\"clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests\""));
        }

        [Test]
        public void EnsureClrNamespace_DoesNotDuplicateWhenUriAlreadyDeclared()
        {
            string xaml =
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\" " +
                "xmlns:abc=\"clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests\">" +
                "<Sequence /></Activity>";

            string result = XamlNamespaceHelper.EnsureClrNamespace(
                xaml,
                "clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests",
                "atw");

            Assert.That(result, Does.Contain(
                "xmlns:abc=\"clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests\""));
            Assert.That(result, Does.Not.Contain("xmlns:atw="),
                "URI уже объявлен под другим префиксом — второй префикс не нужен");
        }

        [Test]
        public void EnsureClrNamespace_GeneratesUniquePrefixOnConflict()
        {
            string xaml =
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\" " +
                "xmlns:atw=\"clr-namespace:Other;assembly=Other\">" +
                "<Sequence /></Activity>";

            string result = XamlNamespaceHelper.EnsureClrNamespace(
                xaml,
                "clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests",
                "atw");

            Assert.That(result, Does.Contain(
                "clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests"));
            Assert.That(result, Does.Not.Contain(
                "xmlns:atw=\"clr-namespace:AiRpaStudio.Tests.Workflow"),
                "префикс atw занят — объявление обязано получить уникальный префикс");
        }

        [Test]
        public void EnsureClrNamespaces_FiltersSystemActivities()
        {
            string xaml = "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\"><Sequence /></Activity>";

            string result = XamlNamespaceHelper.EnsureClrNamespaces(
                xaml,
                new Type[] { typeof(Sequence), typeof(WriteLine) });

            Assert.That(result, Does.Not.Contain("clr-namespace:System.Activities.Statements"),
                "системные активности WF покрыты стандартным пространством имён — xmlns им не нужен");
            Assert.That(result, Does.Not.Contain("clr-namespace:System.Activities;assembly"));
        }

        [Test]
        public void EnsureClrNamespaces_AddsOnlyCustomActivities()
        {
            string xaml = "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\"><Sequence /></Activity>";

            string result = XamlNamespaceHelper.EnsureClrNamespaces(
                xaml,
                new Type[] { typeof(Sequence), typeof(PaletteRefreshProbeActivity) });

            Assert.That(result, Does.Contain(
                "clr-namespace:AiRpaStudio.Tests.Workflow;assembly=AiRpaStudio.Tests"));
            Assert.That(result, Does.Not.Contain("clr-namespace:System.Activities"));
        }
    }
}