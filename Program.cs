using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BrowserDefaults
{
    public class Browser
    {
        public string Name { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Identifier { get; set; } = "";
        public bool IsDefault { get; set; } = false;
    }

    public class BrowserManager
    {
        private readonly List<Browser> _browsers = new();

        public List<Browser> GetInstalledBrowsers()
        {
            _browsers.Clear();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DetectWindowsBrowsers();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                DetectMacOSBrowsers();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                DetectLinuxBrowsers();
            }

            return _browsers.OrderBy(b => b.Name).ToList();
        }

        private void DetectWindowsBrowsers()
        {
            // Common Windows browsers
            var windowsBrowsers = new Dictionary<string, string>
            {
                { "Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe" },
                { "Chrome (x86)", @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe" },
                { "Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" },
                { "Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe" },
                { "Firefox (x86)", @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe" },
                { "Opera", @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Opera\opera.exe" },
                { "Brave", @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe" }
            };

            foreach (var browser in windowsBrowsers)
            {
                if (File.Exists(browser.Value))
                {
                    _browsers.Add(new Browser
                    {
                        Name = browser.Key.Replace(" (x86)", ""),
                        ExecutablePath = browser.Value,
                        Identifier = browser.Key.ToLower().Replace(" ", "").Replace("(x86)", "")
                    });
                }
            }

            // Detect default browser (simplified for Windows)
            var defaultBrowser = GetDefaultBrowserWindows();
            foreach (var browser in _browsers)
            {
                if (browser.Name.ToLower().Contains(defaultBrowser.ToLower()))
                {
                    browser.IsDefault = true;
                    break;
                }
            }
        }

        private void DetectMacOSBrowsers()
        {
            // Common macOS applications
            var macBrowsers = new Dictionary<string, string>
            {
                { "Chrome", "/Applications/Google Chrome.app" },
                { "Safari", "/Applications/Safari.app" },
                { "Firefox", "/Applications/Firefox.app" },
                { "Edge", "/Applications/Microsoft Edge.app" },
                { "Opera", "/Applications/Opera.app" },
                { "Brave", "/Applications/Brave Browser.app" }
            };

            foreach (var browser in macBrowsers)
            {
                if (Directory.Exists(browser.Value))
                {
                    _browsers.Add(new Browser
                    {
                        Name = browser.Key,
                        ExecutablePath = browser.Value,
                        Identifier = browser.Key.ToLower()
                    });
                }
            }

            // Detect default browser on macOS
            var defaultBrowser = GetDefaultBrowserMacOS();
            foreach (var browser in _browsers)
            {
                if (browser.Identifier == defaultBrowser.ToLower())
                {
                    browser.IsDefault = true;
                    break;
                }
            }
        }

        private void DetectLinuxBrowsers()
        {
            // Common Linux browsers with typical installation paths
            var linuxBrowsers = new Dictionary<string, string[]>
            {
                { "Chrome", new[] { "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/opt/google/chrome/chrome" } },
                { "Firefox", new[] { "/usr/bin/firefox", "/usr/bin/firefox-esr", "/snap/bin/firefox" } },
                { "Edge", new[] { "/usr/bin/microsoft-edge", "/usr/bin/microsoft-edge-stable" } },
                { "Opera", new[] { "/usr/bin/opera", "/usr/bin/opera-stable" } },
                { "Brave", new[] { "/usr/bin/brave-browser", "/usr/bin/brave" } },
                { "Chromium", new[] { "/usr/bin/chromium", "/usr/bin/chromium-browser", "/snap/bin/chromium" } }
            };

            foreach (var browser in linuxBrowsers)
            {
                foreach (var path in browser.Value)
                {
                    if (File.Exists(path))
                    {
                        _browsers.Add(new Browser
                        {
                            Name = browser.Key,
                            ExecutablePath = path,
                            Identifier = browser.Key.ToLower()
                        });
                        break; // Only add the first found path for each browser
                    }
                }
            }

            // Detect default browser on Linux
            var defaultBrowser = GetDefaultBrowserLinux();
            foreach (var browser in _browsers)
            {
                if (browser.Identifier == defaultBrowser.ToLower() || browser.ExecutablePath.Contains(defaultBrowser.ToLower()))
                {
                    browser.IsDefault = true;
                    break;
                }
            }
        }

        private string GetDefaultBrowserWindows()
        {
            try
            {
                var result = ExecuteCommand("reg", "query HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\Shell\\Associations\\UrlAssociations\\http\\UserChoice /v ProgId");
                if (result.Contains("ChromeHTML")) return "Chrome";
                if (result.Contains("MSEdgeHTM")) return "Edge";
                if (result.Contains("FirefoxURL")) return "Firefox";
                if (result.Contains("OperaStable")) return "Opera";
                if (result.Contains("BraveHTML")) return "Brave";
            }
            catch { }
            return "";
        }

        private string GetDefaultBrowserMacOS()
        {
            try
            {
                var result = ExecuteCommand("defaults", "read com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers | grep -A 3 -B 3 LSHandlerURLScheme | grep -A 3 http | grep LSHandlerRoleAll -A 1 | grep LSHandlerContentType -A 1 | tail -1");
                if (result.Contains("chrome")) return "chrome";
                if (result.Contains("safari")) return "safari";
                if (result.Contains("firefox")) return "firefox";
                if (result.Contains("edge")) return "edge";
                if (result.Contains("opera")) return "opera";
                if (result.Contains("brave")) return "brave";
            }
            catch { }
            return "";
        }

        private string GetDefaultBrowserLinux()
        {
            try
            {
                var result = ExecuteCommand("xdg-settings", "get default-web-browser");
                return result.Replace(".desktop", "").Trim();
            }
            catch { }
            return "";
        }

        public bool SetDefaultBrowser(Browser browser)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return SetDefaultBrowserWindows(browser);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return SetDefaultBrowserMacOS(browser);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return SetDefaultBrowserLinux(browser);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting default browser: {ex.Message}");
            }
            return false;
        }

        private bool SetDefaultBrowserWindows(Browser browser)
        {
            // Windows requires administrative privileges and is complex
            // For simplicity, we'll show instructions to the user
            Console.WriteLine($"To set {browser.Name} as default on Windows:");
            Console.WriteLine("1. Open Settings > Apps > Default apps");
            Console.WriteLine($"2. Search for {browser.Name} and set it as default for web browser");
            return false;
        }

        private bool SetDefaultBrowserMacOS(Browser browser)
        {
            var bundleId = GetMacOSBundleId(browser.Name);
            if (!string.IsNullOrEmpty(bundleId))
            {
                ExecuteCommand("open", $"-b com.apple.systempreferences /System/Library/PreferencePanes/Profiles.prefPane");
                Console.WriteLine($"Please manually set {browser.Name} as default in System Preferences > General > Default web browser");
                return false;
            }
            return false;
        }

        private bool SetDefaultBrowserLinux(Browser browser)
        {
            var desktopFile = GetLinuxDesktopFile(browser.Name);
            if (!string.IsNullOrEmpty(desktopFile))
            {
                var result = ExecuteCommand("xdg-settings", $"set default-web-browser {desktopFile}");
                return string.IsNullOrEmpty(result) || !result.Contains("error");
            }
            return false;
        }

        private string GetMacOSBundleId(string browserName)
        {
            return browserName.ToLower() switch
            {
                "chrome" => "com.google.Chrome",
                "safari" => "com.apple.Safari",
                "firefox" => "org.mozilla.firefox",
                "edge" => "com.microsoft.edgemac",
                "opera" => "com.operasoftware.Opera",
                "brave" => "com.brave.Browser",
                _ => ""
            };
        }

        private string GetLinuxDesktopFile(string browserName)
        {
            return browserName.ToLower() switch
            {
                "chrome" => "google-chrome.desktop",
                "firefox" => "firefox.desktop",
                "edge" => "microsoft-edge.desktop",
                "opera" => "opera.desktop",
                "brave" => "brave-browser.desktop",
                "chromium" => "chromium.desktop",
                _ => ""
            };
        }

        private string ExecuteCommand(string command, string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
            catch
            {
                return "";
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // output my operating system
            Console.WriteLine($"Operating System: {RuntimeInformation.OSDescription}");

            var browserManager = new BrowserManager();
            var browsers = browserManager.GetInstalledBrowsers();

            if (browsers.Count == 0)
            {
                Console.WriteLine("No browsers found on this system.");
                return;
            }

            if (args.Length == 0)
            {
                // Interactive mode - show list and wait for input
                ShowBrowserList(browsers);
                Console.WriteLine();
                Console.Write("Press ENTER to exit, or enter a number to set as default: ");
                
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    return; // User pressed ENTER to exit
                }

                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= browsers.Count)
                {
                    var selectedBrowser = browsers[choice - 1];
                    SetBrowserAsDefault(browserManager, selectedBrowser);
                }
                else
                {
                    Console.WriteLine("Invalid selection.");
                }
            }
            else if (args.Length == 1)
            {
                // Direct mode - set browser by number
                if (int.TryParse(args[0], out int choice) && choice >= 1 && choice <= browsers.Count)
                {
                    var selectedBrowser = browsers[choice - 1];
                    SetBrowserAsDefault(browserManager, selectedBrowser);
                }
                else
                {
                    Console.WriteLine($"Invalid browser number. Please choose between 1 and {browsers.Count}.");
                    ShowBrowserList(browsers);
                }
            }
            else
            {
                Console.WriteLine("Usage: brodef [browser_number]");
                Console.WriteLine("Run without arguments for interactive mode.");
            }
        }

        static void ShowBrowserList(List<Browser> browsers)
        {
            Console.WriteLine("Installed browsers:");
            for (int i = 0; i < browsers.Count; i++)
            {
                var defaultIndicator = browsers[i].IsDefault ? " (default)" : "";
                Console.WriteLine($"{i + 1}. {browsers[i].Name}{defaultIndicator}");
            }
        }

        static void SetBrowserAsDefault(BrowserManager browserManager, Browser browser)
        {
            Console.WriteLine($"Setting {browser.Name} as default browser...");
            
            if (browserManager.SetDefaultBrowser(browser))
            {
                Console.WriteLine($"{browser.Name} has been set as the default browser.");
            }
            else
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Console.WriteLine($"Attempted to set {browser.Name} as default browser.");
                    Console.WriteLine("If this didn't work, you may need to:");
                    Console.WriteLine("1. Install xdg-utils package");
                    Console.WriteLine("2. Set the browser manually in your system settings");
                }
                else
                {
                    Console.WriteLine("Please set the default browser manually in your system settings.");
                }
            }
        }
    }
}
