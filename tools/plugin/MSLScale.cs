// MSLScale - MSL part scale for the planet pack in use.
// Without RealSolarSystem the stock-scale patches put every part at 0.625 x, which only suits stock Kerbin. After
// ModuleManager and before the part loader, parts are rescaled from the home world radius:
//     s = 0.625 x (R / 600 km) ^ (ln 1.6 / ln (6371 / 600)), clamped to 0.625 .. 1
// (KSRSS 0.83, JNSQ 0.76, real-scale Sol 1.0). GameData/MarsScienceLab/MSL_Scale.cfg can set the number instead.
// With RealSolarSystem nothing happens (parts 1:1).
// Patches/MSL_ScaleTable.cfg lists what the stock patches scale and with which power (lengths 1, masses / thrust /
// areas 2, torques 3); each value is multiplied by (s / rescaleFactor)^p, so ballistic coefficient, T/W and
// delta-v stay those of the 1:1 parts. Shipped craft get *_Scaled.craft copies at the new scale.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MSLAeroshell
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class MSLScale : MonoBehaviour
    {
        const double StockRadius = 600000, EarthRadius = 6371000, StockScale = 0.625;
        static readonly string[] Crafts = { "MSL_Guided_Entry", "MSL_Curiosity_2012" };
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // the scale the parts were left at (0 until the post-patch pass has run; 1 with RSS)
        internal static double Scale;
        static bool done;

        void Awake() { MSLPostPatch.Register(typeof(MSLScale), "Apply"); }

        // also called from ModuleManagerPostLoad in case the post-patch callback could not be registered: once only
        static void Apply()
        {
            if (done || GameDatabase.Instance == null) return;
            done = true;
            try { Rescale(); }
            catch (Exception e) { Debug.LogError("[MSLAeroshell] scale: " + e); }
        }

        public static void ModuleManagerPostLoad() { Apply(); }

        static bool Loaded(string assembly)
        {
            foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                if (a.name == assembly) return true;
            return false;
        }

        static void Rescale()
        {
            if (Loaded("RealSolarSystem")) { Scale = 1; Debug.Log("[MSLAeroshell] scale: RealSolarSystem, parts 1:1"); return; }
            ConfigNode table = null;
            foreach (ConfigNode t in GameDatabase.Instance.GetConfigNodes("MSL_SCALE_TABLE")) table = t;
            if (table == null) { Debug.LogWarning("[MSLAeroshell] scale: no MSL_SCALE_TABLE, parts left at stock scale"); Scale = StockScale; return; }
            double stock = Num(table.GetValue("stockScale"), StockScale);

            string how;
            double s = Target(out how);
            Scale = s;
            Debug.Log("[MSLAeroshell] scale: " + how + " -> MSL parts at " + s.ToString("0.###", C) + " x");
            if (Math.Abs(s - stock) < 1e-4) return;

            var parts = new Dictionary<string, ConfigNode>();
            foreach (ConfigNode p in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                string name = p.GetValue("name");
                if (name != null) parts[name.Replace('.', '_')] = p;
            }
            int n = 0;
            foreach (ConfigNode row in table.GetNodes("PART"))
            {
                ConfigNode part;
                if (!parts.TryGetValue(row.GetValue("name") ?? "", out part)) continue;
                // the scale this part is at now: the stock patch's rescaleFactor (a part another mod pinned is left alone)
                double now = Num(part.GetValue("rescaleFactor"), 1);
                if (Math.Abs(now - stock) > 1e-4) continue;
                double k = s / now;
                foreach (string entry in row.GetValues("scale"))
                {
                    int comma = entry.LastIndexOf(',');
                    if (comma < 0) continue;
                    double power = Num(entry.Substring(comma + 1), double.NaN);
                    if (double.IsNaN(power)) continue;
                    n += Walk(part, entry.Substring(0, comma).Trim().Split('/'), 0, Math.Pow(k, power));
                }
            }
            Debug.Log("[MSLAeroshell] scale: " + n + " values rescaled from " + stock.ToString("0.###", C) + " x");
        }

        // the wanted scale: MSL_SCALE { scale = auto | number } (MSL_Scale.cfg), else from the home world radius
        static double Target(out string how)
        {
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("MSL_SCALE"))
            {
                string v = (n.GetValue("scale") ?? "auto").Trim();
                double fixedScale = Num(v, double.NaN);
                if (!double.IsNaN(fixedScale) && fixedScale > 0.1 && fixedScale <= 2) { how = "MSL_Scale.cfg scale = " + v; return fixedScale; }
            }
            double r = HomeRadius(out how);
            double s = StockScale * Math.Pow(r / StockRadius, Math.Log(1 / StockScale) / Math.Log(EarthRadius / StockRadius));
            s = Math.Round(Math.Max(StockScale, Math.Min(1, s)), 3);
            return s;
        }

        // the home world radius as the planet pack configures it (Kopernicus nodes), else stock Kerbin
        static double HomeRadius(out string how)
        {
            double radius = double.NaN, kerbin = double.NaN;
            string home = null;
            foreach (ConfigNode k in GameDatabase.Instance.GetConfigNodes("Kopernicus"))
                foreach (ConfigNode body in k.GetNodes("Body"))
                {
                    ConfigNode prop = body.GetNode("Properties");
                    double r = prop != null ? Num(prop.GetValue("radius"), double.NaN) : double.NaN;
                    string name = body.GetValue("cbNameLater") ?? body.GetValue("name");
                    if (prop != null && string.Equals(prop.GetValue("isHomeWorld"), "true", StringComparison.OrdinalIgnoreCase))
                    { radius = r; home = name; }
                    else if (body.GetValue("name") == "Kerbin" && double.IsNaN(kerbin)) { kerbin = r; if (home == null) home = name; }
                }
            if (double.IsNaN(radius)) radius = kerbin;
            if (double.IsNaN(radius))
            {
                // no radius in the configs: the stock 600 km, times Sigma Dimensions' Resize where it is installed
                double resize = 1;
                foreach (ConfigNode sd in GameDatabase.Instance.GetConfigNodes("SigmaDimensions")) resize = Num(sd.GetValue("Resize"), 1);
                radius = StockRadius * resize;
                how = resize != 1 ? "Sigma Dimensions Resize " + resize.ToString("0.###", C) : "stock home world";
            }
            else how = "home world " + (home ?? "?");
            how += " radius " + (radius / 1000).ToString("0", C) + " km";
            return radius;
        }

        // path segments NODE[name] (name with a trailing * is a prefix, * alone any) down to a value; returns the count
        static int Walk(ConfigNode node, string[] path, int i, double f)
        {
            string seg = path[i];
            if (i == path.Length - 1)
            {
                int n = 0;
                foreach (ConfigNode.Value v in node.values)
                {
                    if (v.name != seg) continue;
                    string scaled = Multiply(v.value, f);
                    if (scaled != null) { v.value = scaled; n++; }
                }
                return n;
            }
            int open = seg.IndexOf('[');
            string type = open < 0 ? seg : seg.Substring(0, open);
            string pat = open < 0 ? "*" : seg.Substring(open + 1, seg.Length - open - 2);
            int count = 0;
            foreach (ConfigNode child in node.nodes)
            {
                if (child.name != type) continue;
                string name = child.GetValue("name") ?? "";
                bool match = pat == "*" || (pat.EndsWith("*") ? name.StartsWith(pat.Substring(0, pat.Length - 1)) : name == pat);
                if (match) count += Walk(child, path, i + 1, f);
            }
            return count;
        }

        // a number or a comma-separated vector times f; null when not numeric
        static string Multiply(string text, double f)
        {
            string[] parts = text.Split(',');
            for (int j = 0; j < parts.Length; j++)
            {
                double x;
                if (!double.TryParse(parts[j].Trim(), NumberStyles.Float, C, out x)) return null;
                parts[j] = (x * f).ToString("0.#########", C);
            }
            return string.Join(", ", parts);
        }

        static double Num(string s, double fallback)
        {
            double x;
            return s != null && double.TryParse(s.Trim(), NumberStyles.Float, C, out x) ? x : fallback;
        }

        // ---- craft: Ships/VAB/<craft>_Scaled.craft from the 1:1 craft, written at the main menu
        internal static void WriteCraft()
        {
            string dir = Path.Combine(KSPUtil.ApplicationRootPath, "Ships/VAB");
            bool need = Scale > 0 && Math.Abs(Scale - StockScale) > 1e-4 && Math.Abs(Scale - 1) > 1e-4;
            foreach (string name in Crafts)
            {
                string src = Path.Combine(dir, name + ".craft"), dst = Path.Combine(dir, name + "_Scaled.craft");
                try
                {
                    if (!need) { if (File.Exists(dst)) File.Delete(dst); continue; }
                    if (!File.Exists(src)) continue;
                    File.WriteAllText(dst, ScaleCraft(File.ReadAllText(src), Scale, " (" + Scale.ToString("0.###", C) + "x)"));
                    Debug.Log("[MSLAeroshell] scale: wrote " + dst);
                }
                catch (Exception e) { Debug.LogWarning("[MSLAeroshell] scale: craft " + name + ": " + e.Message); }
            }
        }

        static readonly Regex Pos = new Regex(@"^(\s*)(pos|attPos|attPos0) = (.+)$");
        static readonly Regex AttN = new Regex(@"^(\s*attN = [^,]+,.+_\d+_)([-\d.eE]+)\|([-\d.eE]+)\|([-\d.eE]+)$");
        static readonly Regex Ship = new Regex(@"^(ship = )(.+)$");
        static readonly Regex Size = new Regex(@"^(size = )(.+)$");

        // as make_stock_scale.scale_craft: pos about the root part, attPos / attPos0, attN node offsets and size x s
        static string ScaleCraft(string text, double s, string suffix)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            double[] root = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                Match m = Pos.Match(line);
                if (m.Success)
                {
                    double[] v = Vec(m.Groups[3].Value);
                    if (v != null)
                    {
                        if (m.Groups[2].Value == "pos")
                        {
                            if (root == null) root = (double[])v.Clone();
                            for (int j = 0; j < v.Length && j < root.Length; j++) v[j] = root[j] + (v[j] - root[j]) * s;
                        }
                        else for (int j = 0; j < v.Length; j++) v[j] *= s;
                        line = m.Groups[1].Value + m.Groups[2].Value + " = " + Join(v, "0.000000", ",");
                    }
                }
                m = AttN.Match(line);
                if (m.Success)
                    line = m.Groups[1].Value + Join(new[] { Num(m.Groups[2].Value, 0) * s, Num(m.Groups[3].Value, 0) * s, Num(m.Groups[4].Value, 0) * s }, "0.0000000000", "|");
                m = Ship.Match(line);
                if (m.Success) line = m.Groups[1].Value + m.Groups[2].Value + suffix;
                m = Size.Match(line);
                if (m.Success)
                {
                    double[] v = Vec(m.Groups[2].Value);
                    if (v != null) { for (int j = 0; j < v.Length; j++) v[j] *= s; line = m.Groups[1].Value + Join(v, "0.#########", ","); }
                }
                lines[i] = line;
            }
            return string.Join("\n", lines);
        }

        static double[] Vec(string text)
        {
            string[] p = text.Split(',');
            double[] v = new double[p.Length];
            for (int j = 0; j < p.Length; j++) { v[j] = Num(p[j], double.NaN); if (double.IsNaN(v[j])) return null; }
            return v;
        }

        static string Join(double[] v, string format, string sep)
        {
            string[] s = new string[v.Length];
            for (int j = 0; j < v.Length; j++) s[j] = v[j].ToString(format, C);
            return string.Join(sep, s);
        }
    }

    // the scaled craft are written once the game is up (the Ships folder is the game's, not the database's)
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class MSLScaleCraft : MonoBehaviour
    {
        void Start() { MSLScale.WriteCraft(); }
    }

    // ModuleManager post-patch callbacks, registered by reflection (no compile-time reference to ModuleManager): they
    // run after all patches (and on a ConfigCache load), before ModuleManagerPostLoad and the part / contract loaders
    internal static class MSLPostPatch
    {
        internal static void Register(Type owner, string method)
        {
            try
            {
                Type loader = null, callback = null;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                {
                    if (loader == null) loader = a.assembly.GetType("ModuleManager.PostPatchLoader");
                    if (callback == null) callback = a.assembly.GetType("ModuleManager.ModuleManagerPostPatchCallback");
                }
                MethodInfo add = loader != null ? loader.GetMethod("AddPostPatchCallback", BindingFlags.Public | BindingFlags.Static) : null;
                if (add != null && callback != null)
                    add.Invoke(null, new object[] { Delegate.CreateDelegate(callback, owner.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)) });
            }
            catch (Exception e) { Debug.LogWarning("[MSLAeroshell] " + owner.Name + ": no post-patch callback (" + e.Message + ")"); }
        }
    }
}
