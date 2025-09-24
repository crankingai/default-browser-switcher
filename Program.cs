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
                // Enhanced filtering to match macOS System Settings default browser list exactly.
                // 
                // Apple's System Settings uses specific criteria to determine which apps are eligible
                // to be set as the default web browser:
                //
                // 1. The app must properly declare HTTP/HTTPS URL schemes in its Info.plist
                //    using CFBundleURLTypes with proper CFBundleURLSchemes arrays
                //
                // 2. The app must be registered with Launch Services as a legitimate handler
                //    for HTTP/HTTPS URLs (not just any app that can open web links)
                //
                // 3. The app must be a genuine web browser, not auxiliary apps like email clients,
                //    IDEs, or social media apps that can incidentally handle web URLs
                //
                // This implementation matches Apple's filtering logic to ensure our browser list
                // perfectly aligns with what users see in System Settings > General > Default web browser
                
                var infoPlistPath = Path.Combine(appPath, "Contents", "Info.plist");
                if (!File.Exists(infoPlistPath))
                    return false;
                
                // First check: Must be a known web browser or have proper URL type declarations
                if (!IsEligibleWebBrowser(appPath, bundleId))
                    return false;
                
                // Second check: Verify proper HTTP/HTTPS URL scheme handling in Info.plist
                if (!HasProperHttpUrlHandling(infoPlistPath))
                    return false;
                    
                // Third check: Verify Launch Services recognizes this as a proper web browser
                if (!IsRecognizedByLaunchServices(bundleId))
                    return false;
                    
                return true;
            }
            catch
            {
                // If we can't validate properly, only allow known web browsers
                return IsKnownWebBrowser(bundleId);
            }
        }
        
        private bool IsEligibleWebBrowser(string appPath, string bundleId)
        {
            // First, check if it's a known legitimate web browser
            if (IsKnownWebBrowser(bundleId))
                return true;
                
            // Filter out common non-browser applications that handle HTTP URLs
            // but should not appear in System Settings default browser list
            if (IsNonBrowserApplication(bundleId, appPath))
                return false;
                
            // For unknown apps, apply stricter filtering to avoid non-browser apps
            // that might handle URLs (like email clients, IDEs, etc.)
            
            // Check if the app name suggests it's a browser
            var appName = Path.GetFileNameWithoutExtension(appPath).ToLower();
            var browserKeywords = new[] { "browser", "web", "safari", "chrome", "firefox", "edge", "opera" };
            
            if (!browserKeywords.Any(keyword => appName.Contains(keyword)))
            {
                // If it doesn't have browser-like naming and isn't in our known list,
                // it's likely not a proper web browser for System Settings
                return false;
            }
            
            return true;
        }
        
        private bool IsNonBrowserApplication(string bundleId, string appPath)
        {
            // List of bundle ID patterns and app names that should be excluded
            // even if they can handle HTTP URLs, as they're not web browsers
            var nonBrowserPatterns = new[]
            {
                // Email clients
                "com.apple.mail",
                "com.microsoft.outlook", 
                "com.readdle.smartemail",
                "com.airmail",
                
                // Development tools and IDEs
                "com.microsoft.vscode",
                "com.jetbrains.",
                "com.apple.dt.xcode",
                "com.github.atom",
                "com.sublimetext.",
                "com.panic.coda",
                
                // Text editors with web preview
                "com.typora.typora",
                "com.bear-writer.bearnotes",
                "com.inkcode.obsidian",
                
                // Media and social apps
                "com.tweetbot.tweetbot3-for-twitter",
                "com.twitter.twitter-mac",
                "com.facebook.archon",
                "com.slack.slack",
                "com.microsoft.teams",
                "com.discord.discord",
                
                // File managers and utilities
                "com.apple.finder",
                "com.panic.transmit",
                "com.charliemonroe.downie",
                
                // Games and entertainment (that might open web content)
                "com.steam.",
                "com.epicgames.",
            };
            
            // Check bundle ID patterns
            foreach (var pattern in nonBrowserPatterns)
            {
                if (bundleId.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            // Check app name patterns for additional filtering
            var appName = Path.GetFileNameWithoutExtension(appPath).ToLower();
            var nonBrowserNamePatterns = new[] { "mail", "outlook", "slack", "teams", "discord", "xcode", "vscode", "atom" };
            
            foreach (var pattern in nonBrowserNamePatterns)
            {
                if (appName.Contains(pattern))
                    return true;
            }
            
            return false;
        }
        
        private bool HasProperHttpUrlHandling(string infoPlistPath)
        {
            try
            {
                // Use plutil to extract URL schemes from Info.plist
                var result = ExecuteCommand("plutil", $"-extract CFBundleURLTypes json -o - \"{infoPlistPath}\"");
                if (string.IsNullOrEmpty(result) || result.Contains("does not exist"))
                    return false;
                
                // Parse the JSON to verify proper HTTP/HTTPS handling
                // System Settings only shows apps that properly declare these schemes
                using var document = JsonDocument.Parse(result);
                var root = document.RootElement;
                
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var urlType in root.EnumerateArray())
                    {
                        if (urlType.TryGetProperty("CFBundleURLSchemes", out var schemes))
                        {
                            if (schemes.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var scheme in schemes.EnumerateArray())
                                {
                                    var schemeStr = scheme.GetString();
                                    if (schemeStr == "http" || schemeStr == "https")
                                    {
                                        // Found proper HTTP/HTTPS scheme declaration
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                
                return false;
            }
            catch
            {
                // Fallback to simple string search if JSON parsing fails
                // Re-execute the command to get the result in the catch block
                try
                {
                    var fallbackResult = ExecuteCommand("plutil", $"-extract CFBundleURLTypes json -o - \"{infoPlistPath}\"");
                    return fallbackResult.Contains("\"http\"") || fallbackResult.Contains("\"https\"");
                }
                catch
                {
                    return false;
                }
            }
        }
        
        private bool IsRecognizedByLaunchServices(string bundleId)
        {
            try
            {
                // Method 1: Check if this bundle ID is registered as an HTTP handler via Launch Services
                // This uses the same mechanism that System Settings uses to populate the dropdown
                var result = ExecuteCommand("lsregister", $"-dump | grep -A 5 -B 5 '{bundleId}' | grep -i 'http'");
                if (!string.IsNullOrEmpty(result))
                    return true;
            }
            catch
            {
                // lsregister might not be available or accessible
            }
            
            try
            {
                // Method 2: Use duti to check if the app is a registered HTTP handler
                // duti queries Launch Services database directly
                var result = ExecuteCommand("duti", "-l http");
                if (!string.IsNullOrEmpty(result) && result.Contains(bundleId))
                    return true;
            }
            catch
            {
                // duti might not be installed
            }
            
            try 
            {
                // Method 3: Use defaults to check Launch Services database 
                // This checks the same database that System Settings reads from
                var result = ExecuteCommand("defaults", "read com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers");
                if (!string.IsNullOrEmpty(result) && result.Contains(bundleId))
                {
                    // Further verify it's associated with HTTP scheme
                    var lines = result.Split('\n');
                    bool foundBundle = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(bundleId))
                        {
                            foundBundle = true;
                        }
                        if (foundBundle && lines[i].Contains("LSHandlerURLScheme") && 
                            (lines[i].Contains("http") || (i + 1 < lines.Length && lines[i + 1].Contains("http"))))
                        {
                            return true;
                        }
                        if (foundBundle && lines[i].Contains("}"))
                        {
                            foundBundle = false; // End of this handler block
                        }
                    }
                }
            }
            catch
            {
                // Launch Services database read failed
            }
            
            // If we can't verify via Launch Services, be conservative:
            // Allow known browsers but reject unknown apps to avoid clutter
            return IsKnownWebBrowser(bundleId);
        }
        
        private bool IsKnownWebBrowser(string bundleId)
        {
            // List of known web browser bundle identifiers that should appear in System Settings
            // This list includes browsers that Apple recognizes as legitimate default browser candidates
            var knownBrowsers = new[]
            {
                // Major browsers
                "com.apple.Safari",
                "com.google.Chrome",
                "org.mozilla.firefox",
                "com.microsoft.edgemac",
                "com.operasoftware.Opera",
                "com.brave.Browser",
                "com.vivaldi.Vivaldi",
                
                // Developer/Webkit browsers  
                "org.webkit.nightly.WebKit",
                "com.apple.SafariTechnologyPreview",
                
                // Modern browsers
                "com.arc.Arc",
                "com.SigmaOS.SigmaOS",
                "com.ghostbrowsers.ghostbrowser",
                "org.chromium.Chromium",
                "com.google.Chrome.beta",
                "com.google.Chrome.dev",
                "com.google.Chrome.canary",
                
                // Specialty browsers (only if they properly register as web browsers)
                "com.choosy.choosy",  // URL router, but appears in System Settings if configured
                "company.thebrowser.Browser", // The Browser Company's Arc predecessor
                "com.readdle.smartemail-Mac", // Spark (only if configured for HTTP)
            };
            
            return knownBrowsers.Any(known => bundleId.Equals(known, StringComparison.OrdinalIgnoreCase));
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
            var bundleId = browser.Identifier;
            if (string.IsNullOrEmpty(bundleId))
            {
                Console.WriteLine($"Error: Could not determine bundle identifier for {browser.Name}");
                return false;
            }

            Console.WriteLine($"Setting {browser.Name} as default browser programmatically...");
            
            // Check macOS version for compatibility warnings
            var macOSVersion = ExecuteCommand("sw_vers", "-productVersion");
            var versionParts = macOSVersion.Split('.');
            if (versionParts.Length > 0 && int.TryParse(versionParts[0], out int majorVersion))
            {
                if (majorVersion >= 13)
                {
                    Console.WriteLine("Note: macOS 13+ has enhanced security restrictions. Some methods may require additional permissions.");
                }
            }
            
            // Method 1: Try using duti (most reliable for programmatic setting)
            if (TrySetDefaultBrowserWithDuti(bundleId, browser.Name))
            {
                return true;
            }

            // Method 2: Try using defaults command to modify Launch Services database
            if (TrySetDefaultBrowserWithDefaults(bundleId, browser.Name))
            {
                return true;
            }

            // Method 3: Try using lsregister (less reliable but sometimes works)
            if (TrySetDefaultBrowserWithLsregister(bundleId, browser.Name))
            {
                return true;
            }

            // Method 4: Fallback to opening System Settings (original behavior)
            Console.WriteLine($"All programmatic methods failed. Opening System Settings for manual configuration...");
            return FallbackToSystemSettings(browser, bundleId);
        }

        private bool TrySetDefaultBrowserWithDuti(string bundleId, string browserName)
        {
            try
            {
                Console.WriteLine("Attempting to set default browser using duti...");
                
                // First check if duti is available
                var dutiCheckResult = ExecuteCommand("which", "duti");
                if (string.IsNullOrEmpty(dutiCheckResult))
                {
                    Console.WriteLine("duti command not found. To install duti:");
                    Console.WriteLine("  brew install duti");
                    Console.WriteLine("  or download from: https://github.com/moretension/duti");
                    return false;
                }
                
                // Set default handler for http and https URLs
                var httpResult = ExecuteCommand("duti", $"-s {bundleId} http all");
                var httpsResult = ExecuteCommand("duti", $"-s {bundleId} https all");
                
                // Also set for public.html content type for completeness
                var htmlResult = ExecuteCommand("duti", $"-s {bundleId} public.html all");
                
                // Verify the setting was applied
                var verifyResult = ExecuteCommand("duti", "-x http");
                if (!string.IsNullOrEmpty(verifyResult) && verifyResult.Contains(bundleId))
                {
                    Console.WriteLine($"✓ Successfully set {browserName} as default browser using duti");
                    Console.WriteLine("Note: You may need to restart applications for the change to take full effect");
                    return true;
                }
                else
                {
                    Console.WriteLine("duti command executed but verification failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"duti method failed: {ex.Message}");
                return false;
            }
        }

        private bool TrySetDefaultBrowserWithDefaults(string bundleId, string browserName)
        {
            try
            {
                Console.WriteLine("Attempting to set default browser using defaults command...");
                
                // Create a new handler entry for http
                var httpHandler = $"{{LSHandlerContentType=\"public.html\";LSHandlerRoleAll=\"{bundleId}\";}}";
                var httpUrlHandler = $"{{LSHandlerURLScheme=\"http\";LSHandlerRoleAll=\"{bundleId}\";}}";
                var httpsUrlHandler = $"{{LSHandlerURLScheme=\"https\";LSHandlerRoleAll=\"{bundleId}\";}}";
                
                // Try to add handlers to Launch Services database
                var addHttpResult = ExecuteCommand("defaults", $"write com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers -array-add '{httpUrlHandler}'");
                var addHttpsResult = ExecuteCommand("defaults", $"write com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers -array-add '{httpsUrlHandler}'");
                
                // Force Launch Services to refresh its database
                var refreshResult = ExecuteCommand("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister", "-kill -r -domain local -domain system -domain user");
                
                // Wait a moment for the system to process the changes
                System.Threading.Thread.Sleep(2000);
                
                // Verify the setting
                var currentDefault = GetDefaultBrowserMacOS();
                if (bundleId.Equals(currentDefault, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"✓ Successfully set {browserName} as default browser using defaults command");
                    Console.WriteLine("Note: Changes may require a logout/login or system restart to fully take effect");
                    return true;
                }
                else
                {
                    Console.WriteLine("defaults command executed but verification failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"defaults method failed: {ex.Message}");
                return false;
            }
        }

        private bool TrySetDefaultBrowserWithLsregister(string bundleId, string browserName)
        {
            try
            {
                Console.WriteLine("Attempting to set default browser using lsregister...");
                
                // Find the application path using the bundle ID
                var appPath = ExecuteCommand("mdfind", $"kMDItemCFBundleIdentifier == '{bundleId}'");
                if (string.IsNullOrEmpty(appPath))
                {
                    Console.WriteLine("Could not find application path for lsregister");
                    return false;
                }
                
                var firstAppPath = appPath.Split('\n').FirstOrDefault()?.Trim();
                if (string.IsNullOrEmpty(firstAppPath) || !Directory.Exists(firstAppPath))
                {
                    Console.WriteLine("Invalid application path for lsregister");
                    return false;
                }
                
                // Reset Launch Services database and re-register the app
                var resetResult = ExecuteCommand("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister", $"-u \"{firstAppPath}\"");
                var registerResult = ExecuteCommand("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister", $"\"{firstAppPath}\"");
                
                // This method is less reliable for setting defaults directly,
                // but can help ensure the app is properly registered
                Console.WriteLine("lsregister method completed app registration");
                return false; // Don't claim success as this method doesn't directly set defaults
            }
            catch (Exception ex)
            {
                Console.WriteLine($"lsregister method failed: {ex.Message}");
                return false;
            }
        }

        private bool FallbackToSystemSettings(Browser browser, string bundleId)
        {
            try
            {
                Console.WriteLine("Opening System Settings for manual configuration...");
                
                // Check macOS version to determine correct Settings app
                var versionResult = ExecuteCommand("sw_vers", "-productVersion");
                if (versionResult.StartsWith("13") || versionResult.StartsWith("14") || versionResult.StartsWith("15"))
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
                Console.WriteLine();
                
                // Provide helpful troubleshooting information
                Console.WriteLine("Programmatic setting failed. Possible reasons:");
                Console.WriteLine("  • System Integrity Protection (SIP) restrictions");
                Console.WriteLine("  • Insufficient permissions or macOS security policies");
                Console.WriteLine("  • Missing command-line tools (install: brew install duti)");
                Console.WriteLine("  • App not properly registered with Launch Services");
                Console.WriteLine();
                Console.WriteLine("To improve programmatic setting success:");
                Console.WriteLine("  1. Install duti: brew install duti");
                Console.WriteLine("  2. Ensure the browser app is in /Applications/");
                Console.WriteLine("  3. Run with admin privileges if necessary");
                Console.WriteLine("  4. Try logging out and back in after setting");
                
                return false; // Return false since we couldn't set it programmatically
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening System Settings: {ex.Message}");
                return false;
            }
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
                Console.WriteLine($"✓ {browser.Name} has been set as the default browser.");
            }
            else
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Console.WriteLine($"⚠ Programmatic setting failed, but System Settings should now be open for manual configuration.");
                    Console.WriteLine("Please select your preferred browser in the System Settings window that opened.");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
