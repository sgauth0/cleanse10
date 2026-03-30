using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Cleanse10.Core.Unattended
{
    /// <summary>
    /// Generates a Windows 10 unattend.xml from an <see cref="UnattendedConfig"/>.
    /// </summary>
    public static class UnattendedGenerator
    {
        private const string NS = "urn:schemas-microsoft-com:unattend";

        public static string Generate(UnattendedConfig cfg)
        {
            var sb  = new StringBuilder();
            var xws = new XmlWriterSettings
            {
                Indent             = true,
                IndentChars        = "    ",
                Encoding           = Encoding.UTF8,
                OmitXmlDeclaration = false,
            };

            using var xw = XmlWriter.Create(sb, xws);

            xw.WriteStartDocument();
            xw.WriteStartElement("unattend", NS);
            xw.WriteAttributeString("xmlns", "wcm", null, "http://schemas.microsoft.com/WMIConfig/2002/State");

            // ── windowsPE pass ───────────────────────────────────────────────
            WritePass(xw, "windowsPE", () =>
            {
                // International settings for WinPE
                xw.WriteStartElement("component", NS);
                xw.WriteAttributeString("name", "Microsoft-Windows-International-Core-WinPE");
                xw.WriteAttributeString("processorArchitecture", "amd64");
                xw.WriteAttributeString("publicKeyToken", "31bf3856ad364e35");
                xw.WriteAttributeString("language", "neutral");
                xw.WriteAttributeString("versionScope", "nonSxS");
                Elem(xw, "SetupUILanguage", () => Leaf(xw, "UILanguage", cfg.UILanguage));
                Leaf(xw, "InputLocale",  cfg.InputLocale);
                Leaf(xw, "SystemLocale", cfg.UILanguage);
                Leaf(xw, "UILanguage",   cfg.UILanguage);
                Leaf(xw, "UserLocale",   cfg.UserLocale);
                xw.WriteEndElement();
            });

            // ── specialize pass ──────────────────────────────────────────────
            WritePass(xw, "specialize", () =>
            {
                xw.WriteStartElement("component", NS);
                xw.WriteAttributeString("name", "Microsoft-Windows-Shell-Setup");
                xw.WriteAttributeString("processorArchitecture", "amd64");
                xw.WriteAttributeString("publicKeyToken", "31bf3856ad364e35");
                xw.WriteAttributeString("language", "neutral");
                xw.WriteAttributeString("versionScope", "nonSxS");
                Leaf(xw, "ComputerName", cfg.ComputerName);
                Leaf(xw, "TimeZone",     cfg.TimeZone);
                xw.WriteEndElement();
            });

            // ── oobeSystem pass ──────────────────────────────────────────────
            WritePass(xw, "oobeSystem", () =>
            {
                xw.WriteStartElement("component", NS);
                xw.WriteAttributeString("name", "Microsoft-Windows-Shell-Setup");
                xw.WriteAttributeString("processorArchitecture", "amd64");
                xw.WriteAttributeString("publicKeyToken", "31bf3856ad364e35");
                xw.WriteAttributeString("language", "neutral");
                xw.WriteAttributeString("versionScope", "nonSxS");

                if (cfg.SkipOOBE)
                {
                    Elem(xw, "OOBE", () =>
                    {
                        Leaf(xw, "HideEULAPage",         cfg.HideEulaPage     ? "true" : "false");
                        Leaf(xw, "HideWirelessSetupInOOBE", cfg.HideWirelessPage ? "true" : "false");
                        Leaf(xw, "SkipMachineOOBE",      "true");
                        Leaf(xw, "SkipUserOOBE",         "true");
                    });
                }

                if (!string.IsNullOrWhiteSpace(cfg.AdminUsername))
                {
                    Elem(xw, "UserAccounts", () =>
                    {
                        Elem(xw, "LocalAccounts", () =>
                        {
                            xw.WriteStartElement("LocalAccount", NS);
                            xw.WriteAttributeString("wcm:action", "add");
                            Elem(xw, "Password", () =>
                            {
                                Leaf(xw, "Value",     cfg.AdminPassword ?? string.Empty);
                                Leaf(xw, "PlainText", "true");
                            });
                            Leaf(xw, "Description", "");
                            Leaf(xw, "DisplayName",  cfg.AdminUsername!);
                            Leaf(xw, "Group",        "Administrators");
                            Leaf(xw, "Name",         cfg.AdminUsername!);
                            xw.WriteEndElement();
                        });
                    });
                }

                Leaf(xw, "RegisteredOEMInformation", "");
                xw.WriteEndElement();
            });

            xw.WriteEndElement(); // </unattend>
            xw.WriteEndDocument();

            return sb.ToString();
        }

        /// <summary>Writes the unattend.xml to a file inside the mounted image at Windows\Panther\unattend.xml.</summary>
        public static void WriteToImage(UnattendedConfig cfg, string mountPath)
        {
            string pantherDir = Path.Combine(mountPath, "Windows", "Panther");
            Directory.CreateDirectory(pantherDir);
            string dest = Path.Combine(pantherDir, "unattend.xml");
            File.WriteAllText(dest, Generate(cfg), Encoding.UTF8);
        }

        // ──────────────────────────────────────────────────────────────────────
        // XML helpers
        // ──────────────────────────────────────────────────────────────────────

        private static void WritePass(XmlWriter xw, string pass, Action content)
        {
            xw.WriteStartElement("settings", NS);
            xw.WriteAttributeString("pass", pass);
            content();
            xw.WriteEndElement();
        }

        private static void Elem(XmlWriter xw, string name, Action inner)
        {
            xw.WriteStartElement(name, NS);
            inner();
            xw.WriteEndElement();
        }

        private static void Leaf(XmlWriter xw, string name, string value)
        {
            xw.WriteStartElement(name, NS);
            xw.WriteString(value);
            xw.WriteEndElement();
        }
    }
}
