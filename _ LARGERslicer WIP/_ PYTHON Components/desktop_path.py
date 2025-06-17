import os
from pathlib import Path

# Grasshopper Python Component:
# This script finds the current user's Desktop folder path cross-platform
# and outputs it as the variable 'desktopPath'.

def get_desktop_path():
    """Get the desktop path for the current user, works on Windows and macOS."""
    
    # Windows-specific method
    if os.name == 'nt':
        try:
            # Try using the Windows shell API first
            import ctypes
            from ctypes import windll, wintypes
            CSIDL_DESKTOP = 0
            buf = ctypes.create_unicode_buffer(wintypes.MAX_PATH)
            windll.shell32.SHGetFolderPathW(0, CSIDL_DESKTOP, 0, 0, buf)
            if os.path.exists(buf.value):
                return buf.value
        except:
            try:
                # Fallback to registry
                import winreg
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, 
                                  r"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders") as key:
                    desktop_path = winreg.QueryValueEx(key, "Desktop")[0]
                    if os.path.exists(desktop_path):
                        return desktop_path
            except:
                pass

    # macOS method
    if os.name == 'posix':
        try:
            # Try using AppleScript to get the desktop path (most reliable on macOS)
            import subprocess
            cmd = ['osascript', '-e', 'tell application "Finder" to get POSIX path of (desktop as alias)']
            desktop_path = subprocess.check_output(cmd).decode('utf-8').strip()
            if os.path.exists(desktop_path):
                return desktop_path
        except:
            # Standard macOS desktop path
            desktop = Path.home() / "Desktop"
            if desktop.exists():
                return str(desktop)
    
    # Fallback methods
    # Try environment variables first
    possible_env_vars = ['USERPROFILE', 'HOME']
    for env_var in possible_env_vars:
        if env_var in os.environ:
            desktop = Path(os.environ[env_var]) / "Desktop"
            if desktop.exists() and desktop.is_dir():
                return str(desktop)
    
    # Try common names in different languages
    home = Path.home()
    candidates = ["Desktop", "Schreibtisch", "Escritorio", "Bureau", "デスクトップ"]
    for name in candidates:
        desk = home / name
        if desk.exists() and desk.is_dir():
            return str(desk)
    
    # Last fallback: return home directory
    return str(home)

# Output variable for Grasshopper
desktopPath = get_desktop_path()

# Print or output desktopPath to Grasshopper
print(f"Desktop path detected: {desktopPath}") 