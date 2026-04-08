using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XpSidebarLauncher
{
    internal enum ItemIconMode
    {
        LaunchBox = 0,
        FileDefault = 1
    }

    internal sealed class ItemIconPreferenceStore
    {
        private readonly string filePath;
        private readonly Dictionary<string, ItemIconMode> map;
        private readonly object sync;

        public ItemIconPreferenceStore()
        {
            filePath = Path.Combine(PortablePaths.GetDataDirectory(), "item_icon_modes.ini");
            map = new Dictionary<string, ItemIconMode>(StringComparer.OrdinalIgnoreCase);
            sync = new object();
            Load();
        }

        public ItemIconMode GetMode(string itemPath)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return ItemIconMode.LaunchBox;
            }

            string key = NormalizeKey(itemPath);
            lock (sync)
            {
                ItemIconMode mode;
                if (map.TryGetValue(key, out mode))
                {
                    return mode;
                }
            }

            return ItemIconMode.LaunchBox;
        }

        public void SetMode(string itemPath, ItemIconMode mode)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return;
            }

            string key = NormalizeKey(itemPath);
            lock (sync)
            {
                map[key] = mode;
                Save_NoThrow();
            }
        }

        private static string NormalizeKey(string itemPath)
        {
            return itemPath.Trim();
        }

        private void Load()
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    int split = line.IndexOf('=');
                    if (split <= 0 || split >= line.Length - 1)
                    {
                        continue;
                    }

                    string key = Uri.UnescapeDataString(line.Substring(0, split));
                    string value = line.Substring(split + 1).Trim();
                    ItemIconMode mode;
                    if (!TryParseMode(value, out mode))
                    {
                        continue;
                    }

                    map[key] = mode;
                }
            }
            catch
            {
            }
        }

        private void Save_NoThrow()
        {
            try
            {
                var lines = new List<string>();
                foreach (KeyValuePair<string, ItemIconMode> pair in map)
                {
                    lines.Add(Uri.EscapeDataString(pair.Key) + "=" + pair.Value.ToString());
                }

                File.WriteAllLines(filePath, lines.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static bool TryParseMode(string value, out ItemIconMode mode)
        {
            mode = ItemIconMode.LaunchBox;
            if (string.Equals(value, "FileDefault", StringComparison.OrdinalIgnoreCase))
            {
                mode = ItemIconMode.FileDefault;
                return true;
            }

            if (string.Equals(value, "LaunchBox", StringComparison.OrdinalIgnoreCase))
            {
                mode = ItemIconMode.LaunchBox;
                return true;
            }

            return false;
        }
    }
}
