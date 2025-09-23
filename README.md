# default-browser-switcher

A cross-platform .NET console application called `brodef` (short for "browser default") that allows you to easily view and switch your system's default web browser.

## Features

- **Cross-platform support**: Works on Windows, macOS, and Linux
- **Interactive mode**: Run without arguments to see a numbered list of installed browsers
- **Direct mode**: Pass a number to immediately set a browser as default
- **Smart detection**: Automatically detects installed browsers and shows which is currently default

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
The application uses official macOS Launch Services APIs to dynamically detect all installed web browsers. This ensures the browser list always matches what is displayed in **System Settings > General > Default web browser**. The detection includes:

- All browsers that can handle HTTP/HTTPS URLs (automatically detected)
- Browser variants and renamed browsers
- Newly installed browsers
- Apps installed in any location (not just /Applications)

**No additional dependencies or entitlements are required** - the implementation uses standard macOS command-line tools available on all systems.

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
The application uses official macOS Launch Services APIs to dynamically detect installed browsers and retrieve the current default browser. This ensures perfect alignment with System Settings and handles all edge cases automatically.

**Browser Detection:**
- Uses `mdfind` and `mdls` to query the Launch Services database
- Automatically detects all browsers that can handle HTTP/HTTPS URLs
- Handles browser variants, renamed browsers, and custom installations
- Falls back gracefully if system queries fail

**Default Browser Detection:**
- Uses the same Launch Services data as System Settings
- Multiple fallback methods ensure reliability
- Returns exact bundle identifiers for accurate matching

**No additional setup required** - all functionality uses standard macOS system tools.

## License

MIT License - see [LICENSE](LICENSE) file for details.
