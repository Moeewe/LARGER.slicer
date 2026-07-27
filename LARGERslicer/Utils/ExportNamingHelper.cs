using System;
using System.IO;
using System.Text;

namespace LARGERslicer.Utils
{
    internal static class ExportNamingHelper
    {
        public static string BuildBaseName(
            DateTime date,
            string projectName,
            string partNumber,
            string subPart,
            string revision,
            string documentType)
        {
            string prefix = "PS" + date.ToString("yyyyMMdd");
            string project = SanitizeToken(projectName, "Projekt");
            string part = SanitizeToken(partNumber, "Teil");
            string sub = SanitizeToken(subPart, "Allgemein");
            string doc = SanitizeToken(documentType, "Geometrie");
            string rev = SanitizeToken(revision, string.Empty);

            var sb = new StringBuilder();
            sb.Append(prefix);
            sb.Append("_");
            sb.Append(project);
            sb.Append(" - ");
            sb.Append(doc);
            sb.Append(" - ");
            sb.Append(part);
            sb.Append(" - ");
            sb.Append(sub);

            if (!string.IsNullOrWhiteSpace(rev))
            {
                sb.Append(" - ");
                sb.Append(rev);
            }

            return CollapseWhitespace(sb.ToString().Trim());
        }

        public static string EnsureExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return ".txt";
            return extension.StartsWith(".") ? extension : "." + extension;
        }

        public static string SanitizeToken(string value, string fallback)
        {
            string text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                bool isInvalid = false;
                for (int i = 0; i < invalid.Length; i++)
                {
                    if (c == invalid[i])
                    {
                        isInvalid = true;
                        break;
                    }
                }

                if (isInvalid)
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            return CollapseWhitespace(sb.ToString().Trim());
        }

        private static string CollapseWhitespace(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);
            bool prevWasSpace = false;
            foreach (char c in input)
            {
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (!prevWasSpace)
                        sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }

                prevWasSpace = isSpace;
            }

            return sb.ToString();
        }
    }
}