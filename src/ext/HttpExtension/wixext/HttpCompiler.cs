//-------------------------------------------------------------------------------------------------
// <copyright file="HttpCompiler.cs" company="Outercurve Foundation">
//   Copyright (c) 2004, Outercurve Foundation.
//   This software is released under Microsoft Reciprocal License (MS-RL).
//   The license and further copyright text can be found in the file
//   LICENSE.TXT at the root directory of the distribution.
// </copyright>
//-------------------------------------------------------------------------------------------------

namespace WixToolset.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Xml.Linq;
    using WixToolset.Data;
    using WixToolset.Extensibility;

    /// <summary>
    /// The compiler for the WiX Toolset Http Extension.
    /// </summary>
    public sealed class HttpCompiler : CompilerExtension
    {
        /// <summary>
        /// Instantiate a new HttpCompiler.
        /// </summary>
        public HttpCompiler()
        {
            this.Namespace = "http://wixtoolset.org/schemas/v4/wxs/http";
        }

        /// <summary>
        /// Processes an element for the Compiler.
        /// </summary>
        /// <param name="sourceLineNumbers">Source line number for the parent element.</param>
        /// <param name="parentElement">Parent element of element to process.</param>
        /// <param name="element">Element to process.</param>
        /// <param name="contextValues">Extra information about the context in which this element is being parsed.</param>
        public override void ParseElement(XElement parentElement, XElement element, IDictionary<string, string> context)
        {
            switch (parentElement.Name.LocalName)
            {
                case "ServiceInstall":
                    string serviceInstallName = context["ServiceInstallName"];
                    string serviceUser = String.IsNullOrEmpty(serviceInstallName) ? null : String.Concat("NT SERVICE\\", serviceInstallName);
                    string serviceComponentId = context["ServiceInstallComponentId"];

                    switch (element.Name.LocalName)
                    {
                        case "UrlReservation":
                            this.ParseUrlReservationElement(element, serviceComponentId, serviceUser);
                            break;
                        default:
                            this.Core.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                case "Component":
                    string componentId = context["ComponentId"];

                    switch (element.Name.LocalName)
                    {
                        case "UrlReservation":
                            this.ParseUrlReservationElement(element, componentId, null);
                            break;
                        default:
                            this.Core.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                default:
                    this.Core.UnexpectedElement(parentElement, element);
                    break;
            }
        }

        /// <summary>
        /// Parses a UrlReservation element.
        /// </summary>
        /// <param name="node">The element to parse.</param>
        /// <param name="componentId">Identifier of the component that owns this URL reservation.</param>
        /// <param name="securityPrincipal">The security principal of the parent element (null if nested under Component).</param>
        private void ParseUrlReservationElement(XElement node, string componentId, string securityPrincipal)
        {
            SourceLineNumber sourceLineNumbers = Preprocessor.GetSourceLineNumbers(node);
            Identifier id = null;
            int handleExisting = HttpConstants.heReplace;
            string handleExistingValue = null;
            string sddl = null;
            string url = null;
            bool foundACE = false;

            foreach (XAttribute attrib in node.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.Core.GetAttributeIdentifier(sourceLineNumbers, attrib);
                            break;
                        case "HandleExisting":
                            handleExistingValue = this.Core.GetAttributeValue(sourceLineNumbers, attrib);
                            switch (handleExistingValue)
                            {
                                case "replace":
                                    handleExisting = HttpConstants.heReplace;
                                    break;
                                case "ignore":
                                    handleExisting = HttpConstants.heIgnore;
                                    break;
                                case "fail":
                                    handleExisting = HttpConstants.heFail;
                                    break;
                                default:
                                    this.Core.OnMessage(WixErrors.IllegalAttributeValue(sourceLineNumbers, node.Name.LocalName, "HandleExisting", handleExistingValue, "replace", "ignore", "fail"));
                                    break;
                            }
                            break;
                        case "Sddl":
                            sddl = this.Core.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Url":
                            url = this.Core.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.Core.UnexpectedAttribute(node, attrib);
                            break;
                    }
                }
                else
                {
                    this.Core.ParseExtensionAttribute(node, attrib);
                }
            }

            // Need the element ID for child element processing, so generate now if not authored.
            if (null == id)
            {
                id = this.Core.CreateIdentifier("url", componentId, securityPrincipal, url);
            }

            // Parse UrlAce children.
            foreach (XElement child in node.Elements())
            {
                if (this.Namespace == child.Name.Namespace)
                {
                    switch (child.Name.LocalName)
                    {
                        case "UrlAce":
                            if (null != sddl)
                            {
                                this.Core.OnMessage(WixErrors.IllegalParentAttributeWhenNested(sourceLineNumbers, "UrlReservation", "Sddl", "UrlAce"));
                            }
                            else
                            {
                                foundACE = true;
                                this.ParseUrlAceElement(child, id.Id, securityPrincipal);
                            }
                            break;
                        default:
                            this.Core.UnexpectedElement(node, child);
                            break;
                    }
                }
                else
                {
                    this.Core.ParseExtensionElement(node, child);
                }
            }

            // Url is required.
            if (null == url)
            {
                this.Core.OnMessage(WixErrors.ExpectedAttribute(sourceLineNumbers, node.Name.LocalName, "Url"));
            }

            // Security is required.
            if (null == sddl && !foundACE)
            {
                this.Core.OnMessage(HttpErrors.NoSecuritySpecified(sourceLineNumbers));
            }

            if (!this.Core.EncounteredError)
            {
                Row row = this.Core.CreateRow(sourceLineNumbers, "WixHttpUrlReservation");
                row[0] = id.Id;
                row[1] = handleExisting;
                row[2] = sddl;
                row[3] = url;
                row[4] = componentId;

                if (this.Core.CurrentPlatform == Platform.ARM)
                {
                    // Ensure ARM version of the CA is referenced.
                    this.Core.CreateSimpleReference(sourceLineNumbers, "CustomAction", "WixSchedHttpUrlReservationsInstall_ARM");
                    this.Core.CreateSimpleReference(sourceLineNumbers, "CustomAction", "WixSchedHttpUrlReservationsUninstall_ARM");
                }
                else
                {
                    // All other supported platforms use x86.
                    this.Core.CreateSimpleReference(sourceLineNumbers, "CustomAction", "WixSchedHttpUrlReservationsInstall");
                    this.Core.CreateSimpleReference(sourceLineNumbers, "CustomAction", "WixSchedHttpUrlReservationsUninstall");
                }
            }
        }

        /// <summary>
        /// Parses a UrlAce element.
        /// </summary>
        /// <param name="node">The element to parse.</param>
        /// <param name="urlReservationId">The URL reservation ID.</param>
        /// <param name="defaultSecurityPrincipal">The default security principal.</param>
        private void ParseUrlAceElement(XElement node, string urlReservationId, string defaultSecurityPrincipal)
        {
            SourceLineNumber sourceLineNumbers = Preprocessor.GetSourceLineNumbers(node);
            Identifier id = null;
            string securityPrincipal = defaultSecurityPrincipal;
            int rights = HttpConstants.GENERIC_ALL;
            string rightsValue = null;
            
            foreach (XAttribute attrib in node.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.Core.GetAttributeIdentifier(sourceLineNumbers, attrib);
                            break;
                        case "SecurityPrincipal":
                            securityPrincipal = this.Core.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Rights":
                            rightsValue = this.Core.GetAttributeValue(sourceLineNumbers, attrib);
                            switch (rightsValue)
                            {
                                case "all":
                                    rights = HttpConstants.GENERIC_ALL;
                                    break;
                                case "delegate":
                                    rights = HttpConstants.GENERIC_WRITE;
                                    break;
                                case "register":
                                    rights = HttpConstants.GENERIC_EXECUTE;
                                    break;
                                default:
                                    this.Core.OnMessage(WixErrors.IllegalAttributeValue(sourceLineNumbers, node.Name.LocalName, "Rights", rightsValue, "all", "delegate", "register"));
                                    break;
                            }
                            break;
                        default:
                            this.Core.UnexpectedAttribute(node, attrib);
                            break;
                    }
                }
                else
                {
                    this.Core.ParseExtensionAttribute(node, attrib);
                }
            }

            // Generate Id now if not authored.
            if (null == id)
            {
                id = this.Core.CreateIdentifier("ace", urlReservationId, securityPrincipal, rightsValue);
            }

            this.Core.ParseForExtensionElements(node);

            // SecurityPrincipal is required.
            if (null == securityPrincipal)
            {
                this.Core.OnMessage(WixErrors.ExpectedAttribute(sourceLineNumbers, node.Name.LocalName, "SecurityPrincipal"));
            }

            if (!this.Core.EncounteredError)
            {
                Row row = this.Core.CreateRow(sourceLineNumbers, "WixHttpUrlAce");
                row[0] = id.Id;
                row[1] = urlReservationId;
                row[2] = securityPrincipal;
                row[3] = rights;
            }
        }
    }
}
