using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ConsoleANSI
{
    public class ConsoleAnsiArtist
    {
        private static readonly Dictionary<char, string[]> _ansiArtLibrary = new Dictionary<char, string[]>
        {
            {'A', new string[]{" █████╗ ", "██╔══██╗", "███████║", "██╔══██║", "██║  ██║", "╚═╝  ╚═╝"}},
            {'B', new string[]{"██████╗ ", "██╔══██╗", "██████╔╝", "██╔══██╗", "██████╔╝", "╚═════╝ "}},
            {'C', new string[]{" ██████╗", "██╔════╝", "██║     ", "██║     ", "╚██████╗", " ╚═════╝"}},
            {'D', new string[]{"██████╗ ", "██╔══██╗", "██║  ██║", "██║  ██║", "██████╔╝", "╚═════╝ "}},
            {'E', new string[]{"███████╗", "██╔════╝", "█████╗  ", "██╔══╝  ", "███████╗", "╚══════╝"}},
            {'F', new string[]{"███████╗", "██╔════╝", "█████╗  ", "██╔══╝  ", "██║     ", "╚═╝     "}},
            {'G', new string[]{" ██████╗ ", "██╔════╝ ", "██║  ███╗", "██║   ██║", "╚██████╔╝", " ╚═════╝ "}},
            {'H', new string[]{"██╗  ██╗", "██║  ██║", "███████║", "██╔══██║", "██║  ██║", "╚═╝  ╚═╝"}},
            {'I', new string[]{"██╗", "██║", "██║", "██║", "██║", "╚═╝"}},
            {'J', new string[]{"     ██╗", "     ██║", "     ██║", "██   ██║", "╚█████╔╝", " ╚════╝ "}},
            {'K', new string[]{"██╗  ██╗", "██║ ██╔╝", "█████╔╝ ", "██╔═██╗ ", "██║  ██╗", "╚═╝  ╚═╝"}},
            {'L', new string[]{"██╗     ", "██║     ", "██║     ", "██║     ", "███████╗", "╚══════╝"}},
            {'M', new string[]{"███╗   ███╗", "████╗ ████║", "██╔████╔██║", "██║╚██╔╝██║", "██║ ╚═╝ ██║", "╚═╝     ╚═╝"}},
            {'N', new string[]{"███╗   ██╗", "████╗  ██║", "██╔██╗ ██║", "██║╚██╗██║", "██║ ╚████║", "╚═╝  ╚═══╝"}},
            {'O', new string[]{" ██████╗ ", "██╔═══██╗", "██║   ██║", "██║   ██║", "╚██████╔╝", " ╚═════╝ "}},
            {'P', new string[]{"██████╗ ", "██╔══██╗", "██████╔╝", "██╔═══╝ ", "██║     ", "╚═╝     "}},
            {'Q', new string[]{" ██████╗ ", "██╔═══██╗", "██║   ██║", "██║▄▄ ██║", "╚██████╔╝", " ╚══▀▀═╝ "}},
            {'R', new string[]{"██████╗ ", "██╔══██╗", "██████╔╝", "██╔══██╗", "██║  ██║", "╚═╝  ╚═╝"}},
            {'S', new string[]{" ███████╗", "██╔═════╝", "███████╗ ", "╚════██║ ", "███████║ ", "╚══════╝ "}},
            {'T', new string[]{"████████╗", "╚══██╔══╝", "   ██║   ", "   ██║   ", "   ██║   ", "   ╚═╝   "}},
            {'U', new string[]{"██╗   ██╗", "██║   ██║", "██║   ██║", "██║   ██║", "╚██████╔╝", " ╚═════╝ "}},
            {'V', new string[]{"██╗   ██╗", "██║   ██║", "██║   ██║", "╚██╗ ██╔╝", " ╚████╔╝ ", "  ╚═══╝  "}},
            {'W', new string[]{"██╗    ██╗", "██║    ██║", "██║ █╗ ██║", "██║███╗██║", "╚███╔███╔╝", " ╚══╝╚══╝ "}},
            {'X', new string[]{"██╗  ██╗", "╚██╗██╔╝", " ╚███╔╝ ", " ██╔██╗ ", "██╔╝ ██╗", "╚═╝  ╚═╝"}},
            {'Y', new string[]{"██╗   ██╗", "╚██╗ ██╔╝", " ╚████╔╝ ", "  ╚██╔╝  ", "   ██║   ", "   ╚═╝   "}},
            {'Z', new string[]{"███████╗", "╚══███╔╝", "  ███╔╝ ", " ███╔╝  ", "███████╗", "╚══════╝"}},
            {',', new string[]{"   ", "   ", "   ", "██╗", "██║", "╚═╝"}},
            {'?', new string[]{" ██████╗ ", "██╔═══██╗", "     ██╔╝", "   ██╔╝  ", "   ╚═╝   ", "   ██╗   "}},
            {'\'', new string[]{"██╗", "██║", "╚═╝", "   ", "   ", "   "}},
            {'"', new string[]{"██╗   ██╗", "██║   ██║", "╚═╝   ╚═╝", "        ", "        ", "        "}},
            {'-', new string[]{"   ", "   ", "█████╗", "   ", "   ", "   "}},
            {'!', new string[]{" ██╗ ", " ██║ ", " ██║ ", " ██║ ", " ╚═╝ ", " ██╗ "}},
            {' ', new string[]{"     ", "     ", "     ", "     ", "     ", "     "}}
        };

        private class CharArtData { public string Char { get; set; } = ""; public string[] Lines { get; set; } = Array.Empty<string>(); }
        
        public static void ImportFromJson(string json, bool overwrite = true)
        {
            var items = JsonSerializer.Deserialize<List<CharArtData>>(json);
            foreach (var item in items)
            {
                char c = item.Char[0];
                if (overwrite || !_ansiArtLibrary.ContainsKey(c))
                    _ansiArtLibrary[c] = item.Lines;
            }
        }
        
        public static void ImportFromJsonFile(string filePath, bool overwrite = true)
        {
            string json = System.IO.File.ReadAllText(filePath, Encoding.UTF8);
            ImportFromJson(json, overwrite);
        }

        private static bool IsValidRgbColor(string rgbString)
        {
            var parts = rgbString.Split(',');
            if (parts.Length != 3) return false;
            foreach (var part in parts)
                if (!int.TryParse(part.Trim(), out int value) || value < 0 || value > 255) return false;
            return true;
        }

        private static string GetAnsiColorCode(string rgbString)
        {
            var parts = rgbString.Split(',').Select(p => int.Parse(p.Trim())).ToArray();
            return $"\u001b[38;2;{parts[0]};{parts[1]};{parts[2]}m";
        }

        private static string GetAnsiBackgroundColorCode(string rgbString)
        {
            var parts = rgbString.Split(',').Select(p => int.Parse(p.Trim())).ToArray();
            return $"\u001b[48;2;{parts[0]};{parts[1]};{parts[2]}m";
        }

        private static string GetAnsiResetCode() => "\u001b[0m";

        public static void PrintAnsiText(string text) => PrintAnsiText(text, "", "");

        public static void PrintAnsiText(string text, string foregroundColor, string backgroundColor = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            bool hasValidForeground = !string.IsNullOrEmpty(foregroundColor) && IsValidRgbColor(foregroundColor);
            bool hasValidBackground = !string.IsNullOrEmpty(backgroundColor) && IsValidRgbColor(backgroundColor);
            
            string colorPrefix = "";
            if (hasValidForeground) colorPrefix += GetAnsiColorCode(foregroundColor);
            if (hasValidBackground) colorPrefix += GetAnsiBackgroundColorCode(backgroundColor);
            string colorSuffix = string.IsNullOrEmpty(colorPrefix) ? "" : GetAnsiResetCode();
            
            bool canPrintAsAnsi = text.All(c => _ansiArtLibrary.ContainsKey(GetCharKey(c)) || c == ' ' || IsChineseCharacter(c));
            
            if (!canPrintAsAnsi)
            {
                if (!string.IsNullOrEmpty(colorPrefix)) { Console.Write(colorPrefix); Console.Write(text); Console.WriteLine(colorSuffix); }
                else Console.WriteLine(text);
                return;
            }
            
            int maxHeight = 0;
            foreach (char c in text)
            {
                char key = GetCharKey(c);
                if (_ansiArtLibrary.ContainsKey(key))
                    maxHeight = Math.Max(maxHeight, _ansiArtLibrary[key].Length);
            }
            
            if (maxHeight == 0)
            {
                if (!string.IsNullOrEmpty(colorPrefix)) { Console.Write(colorPrefix); Console.Write(text); Console.WriteLine(colorSuffix); }
                else Console.WriteLine(text);
                return;
            }
            
            for (int line = 0; line < maxHeight; line++)
            {
                StringBuilder lineBuilder = new StringBuilder();
                if (!string.IsNullOrEmpty(colorPrefix)) lineBuilder.Append(colorPrefix);
                
                foreach (char c in text)
                {
                    char key = GetCharKey(c);
                    if (c == ' ') lineBuilder.Append("   ");
                    else if (IsChineseCharacter(c) && !_ansiArtLibrary.ContainsKey(c))
                    {
                        if (line == 0) lineBuilder.Append(c).Append(" ");
                        else lineBuilder.Append("  ");
                    }
                    else if (_ansiArtLibrary.ContainsKey(key))
                    {
                        var artLines = _ansiArtLibrary[key];
                        if (line < artLines.Length) lineBuilder.Append(artLines[line]);
                        else if (artLines.Length > 0) lineBuilder.Append(new string(' ', artLines[0].Length));
                        else lineBuilder.Append("   ");
                        lineBuilder.Append(' ');
                    }
                }
                
                if (!string.IsNullOrEmpty(colorSuffix)) lineBuilder.Append(colorSuffix);
                Console.WriteLine(lineBuilder.ToString());
            }
        }

        private static char GetCharKey(char c)
        {
            if (IsSpecialSymbol(c)) return c;
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return char.ToUpper(c);
            return c;
        }

        private static bool IsSpecialSymbol(char c) => c == ',' || c == '?' || c == '\'' || c == '"' || c == '-' || c == '!';

        private static bool IsChineseCharacter(char c) => (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);
        
        public static void AddCustomCharacter(char character, string[] artLines) => _ansiArtLibrary[character] = artLines;
    }
}