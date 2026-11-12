using System;
using System.IO;
using System.Xml;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Быстрый построитель фасада вложенной активности. Принципиально <b>не</b> грузит тело workflow:
    /// читает только корневой элемент (<c>x:Class</c>) и секцию <c>x:Members</c>, а дальше возвращается,
    /// не касаясь <c>&lt;Sequence&gt;</c> с сотнями блоков и переменными. Именно это даёт скорость
    /// «100 активностей за 1 секунду» — цена не зависит от размера тела.
    /// </summary>
    internal static class XamlTypeFacadeBuilder
    {
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        internal static XamlTypeFacade Build(string xaml)
        {
            if (string.IsNullOrWhiteSpace(xaml))
            {
                throw new ArgumentException("Пустой XAML — строить фасад не из чего.", nameof(xaml));
            }

            var facade = new XamlTypeFacade();

            var settings = new XmlReaderSettings
            {
                XmlResolver = null,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
                IgnoreComments = true
            };

            using (XmlReader reader = XmlReader.Create(new StringReader(xaml), settings))
            {
                if (reader.MoveToContent() != XmlNodeType.Element)
                {
                    return facade;
                }

                string fullName = reader.GetAttribute("Class", XamlNamespace);
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    facade.FullName = fullName;
                    int dot = fullName.LastIndexOf('.');
                    facade.Namespace = dot > 0 ? fullName.Substring(0, dot) : string.Empty;
                    facade.Name = dot > 0 ? fullName.Substring(dot + 1) : fullName;
                }

                if (!reader.ReadToFollowing("Members", XamlNamespace))
                {
                    return facade;
                }

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement
                        && reader.LocalName == "Members"
                        && reader.NamespaceURI == XamlNamespace)
                    {
                        break;
                    }

                    if (reader.NodeType == XmlNodeType.Element
                        && reader.LocalName == "Property"
                        && reader.NamespaceURI == XamlNamespace)
                    {
                        facade.Arguments.Add(new XamlTypeFacadeArgument
                        {
                            // У <x:Property> атрибуты Name/Type без XML-префикса, т.е. в пустом
                            // namespace — в отличие от самого элемента x:Property (xaml-namespace).
                            Name = reader.GetAttribute("Name"),
                            Type = reader.GetAttribute("Type")
                        });

                        if (!reader.IsEmptyElement)
                        {
                            reader.Skip();
                        }
                    }
                }
            }

            return facade;
        }
    }
}
