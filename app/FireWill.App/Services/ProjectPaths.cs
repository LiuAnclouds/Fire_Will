namespace FireWill.App.Services;

public static class ProjectPaths
{
    public static string FindProjectRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "war3_macro_gui.ahk")) &&
                Directory.Exists(Path.Combine(dir.FullName, "profiles")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    public static string FindUiIndex(string projectRoot)
    {
        string copied = Path.Combine(AppContext.BaseDirectory, "ui", "index.html");
        if (File.Exists(copied))
        {
            return copied;
        }

        return Path.Combine(projectRoot, "ui", "index.html");
    }

    public static Icon? TryLoadIcon(string projectRoot)
    {
        string iconPath = Path.Combine(projectRoot, "ui", "assets", "icon.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(projectRoot, "icon.ico");
        }

        try
        {
            return File.Exists(iconPath) ? new Icon(iconPath) : null;
        }
        catch
        {
            return null;
        }
    }
}

