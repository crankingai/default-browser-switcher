# default-browser-switcher

A cross-platform .NET console application called `brodef` (short for "browser default") that allows you to easily view and switch your system's default web browser.

## Features

- **Cross-platform support**: Works on Windows, macOS, and Linux
- **Interactive mode**: Run without arguments to see a numbered list of installed browsers
- **Direct mode**: Pass a number to immediately set a browser as default
- **Smart detection**: Automatically detects installed browsers and shows which is currently default
- **Programmatic setting**: Automatically sets default browser on macOS without manual interaction (NEW!)
- **Multiple fallback methods**: Ensures compatibility even with system restrictions

## Usage

### Interactive Mode
```bash
brodef
```

This displays a numbered list of installed browsers and indicates which is the default:
```
Installed browsers:
1. Chrome
2. Edge  
3. Firefox (default)
4. Safari

Press ENTER to exit, or enter a number to set as default:
```

- Press **ENTER** to exit without making changes
- Enter a **number** (e.g., `2`) to set that browser as default

### Direct Mode
```bash
brodef 3
```

This immediately sets browser #3 from the list as the default (e.g., Firefox in the example above).

## Installation

### Prerequisites
- .NET 8.0 SDK or runtime

### Building from source
```bash
git clone https://github.com/crankingai/default-browser-switcher.git
cd default-browser-switcher
dotnet build
```

### Running
```bash
dotnet run
# or
dotnet run 2
```

### Publishing a standalone executable
```bash
dotnet publish -c Release --self-contained true -r linux-x64
# Replace linux-x64 with win-x64 (Windows) or osx-x64 (macOS) as needed
```

## Supported Browsers

The application automatically detects these browsers when installed:

### Windows
- Google Chrome
- Microsoft Edge
- Mozilla Firefox
- Opera
- Brave Browser

### macOS  
The application uses advanced filtering logic to precisely match the browser list shown in **System Settings > General > Default web browser**. The enhanced detection system ensures perfect alignment with Apple's own browser eligibility criteria:

**Advanced Browser Detection:**
- **Info.plist validation**: Verifies proper CFBundleURLTypes declarations for HTTP/HTTPS schemes
- **Launch Services verification**: Uses `lsregister`, `duti`, and Launch Services database queries
- **Application filtering**: Excludes non-browser apps (email clients, IDEs, social apps) that handle HTTP URLs
- **Multi-layer validation**: Combines whitelist, blacklist, and system-level verification

**Supported Browser Types:**
- Major browsers (Safari, Chrome, Firefox, Edge, Opera, Brave, Vivaldi)
- Developer browsers (WebKit Nightly, Safari Technology Preview)  
- Modern browsers (Arc, SigmaOS, Chromium variants)
- Browser variants and renamed installations
- Apps installed anywhere on the system

**Filtering Criteria:**
The implementation follows Apple's exact filtering logic:
1. Must properly declare HTTP/HTTPS URL schemes in Info.plist
2. Must be registered with Launch Services as a legitimate web browser
3. Must not be a non-browser application (email, development, social media apps)

**No additional dependencies or entitlements are required** - uses only standard macOS system tools.

### Linux
- Google Chrome
- Mozilla Firefox
- Microsoft Edge
- Opera
- Brave Browser
- Chromium

## Platform-Specific Notes

### Linux
The application uses `xdg-settings` to get and set the default browser. Make sure you have the `xdg-utils` package installed:
```bash
# Ubuntu/Debian
sudo apt-get install xdg-utils

# Fedora/RHEL
sudo dnf install xdg-utils
```

### Windows
Setting the default browser on Windows requires administrative privileges and user interaction through the system settings. The application will provide instructions for manual setup.

### macOS
The application now **automatically sets the default browser programmatically** without requiring manual interaction with System Settings. Multiple methods are attempted for maximum compatibility:

**Programmatic Setting Methods:**
1. **Primary**: `duti` command-line tool (most reliable)
2. **Secondary**: Direct Launch Services database modification using `defaults`
3. **Tertiary**: Launch Services registration using `lsregister`
4. **Fallback**: System Settings (if all programmatic methods fail)

**Installation Requirements:**
For best results, install `duti` using Homebrew:
```bash
brew install duti
```
If `duti` is not available, the application will attempt alternative methods and fall back to opening System Settings if needed.

**Programmatic Features:**
- ✅ **Fully automated**: No user interaction required in most cases
- ✅ **Multiple fallback methods**: Ensures compatibility across macOS versions
- ✅ **Verification**: Confirms successful setting and provides feedback
- ✅ **Error handling**: Clear messaging about system restrictions or missing tools
- ✅ **Compatibility**: Works with System Integrity Protection (SIP) and modern macOS security

**Enhanced Browser Detection:**
- **Multi-layer filtering**: Info.plist validation + Launch Services verification + application type filtering
- **System alignment**: Matches exactly what appears in System Settings > General > Default web browser  
- **Comprehensive coverage**: Detects browsers installed anywhere on the system, including variants and renamed installations
- **Smart exclusion**: Filters out email clients, IDEs, and other non-browser apps that handle HTTP URLs

**Technical Implementation:**
- Uses `mdfind` and `mdls` to query the Launch Services database
- Validates CFBundleURLTypes in application Info.plist files
- Cross-references with Launch Services handlers using `lsregister`, `duti`, and `defaults`
- Applies whitelist/blacklist filtering based on bundle identifiers and app names
- Falls back gracefully if system queries fail

**Supported Detection Methods:**
1. **Primary**: Known legitimate web browser identification
2. **Secondary**: Info.plist CFBundleURLTypes validation  
3. **Tertiary**: Launch Services database verification
4. **Fallback**: Conservative filtering for unknown applications

**Default Browser Detection:**
- Uses the same Launch Services data as System Settings
- Multiple fallback methods ensure reliability
- Returns exact bundle identifiers for accurate matching

**No additional setup required** - all functionality uses standard macOS system tools.

## License

MIT License - see [LICENSE](LICENSE) file for details.
