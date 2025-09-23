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
            // Use macOS Launch Services to dynamically detect all browsers that can handle HTTP/HTTPS URLs
            // This approach uses official macOS APIs instead of hardcoded paths, ensuring accuracy and robustness
            var detectedBrowsers = GetMacOSBrowsersFromLaunchServices();
            
            foreach (var browserInfo in detectedBrowsers)
            {
                _browsers.Add(new Browser
                {
                    Name = browserInfo.Name,
                    ExecutablePath = browserInfo.Path,
                    Identifier = browserInfo.BundleId
                });
            }

            // Detect default browser using official macOS Launch Services APIs
            var defaultBrowserBundleId = GetDefaultBrowserMacOS();
            foreach (var browser in _browsers)
            {
                if (browser.Identifier.Equals(defaultBrowserBundleId, StringComparison.OrdinalIgnoreCase))
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

        private List<(string Name, string Path, string BundleId)> GetMacOSBrowsersFromLaunchServices()
        {
            var browsers = new List<(string Name, string Path, string BundleId)>();
            
            try
            {
                // Use mdfind to find all applications that can handle HTTP URLs
                // This queries the Launch Services database directly 
                var result = ExecuteCommand("mdfind", "kMDItemCFBundleIdentifier = '*' && kMDItemContentTypeTree = 'com.apple.application-bundle'");
                var appPaths = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var appPath in appPaths)
                {
                    if (!appPath.EndsWith(".app") || !Directory.Exists(appPath))
                        continue;
                        
                    try
                    {
                        // Get bundle identifier for this app
                        var bundleIdResult = ExecuteCommand("mdls", $"-name kMDItemCFBundleIdentifier -r \"{appPath}\"");
                        if (string.IsNullOrEmpty(bundleIdResult) || bundleIdResult.Contains("(null)"))
                            continue;
                            
                        var bundleId = bundleIdResult.Trim();
                        
                        // Check if this app can handle HTTP URLs by checking its Info.plist
                        var canHandleHttp = CheckIfAppCanHandleHttpUrls(appPath, bundleId);
                        if (!canHandleHttp)
                            continue;
                            
                        // Get the display name
                        var displayNameResult = ExecuteCommand("mdls", $"-name kMDItemDisplayName -r \"{appPath}\"");
                        var displayName = !string.IsNullOrEmpty(displayNameResult) && !displayNameResult.Contains("(null)") 
                            ? displayNameResult.Trim() 
                            : Path.GetFileNameWithoutExtension(appPath);
                            
                        // Clean up the display name (remove .app extension if present)
                        if (displayName.EndsWith(".app"))
                            displayName = displayName.Substring(0, displayName.Length - 4);
                            
                        browsers.Add((displayName, appPath, bundleId));
                    }
                    catch
                    {
                        // Skip applications that we can't query properly
                        continue;
                    }
                }
                
                // Deduplicate by bundle ID and sort by name
                browsers = browsers
                    .GroupBy(b => b.BundleId)
                    .Select(g => g.First())
                    .OrderBy(b => b.Name)
                    .ToList();
            }
            catch
            {
                // Fallback: if the dynamic detection fails, use a minimal set of common browsers
                // This ensures the app still works even if Launch Services queries fail
                var fallbackBrowsers = new Dictionary<string, string>
                {
                    { "Safari", "/System/Applications/Safari.app" },
                    { "Google Chrome", "/Applications/Google Chrome.app" },
                    { "Firefox", "/Applications/Firefox.app" },
                    { "Microsoft Edge", "/Applications/Microsoft Edge.app" }
                };
                
                foreach (var browser in fallbackBrowsers)
                {
                    if (Directory.Exists(browser.Value))
                    {
                        var bundleIdResult = ExecuteCommand("mdls", $"-name kMDItemCFBundleIdentifier -r \"{browser.Value}\"");
                        var bundleId = !string.IsNullOrEmpty(bundleIdResult) && !bundleIdResult.Contains("(null)") 
                            ? bundleIdResult.Trim() 
                            : browser.Key.ToLower().Replace(" ", ".");
                            
                        browsers.Add((browser.Key, browser.Value, bundleId));
                    }
                }
            }
            
            return browsers;
        }
        
        private bool CheckIfAppCanHandleHttpUrls(string appPath, string bundleId)
        {
            try
            {
                // Check the app's Info.plist for URL scheme handlers
                var infoPlistPath = Path.Combine(appPath, "Contents", "Info.plist");
                if (!File.Exists(infoPlistPath))
                    return false;
                    
                // Use plutil to extract URL schemes from Info.plist
                var result = ExecuteCommand("plutil", $"-extract CFBundleURLTypes json -o - \"{infoPlistPath}\"");
                if (string.IsNullOrEmpty(result) || result.Contains("does not exist"))
                    return false;
                    
                // Check if the result contains http or https schemes
                return result.Contains("\"http\"") || result.Contains("\"https\"") || 
                       IsKnownWebBrowser(bundleId);
            }
            catch
            {
                // If we can't read the plist, check if it's a known web browser by bundle ID
                return IsKnownWebBrowser(bundleId);
            }
        }
        
        private bool IsKnownWebBrowser(string bundleId)
        {
            // List of known web browser bundle identifiers
            // This serves as a fallback when plist parsing fails
            var knownBrowsers = new[]
            {
                "com.apple.Safari",
                "com.google.Chrome",
                "org.mozilla.firefox",
                "com.microsoft.edgemac",
                "com.operasoftware.Opera",
                "com.brave.Browser",
                "com.vivaldi.Vivaldi",
                "org.webkit.nightly.WebKit",
                "com.arc.Arc",
                "com.SigmaOS.SigmaOS",
                "com.ghostbrowsers.ghostbrowser",
                "com.choosy.choosy"
            };
            
            return knownBrowsers.Any(known => bundleId.Contains(known, StringComparison.OrdinalIgnoreCase));
        }

        private string GetDefaultBrowserMacOS()
        {
            try
            {
                // Method 1: Use plutil to read and parse the Launch Services database directly
                // This is more reliable than parsing raw defaults output and avoids Python dependency
                var tempFile = Path.GetTempFileName();
                try
                {
                    // Export Launch Services handlers to a temporary plist file
                    var exportResult = ExecuteCommand("defaults", $"export com.apple.LaunchServices/com.apple.launchservices.secure \"{tempFile}\"");
                    
                    if (File.Exists(tempFile))
                    {
                        // Use plutil to extract LSHandlers array in JSON format
                        var handlersJson = ExecuteCommand("plutil", $"-extract LSHandlers json -o - \"{tempFile}\"");
                        
                        if (!string.IsNullOrEmpty(handlersJson) && handlersJson.Trim().StartsWith("["))
                        {
                            // Parse the JSON to find the HTTP handler using System.Text.Json
                            var bundleId = ParseHttpHandlerFromJson(handlersJson);
                            if (!string.IsNullOrEmpty(bundleId))
                            {
                                return bundleId;
                            }
                        }
                    }
                }
                finally
                {
                    // Clean up temporary file
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                
                // Method 2: Use duti command if available (alternative method)
                var result = ExecuteCommand("duti", "-x http");
                if (!string.IsNullOrEmpty(result) && result.Contains("Bundle ID:"))
                {
                    var lines = result.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Trim().StartsWith("Bundle ID:"))
                        {
                            return line.Replace("Bundle ID:", "").Trim();
                        }
                    }
                }
                
                // Method 3: Direct defaults read with proper parsing
                result = ExecuteCommand("defaults", "read com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers");
                if (!string.IsNullOrEmpty(result))
                {
                    // Look for the http handler in the output
                    var lines = result.Split('\n');
                    bool foundHttpHandler = false;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains("LSHandlerURLScheme") && lines[i].Contains("http"))
                        {
                            foundHttpHandler = true;
                        }
                        
                        if (foundHttpHandler && lines[i].Contains("LSHandlerRoleAll"))
                        {
                            // Extract bundle ID from the line
                            var bundleIdLine = lines[i].Trim();
                            if (bundleIdLine.Contains("="))
                            {
                                var bundleId = bundleIdLine.Split('=')[1].Trim().Trim('"', ';', ' ');
                                if (!string.IsNullOrEmpty(bundleId))
                                {
                                    return bundleId;
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch { }
            
            return "";
        }

        private string ParseHttpHandlerFromJson(string handlersJson)
        {
            try
            {
                using var document = JsonDocument.Parse(handlersJson);
                var root = document.RootElement;
                
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var handler in root.EnumerateArray())
                    {
                        if (handler.ValueKind == JsonValueKind.Object)
                        {
                            // Look for HTTP URL scheme handler
                            if (handler.TryGetProperty("LSHandlerURLScheme", out var scheme) && 
                                scheme.GetString() == "http")
                            {
                                // Get the bundle identifier for all roles
                                if (handler.TryGetProperty("LSHandlerRoleAll", out var bundleIdElement))
                                {
                                    return bundleIdElement.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If JSON parsing fails, fall back to string parsing
                return ParseHttpHandlerFromString(handlersJson);
            }
            
            return "";
        }

        private string ParseHttpHandlerFromString(string handlersJson)
        {
            try
            {
                // Simple string-based parsing as fallback
                var lines = handlersJson.Split('\n');
                bool inHttpHandler = false;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    
                    if (line.Contains("\"LSHandlerURLScheme\"") && line.Contains("\"http\""))
                    {
                        inHttpHandler = true;
                    }
                    else if (inHttpHandler && line.Contains("\"LSHandlerRoleAll\""))
                    {
                        // Extract the bundle ID from this line
                        var colonIndex = line.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < line.Length - 1)
                        {
                            var bundleId = line.Substring(colonIndex + 1)
                                .Trim()
                                .Trim('"', ',', ' ');
                            if (!string.IsNullOrEmpty(bundleId))
                            {
                                return bundleId;
                            }
                        }
                        break;
                    }
                    else if (line.Contains("}") && inHttpHandler)
                    {
                        // End of this handler object
                        inHttpHandler = false;
                    }
                }
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
            // Use the actual bundle identifier we detected dynamically
            var bundleId = browser.Identifier;
            if (!string.IsNullOrEmpty(bundleId))
            {
                // Open System Settings/Preferences to the General section where default browser is set
                if (ExecuteCommand("sw_vers", "-productVersion").StartsWith("13") || 
                    ExecuteCommand("sw_vers", "-productVersion").StartsWith("14") ||
                    ExecuteCommand("sw_vers", "-productVersion").StartsWith("15"))
                {
                    // macOS 13+ uses System Settings
                    ExecuteCommand("open", "x-apple.systempreferences:com.apple.preference.general");
                }
                else
                {
                    // Older macOS versions use System Preferences
                    ExecuteCommand("open", "/System/Library/PreferencePanes/General.prefPane");
                }
                
                Console.WriteLine($"Please manually set {browser.Name} as default in System Settings > General > Default web browser");
                Console.WriteLine($"(Bundle ID: {bundleId})");
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
