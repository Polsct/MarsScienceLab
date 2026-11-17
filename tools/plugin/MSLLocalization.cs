// Shared by the three MSL plugin assemblies: cached UI text, so physics updates do not parse or allocate.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KSP.Localization;

namespace MSL.Localization
{
    internal static class Text
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        internal static string Get(string key)
        {
            string value;
            if (!cache.TryGetValue(key, out value))
            {
                value = Localizer.Format(key);
                cache.Add(key, value);
            }
            return value;
        }

        // a composite format string from a value written with <<1>> / <<1:F2>> placeholders (a cfg value cannot hold
        // braces: KSP cut "{0:F0} m/s" off at the brace).  Localizer.Format is not used: it drops the format part.
        internal static string Pattern(string key)
        {
            string value;
            if (!cache.TryGetValue("pattern:" + key, out value))
            {
                string raw;
                if (!Localizer.TryGetStringByTag(key, out raw)) raw = key;
                value = Regex.Replace(raw, @"<<(\d+)(:[^>]+)?>>", m => "{" + (int.Parse(m.Groups[1].Value) - 1) + m.Groups[2].Value + "}");
                cache.Add("pattern:" + key, value);
            }
            return value;
        }
    }
}
