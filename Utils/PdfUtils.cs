using UglyToad.PdfPig;
using System.Collections.Generic;
using System.Linq;

namespace helpdesk.Api.Utils
{
    public static class PdfUtils
    {
        // Return dictionary: pageNumber -> text
        public static Dictionary<int, string> ReadPages(System.IO.Stream s)
        {
            var res = new Dictionary<int, string>();
            using var doc = PdfDocument.Open(s);
            foreach (var p in doc.GetPages())
            {
                // Normalize whitespace
                var text = string.Join("\n", p.Text.Split('\n').Select(l => l.Trim())).Trim();
                res[p.Number] = text;
            }
            return res;
        }

        // Simple chunker: chunk each page into slices up to maxChars with overlap
        public static List<(int Page, string Text)> ChunkPages(Dictionary<int, string> pages, int maxChars = 1200, int overlap = 200)
        {
            var outList = new List<(int, string)>();
            foreach (var kv in pages.OrderBy(k => k.Key))
            {
                var page = kv.Key;
                var text = kv.Value ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                int start = 0;
                while (start < text.Length)
                {
                    var len = System.Math.Min(maxChars, text.Length - start);
                    outList.Add((page, text.Substring(start, len)));
                    start += (maxChars - overlap);
                }
            }
            return outList;
        }
    }
}
