using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Cleanse10.Core.Unattended
{
    /// <summary>
    /// Generates Windows 10 unattend XML from an <see cref="UnattendedConfig"/>.
    ///
    /// Two output methods are provided:
    ///   <see cref="WriteToIsoRoot"/>  — writes autounattend.xml at the ISO source root.
    ///                                   Picked up by Windows Setup during the windowsPE pass
    ///                                   (disk partitioning, image selection, language choice).
    ///   <see cref="WriteToImage"/>    — writes Windows\Panther\unattend.xml inside the mounted
    ///                                   WIM.  Picked up after first reboot for specialize /
    ///                                   oobeSystem passes (hostname, account creation, OOBE skip).
    ///
    /// Together these two files provide a fully unattended installation when
    /// <see cref="UnattendedConfig.SkipOOBE"/> is true.
    /// </summary>
    public static class UnattendedGenerator
    {
        private const string NS  = "urn:schemas-microsoft-com:unattend";
        private const string WCM = "http://schemas.microsoft.com/WMIConfig/2002/State";

        // ──────────────────────────────────────────────────────────────────────
        // ── Public entry points ────────────────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes <c>autounattend.xml</c> to the root of the ISO source directory.
        /// This is the file Windows Setup reads during the <c>windowsPE</c> pass —
        /// it handles disk partitioning, edition selection, and locale.
        /// </summary>
        public static void WriteToIsoRoot(UnattendedConfig cfg, string isoSourceRoot)
        {
            Directory.CreateDirectory(isoSourceRoot);
            string dest = Path.Combine(isoSourceRoot, "autounattend.xml");
            File.WriteAllText(dest, GenerateAutoUnattend(cfg), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        /// <summary>
        /// Writes <c>unattend.xml</c> to <c>Windows\Panther\</c> inside the mounted image.
        /// This file is read after the first reboot during the <c>specialize</c> and
        /// <c>oobeSystem</c> passes — it sets the hostname, creates the local account,
        /// and skips OOBE prompts.
        /// </summary>
        public static void WriteToImage(UnattendedConfig cfg, string mountPath)
        {
            string pantherDir = Path.Combine(mountPath, "Windows", "Panther");
            Directory.CreateDirectory(pantherDir);
            string dest = Path.Combine(pantherDir, "unattend.xml");
            File.WriteAllText(dest, GeneratePantherUnattend(cfg), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        // ──────────────────────────────────────────────────────────────────────
        // ── autounattend.xml (windowsPE pass only) ─────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates the ISO-root <c>autounattend.xml</c>.
        /// Covers the <c>windowsPE</c> pass: locale, disk config, and image selection.
        /// The <c>specialize</c> / <c>oobeSystem</c> passes are intentionally omitted here
        /// and handled by <see cref="GeneratePantherUnattend"/> instead, so that both
        /// standard and AFK-install scenarios share the same disk-config logic.
        /// </summary>
        private static string GenerateAutoUnattend(UnattendedConfig cfg)
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
            xw.WriteAttributeString("xmlns", "wcm", null, WCM);

            // ── windowsPE pass ───────────────────────────────────────────────
            WritePass(xw, "windowsPE", () =>
            {
                // International settings for WinPE UI
                WriteComponent(xw, "Microsoft-Windows-International-Core-WinPE", () =>
                {
                    Elem(xw, "SetupUILanguage", () => Leaf(xw, "UILanguage", cfg.UILanguage));
                    Leaf(xw, "InputLocale",  cfg.InputLocale);
                    Leaf(xw, "SystemLocale", cfg.UILanguage);
                    Leaf(xw, "UILanguage",   cfg.UILanguage);
                    Leaf(xw, "UserLocale",   cfg.UserLocale);
                });

                // Disk configuration: wipe disk 0, create GPT layout with ESP + MSR + Windows partition
                WriteComponent(xw, "Microsoft-Windows-Setup", () =>
                {
                    Elem(xw, "DiskConfiguration", () =>
                    {
                        // WillShowUI: OnError ensures setup only blocks on actual errors.
                        Leaf(xw, "WillShowUI", "OnError");

                        // Disk 0
                        xw.WriteStartElement("Disk", NS);
                        xw.WriteAttributeString("wcm", "action", WCM, "add");
                        Leaf(xw, "DiskID",   "0");
                        Leaf(xw, "WillWipeDisk", "true");

                        Elem(xw, "CreatePartitions", () =>
                        {
                            // Partition 1 — EFI System Partition (100 MB)
                            xw.WriteStartElement("CreatePartition", NS);
                            xw.WriteAttributeString("wcm", "action", WCM, "add");
                            Leaf(xw, "Order",  "1");
                            Leaf(xw, "Type",   "EFI");
                            Leaf(xw, "Size",   "100");
                            xw.WriteEndElement();

                            // Partition 2 — Microsoft Reserved Partition (16 MB)
                            xw.WriteStartElement("CreatePartition", NS);
                            xw.WriteAttributeString("wcm", "action", WCM, "add");
                            Leaf(xw, "Order",  "2");
                            Leaf(xw, "Type",   "MSR");
                            Leaf(xw, "Size",   "16");
                            xw.WriteEndElement();

                            // Partition 3 — Windows (extends to fill the rest of the disk)
                            xw.WriteStartElement("CreatePartition", NS);
                            xw.WriteAttributeString("wcm", "action", WCM, "add");
                            Leaf(xw, "Order",    "3");
                            Leaf(xw, "Type",     "Primary");
                            Leaf(xw, "Extend",   "true");
                            xw.WriteEndElement();
                        });

                        Elem(xw, "ModifyPartitions", () =>
                        {
                            // Partition 1 — format as FAT32, assign drive letter S
                            xw.WriteStartElement("ModifyPartition", NS);
                            xw.WriteAttributeString("wcm", "action", WCM, "add");
                            Leaf(xw, "Order",       "1");
                            Leaf(xw, "PartitionID", "1");
                            Leaf(xw, "Label",       "System");
                            Leaf(xw, "Format",      "FAT32");
                            xw.WriteEndElement();

                            // Partition 3 — format as NTFS, assign drive letter C
                            xw.WriteStartElement("ModifyPartition", NS);
                            xw.WriteAttributeString("wcm", "action", WCM, "add");
                            Leaf(xw, "Order",       "2");
                            Leaf(xw, "PartitionID", "3");
                            Leaf(xw, "Label",       "Windows");
                            Leaf(xw, "Format",      "NTFS");
                            Leaf(xw, "Letter",      "C");
                            xw.WriteEndElement();
                        });

                        xw.WriteEndElement(); // </Disk>
                    });

                    // Image selection — which index in the WIM/ESD to install
                    Elem(xw, "ImageInstall", () =>
                    {
                        Elem(xw, "OSImage", () =>
                        {
                            Elem(xw, "InstallTo", () =>
                            {
                                Leaf(xw, "DiskID",       "0");
                                Leaf(xw, "PartitionID",  "3");
                            });
                            Elem(xw, "InstallFrom", () =>
                            {
                                Elem(xw, "MetaData", () =>
                                {
                                    xw.WriteAttributeString("wcm", "action", WCM, "add");
                                    Leaf(xw, "Key",   "/IMAGE/INDEX");
                                    Leaf(xw, "Value", cfg.WimIndex.ToString());
                                });
                            });
                            Leaf(xw, "WillShowUI", "OnError");
                        });
                    });

                    // UserData — product key and EULA acceptance
                    Elem(xw, "UserData", () =>
                    {
                        Leaf(xw, "AcceptEula", cfg.AcceptEula ? "true" : "false");
                        // ProductKey left absent → generic retail / volume activation
                    });
                });
            });

            xw.WriteEndElement(); // </unattend>
            xw.WriteEndDocument();

            return sb.ToString();
        }

        // ──────────────────────────────────────────────────────────────────────
        // ── Windows\Panther\unattend.xml (specialize + oobeSystem passes) ──────
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates the Panther <c>unattend.xml</c>.
        /// Covers the <c>specialize</c> pass (hostname, timezone) and the
        /// <c>oobeSystem</c> pass (OOBE skip, local account creation).
        /// </summary>
        private static string GeneratePantherUnattend(UnattendedConfig cfg)
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
            xw.WriteAttributeString("xmlns", "wcm", null, WCM);

            // ── specialize pass ──────────────────────────────────────────────
            WritePass(xw, "specialize", () =>
            {
                WriteComponent(xw, "Microsoft-Windows-Shell-Setup", () =>
                {
                    Leaf(xw, "ComputerName", cfg.ComputerName);
                    Leaf(xw, "TimeZone",     cfg.TimeZone);
                });
            });

            // ── oobeSystem pass ──────────────────────────────────────────────
            WritePass(xw, "oobeSystem", () =>
            {
                WriteComponent(xw, "Microsoft-Windows-Shell-Setup", () =>
                {
                    if (cfg.SkipOOBE)
                    {
                        Elem(xw, "OOBE", () =>
                        {
                            Leaf(xw, "HideEULAPage",            cfg.HideEulaPage     ? "true" : "false");
                            Leaf(xw, "HideWirelessSetupInOOBE", cfg.HideWirelessPage ? "true" : "false");
                            Leaf(xw, "SkipMachineOOBE",         "true");
                            Leaf(xw, "SkipUserOOBE",            "true");
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(cfg.AdminUsername))
                    {
                        Elem(xw, "UserAccounts", () =>
                        {
                            Elem(xw, "LocalAccounts", () =>
                            {
                                xw.WriteStartElement("LocalAccount", NS);
                                xw.WriteAttributeString("wcm", "action", WCM, "add");
                                Elem(xw, "Password", () =>
                                {
                                    Leaf(xw, "Value",     cfg.AdminPassword ?? string.Empty);
                                    Leaf(xw, "PlainText", "true");
                                });
                                Leaf(xw, "Description", string.Empty);
                                Leaf(xw, "DisplayName",  cfg.AdminUsername!);
                                Leaf(xw, "Group",        "Administrators");
                                Leaf(xw, "Name",         cfg.AdminUsername!);
                                xw.WriteEndElement();
                            });
                        });

                        // Auto-logon on first boot so the RunOnce scripts fire
                        Elem(xw, "AutoLogon", () =>
                        {
                            Leaf(xw, "Username", cfg.AdminUsername!);
                            Elem(xw, "Password", () =>
                            {
                                Leaf(xw, "Value",     cfg.AdminPassword ?? string.Empty);
                                Leaf(xw, "PlainText", "true");
                            });
                            Leaf(xw, "LogonCount", "1");
                            Leaf(xw, "Enabled",    "true");
                        });
                    }
                });
            });

            xw.WriteEndElement(); // </unattend>
            xw.WriteEndDocument();

            return sb.ToString();
        }

        // ──────────────────────────────────────────────────────────────────────
        // ── XML helpers ────────────────────────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

        private static void WritePass(XmlWriter xw, string pass, Action content)
        {
            xw.WriteStartElement("settings", NS);
            xw.WriteAttributeString("pass", pass);
            content();
            xw.WriteEndElement();
        }

        private static void WriteComponent(XmlWriter xw, string name, Action content)
        {
            xw.WriteStartElement("component", NS);
            xw.WriteAttributeString("name", name);
            xw.WriteAttributeString("processorArchitecture", "amd64");
            xw.WriteAttributeString("publicKeyToken", "31bf3856ad364e35");
            xw.WriteAttributeString("language", "neutral");
            xw.WriteAttributeString("versionScope", "nonSxS");
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
