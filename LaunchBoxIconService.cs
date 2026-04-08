using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SidebarGameLauncher
{
    internal sealed class LaunchBoxIconService
    {
        private const string IndexVersionHeader = "#v8";
        private const string SearchApiBase = "https://api.gamesdb.launchbox-app.com/api/search/%20?title=";
        private const string SearchApiSuffix = "&platform=Windows";
        private const string DetailsApiBase = "https://api.gamesdb.launchbox-app.com/api/games/details/";
        private const string ImageBase = "https://images.launchbox-app.com/";

        private readonly string indexFilePath;
        private readonly string caseIndexFilePath;
        private readonly string imageCacheDir;
        private readonly Dictionary<string, string> iconIndex;
        private readonly Dictionary<string, string> caseIndex;
        private readonly object sync;
        private readonly JavaScriptSerializer serializer;

        public LaunchBoxIconService()
        {
            string dataDir = PortablePaths.GetDataDirectory();
            string launchBoxDir = Path.Combine(dataDir, "LaunchBoxCache");
            imageCacheDir = Path.Combine(launchBoxDir, "Images");
            indexFilePath = Path.Combine(launchBoxDir, "index.ini");
            caseIndexFilePath = Path.Combine(launchBoxDir, "case_index.ini");
            iconIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            caseIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sync = new object();
            serializer = new JavaScriptSerializer();

            Directory.CreateDirectory(imageCacheDir);
            LoadIndex();
            LoadCaseIndex();
        }

        public bool TryGetCaseArtPaths(string fileName, out string spineImagePath, out string frontImagePath)
        {
            spineImagePath = string.Empty;
            frontImagePath = string.Empty;
            string primaryKey = NormalizeTitle(fileName);
            if (string.IsNullOrEmpty(primaryKey))
            {
                return false;
            }

            if (TryGetCachedCase(primaryKey, out spineImagePath, out frontImagePath))
            {
                return true;
            }

            List<string> queries = BuildSearchQueries(fileName);
            if (queries.Count == 0)
            {
                return false;
            }

            int gameKey;
            string bestName;
            string matchedQuery;
            if (!TryFindBestGame(queries, out gameKey, out bestName, out matchedQuery))
            {
                return false;
            }

            string spineFileName;
            string frontFileName;
            if (!TryGetPreferredCaseImageFileNames(gameKey, out spineFileName, out frontFileName))
            {
                return false;
            }

            if (!TryDownloadImage(gameKey, spineFileName, out spineImagePath))
            {
                return false;
            }

            if (!TryDownloadImage(gameKey, frontFileName, out frontImagePath))
            {
                frontImagePath = spineImagePath;
            }

            lock (sync)
            {
                caseIndex[primaryKey] = spineImagePath + "|" + frontImagePath;
                SaveCaseIndex_NoThrow();
            }

            return true;
        }

        public bool TryGetIconPath(string fileName, out string localImagePath)
        {
            localImagePath = string.Empty;
            string primaryKey = NormalizeTitle(fileName);
            if (string.IsNullOrEmpty(primaryKey))
            {
                return false;
            }

            if (TryGetCached(primaryKey, out localImagePath))
            {
                return true;
            }

            List<string> queries = BuildSearchQueries(fileName);
            if (queries.Count == 0)
            {
                return false;
            }

            int gameKey;
            string bestName;
            string matchedQuery;
            if (!TryFindBestGame(queries, out gameKey, out bestName, out matchedQuery))
            {
                return false;
            }

            string imageFileName;
            if (!TryGetPreferredImageFileName(gameKey, out imageFileName))
            {
                return false;
            }

            if (!TryDownloadImage(gameKey, imageFileName, out localImagePath))
            {
                return false;
            }

            lock (sync)
            {
                iconIndex[primaryKey] = localImagePath;

                if (!string.IsNullOrEmpty(bestName))
                {
                    string bestNameNormalized = NormalizeTitle(bestName);
                    if (!string.IsNullOrEmpty(bestNameNormalized) && string.Equals(bestNameNormalized, primaryKey, StringComparison.OrdinalIgnoreCase))
                    {
                        iconIndex[bestNameNormalized] = localImagePath;
                    }
                }

                if (!string.IsNullOrEmpty(matchedQuery) && string.Equals(matchedQuery, primaryKey, StringComparison.OrdinalIgnoreCase))
                {
                    iconIndex[matchedQuery] = localImagePath;
                }

                SaveIndex_NoThrow();
            }

            return true;
        }

        private bool TryFindBestGame(List<string> queries, out int gameKey, out string bestName, out string matchedQuery)
        {
            gameKey = 0;
            bestName = string.Empty;
            matchedQuery = string.Empty;
            int bestScore = int.MinValue;

            for (int i = 0; i < queries.Count; i++)
            {
                string query = queries[i];
                int currentKey;
                string currentName;
                int currentScore;
                if (TryFindGame(query, out currentKey, out currentName, out currentScore))
                {
                    if (currentScore > bestScore)
                    {
                        bestScore = currentScore;
                        gameKey = currentKey;
                        bestName = currentName;
                        matchedQuery = query;
                    }
                }
            }

            return gameKey > 0;
        }

        private bool TryGetCached(string normalizedTitle, out string localImagePath)
        {
            localImagePath = string.Empty;

            lock (sync)
            {
                string cachedPath;
                if (!iconIndex.TryGetValue(normalizedTitle, out cachedPath))
                {
                    return false;
                }

                if (File.Exists(cachedPath))
                {
                    localImagePath = cachedPath;
                    return true;
                }

                iconIndex.Remove(normalizedTitle);
                SaveIndex_NoThrow();
            }

            return false;
        }

        private bool TryGetCachedCase(string normalizedTitle, out string spineImagePath, out string frontImagePath)
        {
            spineImagePath = string.Empty;
            frontImagePath = string.Empty;

            lock (sync)
            {
                string packed;
                if (!caseIndex.TryGetValue(normalizedTitle, out packed))
                {
                    return false;
                }

                string[] parts = packed.Split(new[] { '|' }, 2);
                if (parts.Length == 0)
                {
                    return false;
                }

                string spine = parts[0];
                string front = parts.Length > 1 ? parts[1] : string.Empty;
                if (string.IsNullOrEmpty(spine) || !File.Exists(spine))
                {
                    caseIndex.Remove(normalizedTitle);
                    SaveCaseIndex_NoThrow();
                    return false;
                }

                if (string.IsNullOrEmpty(front) || !File.Exists(front))
                {
                    front = spine;
                }

                spineImagePath = spine;
                frontImagePath = front;
                return true;
            }
        }

        private bool TryFindGame(string normalizedTitle, out int gameKey, out string bestName, out int bestScore)
        {
            gameKey = 0;
            bestName = string.Empty;
            bestScore = int.MinValue;
            string wantedForScore = NormalizeTitle(normalizedTitle);
            if (string.IsNullOrEmpty(wantedForScore))
            {
                wantedForScore = normalizedTitle;
            }
            wantedForScore = NormalizeAliasTokens(wantedForScore);
            bool gtaQuery = IsGtaQuery(wantedForScore);
            string wantedNfsEpisode;
            bool hasWantedNfsEpisode = TryGetNfsEpisodeToken(wantedForScore, out wantedNfsEpisode);

            string url = SearchApiBase + Uri.EscapeDataString(normalizedTitle) + SearchApiSuffix;
            string json = DownloadString(url);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            object parsed = serializer.DeserializeObject(json);
            var root = parsed as IDictionary<string, object>;
            if (root == null)
            {
                return false;
            }

            object dataObj;
            if (!root.TryGetValue("data", out dataObj))
            {
                return false;
            }

            var dataList = dataObj as object[];
            if (dataList == null || dataList.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < dataList.Length; i++)
            {
                var entry = dataList[i] as IDictionary<string, object>;
                if (entry == null)
                {
                    continue;
                }

                int currentKey;
                if (!TryToInt(GetObject(entry, "gameKey"), out currentKey))
                {
                    continue;
                }

                string candidateName = GetString(entry, "name");
                if (string.IsNullOrEmpty(candidateName))
                {
                    continue;
                }
                string normalizedCandidate = NormalizeAliasTokens(NormalizeTitle(candidateName));
                if (gtaQuery && !normalizedCandidate.StartsWith("grand theft auto", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (hasWantedNfsEpisode)
                {
                    string candidateNfsEpisode;
                    if (!TryGetNfsEpisodeToken(normalizedCandidate, out candidateNfsEpisode) ||
                        !string.Equals(wantedNfsEpisode, candidateNfsEpisode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                string candidatePlatform = GetString(entry, "platformName");
                if (!IsWindowsPlatform(candidatePlatform))
                {
                    continue;
                }

                int score = ScoreMatch(wantedForScore, normalizedCandidate, candidatePlatform);
                if (score > bestScore)
                {
                    bestScore = score;
                    gameKey = currentKey;
                    bestName = candidateName;
                }
            }

            return gameKey > 0 && bestScore >= 80;
        }

        private static bool IsWindowsPlatform(string platformName)
        {
            if (string.IsNullOrEmpty(platformName))
            {
                return false;
            }

            return platformName.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryGetPreferredImageFileName(int gameKey, out string imageFileName)
        {
            imageFileName = string.Empty;
            string url = DetailsApiBase + gameKey.ToString(CultureInfo.InvariantCulture);
            string json = DownloadString(url);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            object parsed = serializer.DeserializeObject(json);
            var root = parsed as IDictionary<string, object>;
            if (root == null)
            {
                return false;
            }

            object imagesObj;
            if (!root.TryGetValue("gameImages", out imagesObj))
            {
                return false;
            }

            var imageList = imagesObj as object[];
            if (imageList == null || imageList.Length == 0)
            {
                return false;
            }

            string preferredByIcon = FindBestImageByTypePriority(imageList, true, false);
            if (!string.IsNullOrEmpty(preferredByIcon))
            {
                imageFileName = preferredByIcon;
                return true;
            }

            string preferredBySquare = FindBestImageByTypePriority(imageList, false, false, true);
            if (!string.IsNullOrEmpty(preferredBySquare))
            {
                imageFileName = preferredBySquare;
                return true;
            }

            string preferredByClearLogo = FindBestImageByTypePriority(imageList, false, true);
            if (!string.IsNullOrEmpty(preferredByClearLogo))
            {
                imageFileName = preferredByClearLogo;
                return true;
            }

            string bestFile = string.Empty;
            int bestScore = int.MinValue;

            for (int i = 0; i < imageList.Length; i++)
            {
                var image = imageList[i] as IDictionary<string, object>;
                if (image == null)
                {
                    continue;
                }

                string selectedFile = GetString(image, "fullGameImageFileName");
                if (string.IsNullOrEmpty(selectedFile))
                {
                    selectedFile = GetString(image, "imageFileName");
                }

                if (string.IsNullOrEmpty(selectedFile))
                {
                    continue;
                }

                int width;
                int height;
                GetImageSize(image, out width, out height);

                string typeName = GetString(image, "imageTypeName");
                int score = ScoreImageCandidate(width, height, typeName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFile = selectedFile;
                }
            }

            if (string.IsNullOrEmpty(bestFile))
            {
                return false;
            }

            imageFileName = bestFile;
            return true;
        }

        private bool TryGetPreferredCaseImageFileNames(int gameKey, out string spineFileName, out string frontFileName)
        {
            spineFileName = string.Empty;
            frontFileName = string.Empty;
            string url = DetailsApiBase + gameKey.ToString(CultureInfo.InvariantCulture);
            string json = DownloadString(url);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            object parsed = serializer.DeserializeObject(json);
            var root = parsed as IDictionary<string, object>;
            if (root == null)
            {
                return false;
            }

            object imagesObj;
            if (!root.TryGetValue("gameImages", out imagesObj))
            {
                return false;
            }

            var imageList = imagesObj as object[];
            if (imageList == null || imageList.Length == 0)
            {
                return false;
            }

            spineFileName = FindBestMatchByTypeName(imageList, new[] { "Box - Spine", "Box - Spine Thumb", "Icon", "Square", "Box - Front" });
            frontFileName = FindBestMatchByTypeName(imageList, new[] { "Box - Front", "Box - Front Thumb", "Box - Front - Reconstructed", "Square", "Icon", "Clear Logo" });

            if (string.IsNullOrEmpty(spineFileName) && string.IsNullOrEmpty(frontFileName))
            {
                return false;
            }

            if (string.IsNullOrEmpty(spineFileName))
            {
                spineFileName = frontFileName;
            }

            if (string.IsNullOrEmpty(frontFileName))
            {
                frontFileName = spineFileName;
            }

            return !string.IsNullOrEmpty(spineFileName) && !string.IsNullOrEmpty(frontFileName);
        }

        private static string FindBestMatchByTypeName(object[] imageList, string[] preferredTypeOrder)
        {
            for (int p = 0; p < preferredTypeOrder.Length; p++)
            {
                string preferred = preferredTypeOrder[p];
                string bestFile = string.Empty;
                int bestScore = int.MinValue;

                for (int i = 0; i < imageList.Length; i++)
                {
                    var image = imageList[i] as IDictionary<string, object>;
                    if (image == null)
                    {
                        continue;
                    }

                    string typeName = GetString(image, "imageTypeName");
                    if (typeName.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string fileName = GetString(image, "fullGameImageFileName");
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = GetString(image, "imageFileName");
                    }

                    if (string.IsNullOrEmpty(fileName))
                    {
                        continue;
                    }

                    int width;
                    int height;
                    GetImageSize(image, out width, out height);
                    int score = ScoreImageCandidate(width, height, typeName);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestFile = fileName;
                    }
                }

                if (!string.IsNullOrEmpty(bestFile))
                {
                    return bestFile;
                }
            }

            return string.Empty;
        }

        private static string FindBestImageByTypePriority(object[] imageList, bool onlyIconTypes, bool onlyClearLogoTypes)
        {
            return FindBestImageByTypePriority(imageList, onlyIconTypes, onlyClearLogoTypes, false);
        }

        private static string FindBestImageByTypePriority(object[] imageList, bool onlyIconTypes, bool onlyClearLogoTypes, bool onlySquareTypes)
        {
            string bestFile = string.Empty;
            int bestScore = int.MinValue;

            for (int i = 0; i < imageList.Length; i++)
            {
                var image = imageList[i] as IDictionary<string, object>;
                if (image == null)
                {
                    continue;
                }

                string typeName = GetString(image, "imageTypeName");
                bool isIconType = typeName.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isClearLogoType = typeName.IndexOf("Clear Logo", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSquareType = typeName.IndexOf("Square", StringComparison.OrdinalIgnoreCase) >= 0;
                if (onlyIconTypes && !isIconType)
                {
                    continue;
                }

                if (onlyClearLogoTypes && !isClearLogoType)
                {
                    continue;
                }

                if (onlySquareTypes && !isSquareType)
                {
                    continue;
                }

                string selectedFile = GetString(image, "fullGameImageFileName");
                if (string.IsNullOrEmpty(selectedFile))
                {
                    selectedFile = GetString(image, "imageFileName");
                }

                if (string.IsNullOrEmpty(selectedFile))
                {
                    continue;
                }

                int width;
                int height;
                GetImageSize(image, out width, out height);
                int score = ScoreImageCandidate(width, height, typeName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFile = selectedFile;
                }
            }

            return bestFile;
        }

        private bool TryDownloadImage(int gameKey, string imageFileName, out string localImagePath)
        {
            localImagePath = string.Empty;
            if (string.IsNullOrEmpty(imageFileName))
            {
                return false;
            }

            string extension = Path.GetExtension(imageFileName);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".jpg";
            }

            string safeName = MakeSafeFileName(gameKey.ToString(CultureInfo.InvariantCulture) + "_" + Path.GetFileNameWithoutExtension(imageFileName) + extension);
            string fullPath = Path.Combine(imageCacheDir, safeName);
            if (File.Exists(fullPath))
            {
                localImagePath = fullPath;
                return true;
            }

            byte[] data = DownloadBytes(ImageBase + imageFileName);
            if (data == null || data.Length == 0)
            {
                return false;
            }

            File.WriteAllBytes(fullPath, data);
            localImagePath = fullPath;
            return true;
        }

        private static int ScoreImageCandidate(int width, int height, string typeName)
        {
            int score = 0;

            if (width > 0 && height > 0)
            {
                if (width == 256 && height == 256)
                {
                    score += 10000;
                }
                else
                {
                    if (width == 256 || height == 256)
                    {
                        score += 2500;
                    }

                    int distance = Math.Abs(width - 256) + Math.Abs(height - 256);
                    score += Math.Max(0, 1800 - (distance * 3));
                }

                if (Math.Abs(width - height) <= 12)
                {
                    score += 350;
                }
            }

            if (!string.IsNullOrEmpty(typeName))
            {
                if (typeName.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 4200;
                    if (width > 0 && height > 0 && width <= 64 && height <= 64)
                    {
                        score += 2200;
                    }
                }
                else if (string.Equals(typeName, "Box - Front", StringComparison.OrdinalIgnoreCase))
                {
                    score += 260;
                }
                else if (string.Equals(typeName, "Box - Front Thumb", StringComparison.OrdinalIgnoreCase))
                {
                    score += 220;
                }
                else if (string.Equals(typeName, "Clear Logo", StringComparison.OrdinalIgnoreCase))
                {
                    score += 140;
                }
                else if (typeName.IndexOf("Disc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         typeName.IndexOf("CD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         typeName.IndexOf("DVD", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score -= 3200;
                }
                else if (typeName.IndexOf("Thumb", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score -= 50;
                }
            }

            return score;
        }

        private static void GetImageSize(IDictionary<string, object> image, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!TryToInt(GetObject(image, "fullGameImageWidth"), out width))
            {
                TryToInt(GetObject(image, "width"), out width);
            }

            if (!TryToInt(GetObject(image, "fullGameImageHeight"), out height))
            {
                TryToInt(GetObject(image, "height"), out height);
            }
        }

        private static object GetObject(IDictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value))
            {
                return null;
            }

            return value;
        }

        private static string GetString(IDictionary<string, object> data, string key)
        {
            object value = GetObject(data, key);
            if (value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool TryToInt(object value, out int result)
        {
            if (value is int)
            {
                result = (int)value;
                return true;
            }

            if (value is long)
            {
                long longValue = (long)value;
                if (longValue >= int.MinValue && longValue <= int.MaxValue)
                {
                    result = (int)longValue;
                    return true;
                }
            }

            if (value is double)
            {
                result = (int)Math.Round((double)value, MidpointRounding.AwayFromZero);
                return true;
            }

            if (value is decimal)
            {
                result = (int)Math.Round((decimal)value, MidpointRounding.AwayFromZero);
                return true;
            }

            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static int ScoreMatch(string wanted, string candidate, string platformName)
        {
            if (string.IsNullOrEmpty(wanted) || string.IsNullOrEmpty(candidate))
            {
                return int.MinValue;
            }

            int score = 0;
            if (string.Equals(wanted, candidate, StringComparison.OrdinalIgnoreCase))
            {
                score += 2200;
            }

            if (candidate.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            {
                score += 550;
            }

            if (candidate.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 400;
            }

            if (wanted.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 300;
            }

            int overlap = CountWordOverlap(wanted, candidate);
            int maxWords = Math.Max(CountWords(wanted), CountWords(candidate));
            if (maxWords > 0)
            {
                score += (overlap * 800) / maxWords;
            }

            score -= Math.Abs(candidate.Length - wanted.Length) * 3;

            if (!string.IsNullOrEmpty(platformName))
            {
                if (platformName.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 140;
                }
                else if (platformName.IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 100;
                }
                else if (platformName.IndexOf("DOS", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 70;
                }
            }

            return score;
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static int CountWordOverlap(string a, string b)
        {
            string[] first = a.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] second = b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;

            for (int i = 0; i < first.Length; i++)
            {
                for (int j = 0; j < second.Length; j++)
                {
                    if (string.Equals(first[i], second[j], StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private void LoadIndex()
        {
            if (!File.Exists(indexFilePath))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(indexFilePath, Encoding.UTF8);
                bool hasVersionHeader = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!hasVersionHeader)
                    {
                        hasVersionHeader = true;
                        if (!string.Equals(line.Trim(), IndexVersionHeader, StringComparison.Ordinal))
                        {
                            mapClearAndRewrite();
                            return;
                        }

                        continue;
                    }

                    int split = line.IndexOf('=');
                    if (split <= 0 || split >= line.Length - 1)
                    {
                        continue;
                    }

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    iconIndex[key] = value;
                }
            }
            catch
            {
            }

            return;

            void mapClearAndRewrite()
            {
                iconIndex.Clear();
                SaveIndex_NoThrow();
            }
        }

        private void LoadCaseIndex()
        {
            if (!File.Exists(caseIndexFilePath))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(caseIndexFilePath, Encoding.UTF8);
                bool hasVersionHeader = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!hasVersionHeader)
                    {
                        hasVersionHeader = true;
                        if (!string.Equals(line.Trim(), IndexVersionHeader, StringComparison.Ordinal))
                        {
                            caseIndex.Clear();
                            SaveCaseIndex_NoThrow();
                            return;
                        }
                        continue;
                    }

                    int split = line.IndexOf('=');
                    if (split <= 0 || split >= line.Length - 1)
                    {
                        continue;
                    }

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    caseIndex[key] = value;
                }
            }
            catch
            {
            }
        }

        private void SaveIndex_NoThrow()
        {
            try
            {
                string dir = Path.GetDirectoryName(indexFilePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var lines = new List<string>();
                lines.Add(IndexVersionHeader);
                foreach (KeyValuePair<string, string> entry in iconIndex)
                {
                    lines.Add(entry.Key + "=" + entry.Value);
                }

                File.WriteAllLines(indexFilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private void SaveCaseIndex_NoThrow()
        {
            try
            {
                string dir = Path.GetDirectoryName(caseIndexFilePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var lines = new List<string>();
                lines.Add(IndexVersionHeader);
                foreach (KeyValuePair<string, string> entry in caseIndex)
                {
                    lines.Add(entry.Key + "=" + entry.Value);
                }

                File.WriteAllLines(caseIndexFilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static List<string> BuildSearchQueries(string input)
        {
            string rawTitle = Path.GetFileNameWithoutExtension(input ?? string.Empty);
            string baseTitle = NormalizeTitle(input);
            var results = new List<string>();
            AddUnique(results, baseTitle);
            AddNeedForSpeedQueries(results, rawTitle, baseTitle);
            AddGtaQueries(results, rawTitle, baseTitle);
            AddSymbolAndColonQueries(results, rawTitle, baseTitle);

            if (!string.IsNullOrEmpty(baseTitle))
            {
                string noDigitsSuffix = Regex.Replace(baseTitle, @"\s+\d{4}$", string.Empty);
                AddUnique(results, noDigitsSuffix);

                string punctuationLight = Regex.Replace(baseTitle, @"[^\w\s]", " ");
                punctuationLight = Regex.Replace(punctuationLight, @"\s+", " ").Trim();
                AddUnique(results, punctuationLight);

                string noRoman = Regex.Replace(baseTitle, @"\b(i|ii|iii|iv|v|vi|vii|viii|ix|x)\b", " ", RegexOptions.IgnoreCase);
                noRoman = Regex.Replace(noRoman, @"\s+", " ").Trim();
                AddUnique(results, noRoman);

                string romanToArabic = ReplaceRomanNumeralsWithArabic(baseTitle);
                AddUnique(results, romanToArabic);

                string arabicToRoman = ReplaceArabicNumeralsWithRoman(baseTitle);
                AddUnique(results, arabicToRoman);
            }

            return results;
        }

        private static void AddSymbolAndColonQueries(List<string> results, string rawTitle, string baseTitle)
        {
            if (!string.IsNullOrWhiteSpace(baseTitle))
            {
                AddAndAmpersandAlternatives(results, baseTitle);
                AddAnniversaryColonAlternative(results, baseTitle);
            }

            if (!string.IsNullOrWhiteSpace(rawTitle))
            {
                string normalizedRaw = NormalizeTitle(rawTitle);
                AddAndAmpersandAlternatives(results, normalizedRaw);
                AddAnniversaryColonAlternative(results, normalizedRaw);
            }

            if (!string.IsNullOrWhiteSpace(baseTitle) && baseTitle.StartsWith("Metaphor ", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = baseTitle.Substring("Metaphor ".Length).Trim();
                if (!string.IsNullOrEmpty(suffix))
                {
                    AddUnique(results, "Metaphor: " + suffix);
                }
            }
        }

        private static void AddAndAmpersandAlternatives(List<string> results, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (value.IndexOf(" and ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string withAmp = Regex.Replace(value, @"\band\b", "&", RegexOptions.IgnoreCase);
                withAmp = Regex.Replace(withAmp, @"\s+", " ").Trim();
                AddUnique(results, withAmp);
            }

            if (value.IndexOf("&", StringComparison.Ordinal) >= 0)
            {
                string withAnd = value.Replace("&", " and ");
                withAnd = Regex.Replace(withAnd, @"\s+", " ").Trim();
                AddUnique(results, withAnd);
            }
        }

        private static void AddAnniversaryColonAlternative(List<string> results, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string colonized = Regex.Replace(
                value,
                @"^(.*?)(\s+\d+(st|nd|rd|th)\s+Anniversary.*)$",
                "$1:$2",
                RegexOptions.IgnoreCase);

            if (!string.Equals(colonized, value, StringComparison.OrdinalIgnoreCase))
            {
                colonized = Regex.Replace(colonized, @"\s+", " ").Trim();
                AddUnique(results, colonized);
            }
        }

        private static void AddNeedForSpeedQueries(List<string> results, string rawTitle, string baseTitle)
        {
            if (string.IsNullOrWhiteSpace(baseTitle))
            {
                return;
            }

            if (!baseTitle.StartsWith("Need for Speed ", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string suffix = baseTitle.Substring("Need for Speed ".Length).Trim();
            if (!string.IsNullOrEmpty(suffix))
            {
                AddUnique(results, "Need for Speed: " + suffix);
                Match seVariant = Regex.Match(suffix, @"^(I|II|III|IV|V|VI|VII|VIII|IX|X|\d+)\s+SE$", RegexOptions.IgnoreCase);
                if (seVariant.Success)
                {
                    string generation = seVariant.Groups[1].Value.ToUpperInvariant();
                    AddUnique(results, "Need for Speed " + generation + ": SE");
                }
                Match episodeWithSubtitle = Regex.Match(suffix, @"^(I|II|III|IV|V|VI|VII|VIII|IX|X|\d+)\s+(.+)$", RegexOptions.IgnoreCase);
                if (episodeWithSubtitle.Success)
                {
                    string episode = episodeWithSubtitle.Groups[1].Value.ToUpperInvariant();
                    string subtitle = episodeWithSubtitle.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(subtitle))
                    {
                        AddUnique(results, "Need for Speed " + episode + ": " + subtitle);
                    }
                }

                Match romanPrefix = Regex.Match(suffix, @"^(I|II|III|IV|V|VI|VII|VIII|IX|X)\s+(.+)$", RegexOptions.IgnoreCase);
                if (romanPrefix.Success)
                {
                    string withoutRoman = romanPrefix.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(withoutRoman))
                    {
                        AddUnique(results, "Need for Speed: " + withoutRoman);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(rawTitle))
            {
                string withColon = Regex.Replace(rawTitle, @"\s*-\s*", ": ");
                withColon = Regex.Replace(withColon, @"\s+", " ").Trim();
                if (!string.IsNullOrEmpty(withColon))
                {
                    AddUnique(results, withColon);
                    AddUnique(results, NormalizeTitle(withColon));
                }
            }
        }

        private static void AddGtaQueries(List<string> results, string rawTitle, string baseTitle)
        {
            string normalizedRaw = NormalizeTitle(rawTitle);
            if (string.IsNullOrEmpty(baseTitle))
            {
                return;
            }

            if (!baseTitle.StartsWith("GTA ", StringComparison.OrdinalIgnoreCase) &&
                !normalizedRaw.StartsWith("GTA ", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string suffix = baseTitle;
            if (baseTitle.StartsWith("GTA ", StringComparison.OrdinalIgnoreCase))
            {
                suffix = baseTitle.Substring(4).Trim();
            }

            if (string.IsNullOrEmpty(suffix))
            {
                return;
            }

            AddUnique(results, "Grand Theft Auto " + suffix);
            AddUnique(results, "Grand Theft Auto: " + suffix);
        }

        private static string ReplaceRomanNumeralsWithArabic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string text = value;
            text = Regex.Replace(text, @"\bX\b", "10", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bIX\b", "9", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bVIII\b", "8", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bVII\b", "7", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bVI\b", "6", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bV\b", "5", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bIV\b", "4", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bIII\b", "3", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bII\b", "2", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bI\b", "1", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string ReplaceArabicNumeralsWithRoman(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string text = value;
            text = Regex.Replace(text, @"\b10\b", "X", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b9\b", "IX", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b8\b", "VIII", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b7\b", "VII", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b6\b", "VI", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b5\b", "V", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b4\b", "IV", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b3\b", "III", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b2\b", "II", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b1\b", "I", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            list.Add(value);
        }

        private static bool IsGtaQuery(string normalizedQuery)
        {
            if (string.IsNullOrEmpty(normalizedQuery))
            {
                return false;
            }

            return normalizedQuery.StartsWith("gta ", StringComparison.OrdinalIgnoreCase) ||
                   normalizedQuery.StartsWith("grand theft auto", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetNfsEpisodeToken(string normalizedTitle, out string token)
        {
            token = string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return false;
            }

            Match match = Regex.Match(normalizedTitle, @"^Need for Speed\s+(I|II|III|IV|V|VI|VII|VIII|IX|X|\d+)\b", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            token = match.Groups[1].Value.ToUpperInvariant();
            token = ConvertEpisodeTokenToCanonical(token);
            return !string.IsNullOrEmpty(token);
        }

        private static string ConvertEpisodeTokenToCanonical(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            int numeric;
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
            {
                return numeric.ToString(CultureInfo.InvariantCulture);
            }

            switch (token.ToUpperInvariant())
            {
                case "I": return "1";
                case "II": return "2";
                case "III": return "3";
                case "IV": return "4";
                case "V": return "5";
                case "VI": return "6";
                case "VII": return "7";
                case "VIII": return "8";
                case "IX": return "9";
                case "X": return "10";
                default: return token;
            }
        }

        private static string NormalizeAliasTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = Regex.Replace(text, @"\bgta\b", "grand theft auto", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private static string NormalizeTitle(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string text = Path.GetFileNameWithoutExtension(input);
            if (string.IsNullOrEmpty(text))
            {
                text = input;
            }

            text = text.Replace('&', ' ');
            text = text.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
            text = text.Replace(':', ' ');
            text = Regex.Replace(text, @"\[[^\]]*\]", " ");
            text = Regex.Replace(text, @"\([^\)]*\)", " ");
            text = Regex.Replace(text, @"\b(v|ver|version)\s*\d+(\.\d+)*\b", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(usa|eur|europe|japan|world|gog|steam|fitgirl|portable|rip|repack|multi\d*)\b", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        private static string DownloadString(string url)
        {
            byte[] data = DownloadBytes(url);
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(data);
        }

        private static byte[] DownloadBytes(string url)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.UserAgent = "SidebarGameLauncher/1.0";

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        return memory.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string MakeSafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                name = name.Replace(invalid[i], '_');
            }

            return name;
        }
    }
}

