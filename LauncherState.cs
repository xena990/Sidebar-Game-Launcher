using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SidebarGameLauncher
{
    internal struct WindowStateData
    {
        public readonly Rectangle WindowBounds;
        public readonly string LastFolder;
        public readonly bool TopMost;
        public readonly Color BorderColor;
        public readonly int IconSize;
        public readonly bool HideIconLabels;
        public readonly string VisualStyle;

        public WindowStateData(Rectangle bounds, string lastFolder, bool topMost, Color borderColor, int iconSize, bool hideIconLabels, string visualStyle)
        {
            WindowBounds = bounds;
            LastFolder = lastFolder;
            TopMost = topMost;
            BorderColor = borderColor;
            IconSize = iconSize;
            HideIconLabels = hideIconLabels;
            VisualStyle = visualStyle;
        }
    }

    internal sealed class FolderSettings
    {
        private readonly string settingsPath;

        public FolderSettings()
        {
            string settingsDir = PortablePaths.GetDataDirectory();
            settingsPath = Path.Combine(settingsDir, "settings.ini");
        }

        public WindowStateData LoadState()
        {
            Rectangle defaultBounds = GetDefaultBounds();
            string lastFolder = string.Empty;
            bool topMost = true;
            Color borderColor = Color.FromArgb(124, 167, 204);
            int left = defaultBounds.Left;
            int top = defaultBounds.Top;
            int width = defaultBounds.Width;
            int height = defaultBounds.Height;
            int borderR = borderColor.R;
            int borderG = borderColor.G;
            int borderB = borderColor.B;
            int iconSize = 32;
            bool hideIconLabels = false;
            string visualStyle = "blue";

            try
            {
                if (!File.Exists(settingsPath))
                {
                    return new WindowStateData(defaultBounds, lastFolder, topMost, borderColor, iconSize, hideIconLabels, visualStyle);
                }

                string[] lines = File.ReadAllLines(settingsPath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("LastFolder=", StringComparison.OrdinalIgnoreCase))
                    {
                        lastFolder = line.Substring("LastFolder=".Length).Trim();
                    }
                    else if (line.StartsWith("Left=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("Left=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out left);
                    }
                    else if (line.StartsWith("Top=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("Top=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out top);
                    }
                    else if (line.StartsWith("Width=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("Width=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
                    }
                    else if (line.StartsWith("Height=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("Height=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
                    }
                    else if (line.StartsWith("TopMost=", StringComparison.OrdinalIgnoreCase))
                    {
                        topMost = line.EndsWith("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (line.StartsWith("BorderR=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("BorderR=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out borderR);
                    }
                    else if (line.StartsWith("BorderG=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("BorderG=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out borderG);
                    }
                    else if (line.StartsWith("BorderB=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("BorderB=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out borderB);
                    }
                    else if (line.StartsWith("IconSize=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("IconSize=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out iconSize);
                    }
                    else if (line.StartsWith("HideIconLabels=", StringComparison.OrdinalIgnoreCase))
                    {
                        hideIconLabels = line.EndsWith("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (line.StartsWith("VisualStyle=", StringComparison.OrdinalIgnoreCase))
                    {
                        visualStyle = line.Substring("VisualStyle=".Length).Trim();
                    }
                }
            }
            catch
            {
                return new WindowStateData(defaultBounds, string.Empty, true, borderColor, 32, false, "blue");
            }

            if (width < 180)
            {
                width = 180;
            }

            if (height < 280)
            {
                height = 280;
            }

            if (iconSize < 24)
            {
                iconSize = 24;
            }

            if (iconSize > 96)
            {
                iconSize = 96;
            }

            borderColor = Color.FromArgb(ClampByte(borderR), ClampByte(borderG), ClampByte(borderB));
            Rectangle bounds = ClampToScreen(new Rectangle(left, top, width, height));
            if (string.Equals(visualStyle, "classic", StringComparison.OrdinalIgnoreCase))
            {
                visualStyle = "green";
            }
            else if (!string.Equals(visualStyle, "green", StringComparison.OrdinalIgnoreCase))
            {
                visualStyle = "blue";
            }

            return new WindowStateData(bounds, lastFolder, topMost, borderColor, iconSize, hideIconLabels, visualStyle);
        }

        public void SaveState(WindowStateData state)
        {
            try
            {
                string dir = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string[] lines = new[]
                {
                    "LastFolder=" + (state.LastFolder ?? string.Empty),
                    "Left=" + state.WindowBounds.Left.ToString(CultureInfo.InvariantCulture),
                    "Top=" + state.WindowBounds.Top.ToString(CultureInfo.InvariantCulture),
                    "Width=" + state.WindowBounds.Width.ToString(CultureInfo.InvariantCulture),
                    "Height=" + state.WindowBounds.Height.ToString(CultureInfo.InvariantCulture),
                    "TopMost=" + state.TopMost.ToString().ToLowerInvariant(),
                    "BorderR=" + state.BorderColor.R.ToString(CultureInfo.InvariantCulture),
                    "BorderG=" + state.BorderColor.G.ToString(CultureInfo.InvariantCulture),
                    "BorderB=" + state.BorderColor.B.ToString(CultureInfo.InvariantCulture),
                    "IconSize=" + state.IconSize.ToString(CultureInfo.InvariantCulture),
                    "HideIconLabels=" + state.HideIconLabels.ToString().ToLowerInvariant(),
                    "VisualStyle=" + (state.VisualStyle ?? "blue")
                };

                File.WriteAllLines(settingsPath, lines);
            }
            catch
            {
            }
        }

        private static int ClampByte(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }

        private static Rectangle GetDefaultBounds()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int width = 250;
            int height = Math.Max(360, area.Height - 80);
            int left = Math.Max(area.Left, area.Right - width - 12);
            int top = Math.Max(area.Top, area.Top + 40);
            return new Rectangle(left, top, width, height);
        }

        private static Rectangle ClampToScreen(Rectangle bounds)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;

            if (bounds.Width > area.Width)
            {
                bounds.Width = area.Width;
            }

            if (bounds.Height > area.Height)
            {
                bounds.Height = area.Height;
            }

            if (bounds.Left < area.Left)
            {
                bounds.X = area.Left;
            }

            if (bounds.Top < area.Top)
            {
                bounds.Y = area.Top;
            }

            if (bounds.Right > area.Right)
            {
                bounds.X = area.Right - bounds.Width;
            }

            if (bounds.Bottom > area.Bottom)
            {
                bounds.Y = area.Bottom - bounds.Height;
            }

            return bounds;
        }
    }

    internal static class PortablePaths
    {
        public static string GetDataDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = Path.Combine(baseDir, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            return dataDir;
        }
    }

    internal static class StartupRegistration
    {
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunValueName = "SidebarGameLauncher";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null)
                {
                    return false;
                }

                object value = key.GetValue(RunValueName);
                if (value == null)
                {
                    return false;
                }

                string configured = value.ToString().Trim();
                string expected = QuotePath(Application.ExecutablePath);
                return string.Equals(configured, expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Could not open startup registry key.");
                }

                if (enabled)
                {
                    key.SetValue(RunValueName, QuotePath(Application.ExecutablePath), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }

        private static string QuotePath(string path)
        {
            return "\"" + path + "\"";
        }
    }
}

