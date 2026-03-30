using System.Collections.Generic;

namespace Cleanse10.Core.Bloat
{
    /// <summary>
    /// Windows 10–specific list of provisioned AppX packages to remove.
    ///
    /// Notes:
    ///   - Copilot is not a packaged AppX on Win10 — no entry needed.
    ///   - Teams on Win10 ships as "MicrosoftTeams" (Classic Teams), not the Store package name.
    ///   - Cortana is a built-in system binary on Win10, not a removable AppX; disable via policy instead.
    ///   - Xbox apps are packaged and removable.
    /// </summary>
    public static class BloatwareList10
    {
        public static readonly IReadOnlyList<string> Packages = new[]
        {
            // ── Microsoft apps ────────────────────────────────────────────────
            "Microsoft.3DBuilder",
            "Microsoft.BingFinance",
            "Microsoft.BingFoodAndDrink",
            "Microsoft.BingHealthAndFitness",
            "Microsoft.BingMaps",
            "Microsoft.BingNews",
            "Microsoft.BingSports",
            "Microsoft.BingTranslator",
            "Microsoft.BingTravel",
            "Microsoft.BingWeather",
            "Microsoft.GetHelp",
            "Microsoft.Getstarted",
            "Microsoft.Messaging",
            "Microsoft.Microsoft3DViewer",
            "Microsoft.MicrosoftOfficeHub",
            "Microsoft.MicrosoftSolitaireCollection",
            "Microsoft.MicrosoftStickyNotes",
            "Microsoft.MixedReality.Portal",
            "Microsoft.MSPaint",                          // Paint 3D (not classic Paint)
            "Microsoft.NetworkSpeedTest",
            "Microsoft.News",
            "Microsoft.Office.OneNote",
            "Microsoft.Office.Sway",
            "Microsoft.OneConnect",
            "Microsoft.People",
            "Microsoft.Print3D",
            "Microsoft.RemoteDesktop",
            "Microsoft.ScreenSketch",
            "Microsoft.SkypeApp",
            "Microsoft.StorePurchaseApp",
            "Microsoft.Todos",
            "Microsoft.Wallet",
            "Microsoft.WebMediaExtensions",
            "Microsoft.Windows.Photos",
            "Microsoft.WindowsAlarms",
            "Microsoft.WindowsCamera",
            "microsoft.windowscommunicationsapps",        // Mail & Calendar
            "Microsoft.WindowsFeedbackHub",
            "Microsoft.WindowsMaps",
            "Microsoft.WindowsSoundRecorder",
            "Microsoft.YourPhone",
            "Microsoft.ZuneMusic",                        // Groove Music
            "Microsoft.ZuneVideo",                        // Movies & TV

            // ── Games ────────────────────────────────────────────────────────
            "Microsoft.MicrosoftMahjong",
            "Microsoft.MicrosoftMinesweeper",
            "Microsoft.MicrosoftUltimateWordGames",
            "king.com.CandyCrushSodaSaga",

            // ── Communication / collaboration apps ────────────────────────────
            "Clipchamp.Clipchamp",
            "MicrosoftTeams",                             // Classic Teams (Win10)
            "MicrosoftCorporationII.QuickAssist",
            "Microsoft.MicrosoftFamilySafety",

            // ── Automation / productivity ──────────────────────────────────────
            "Microsoft.PowerAutomateDesktop",
            "LinkedIn.LinkedIn",

            // ── Xbox / Gaming ────────────────────────────────────────────────
            "Microsoft.GamingApp",
            "Microsoft.GamingServices",
            "Microsoft.Xbox.TCUI",
            "Microsoft.XboxApp",
            "Microsoft.XboxGameOverlay",
            "Microsoft.XboxGamingOverlay",
            "Microsoft.XboxIdentityProvider",
            "Microsoft.XboxSpeechToTextOverlay",

            // ── OneDrive (Store version, separate from desktop client) ────────
            "Microsoft.OneDriveSync",

            // ── Third-party OEM-style apps ────────────────────────────────────
            "4DF9E0F8.Netflix",
            "828B5831.HiddenCity",
            "A278AB0D.MarchofEmpires",
            "DB6EA5DB.CyberLinkMediaSuiteEssentials",
            "E046963F.LenovoCompanion",
            "Facebook.Facebook",
            "flaregamesGmbH.RoyalRevolt2",
            "king.com.BubbleWitch3Saga",
            "king.com.CandyCrushFriends",
            "king.com.CandyCrushSaga",
            "SpotifyAB.SpotifyMusic",
            "TikTok.TikTok",
            "Twitter.Twitter",
        };
    }
}
