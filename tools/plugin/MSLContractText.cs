// MSLContractText - Contract Configurator does not resolve #keys, so the MSL contracts' localization keys are
// replaced in the game database with the player's language after ModuleManager has patched and before Contract
// Configurator reads it. Registered by reflection as a ModuleManager post-patch callback; ModuleManagerPostLoad
// translates again in case that failed. Only MSL* CONTRACT_GROUP / CONTRACT_TYPE values that are a whole key.
using System;
using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;
using UnityEngine;

namespace MSLAeroshell
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class MSLContractText : MonoBehaviour
    {
        void Awake() { MSLPostPatch.Register(typeof(MSLContractText), "Translate"); }

        // ModuleManager calls this static method in every assembly after loading
        public static void ModuleManagerPostLoad() { Translate(); }

        static void Translate()
        {
            if (GameDatabase.Instance == null) return;
            Dictionary<string, string> text = Dictionary(Localizer.CurrentLanguage);
            Dictionary<string, string> english = Dictionary("en-us");
            int n = 0;
            foreach (string type in new[] { "CONTRACT_GROUP", "CONTRACT_TYPE" })
                foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(type))
                {
                    string name = node.GetValue("name");
                    if (name != null && name.StartsWith("MSL")) n += Replace(node, text, english);
                }
            if (n > 0) Debug.Log("[MSLAeroshell] contract text: " + n + " values in " + Localizer.CurrentLanguage);
        }

        static Dictionary<string, string> Dictionary(string language)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(language)) return d;
            foreach (ConfigNode root in GameDatabase.Instance.GetConfigNodes("Localization"))
            {
                ConfigNode lang = root.GetNode(language);
                if (lang == null) continue;
                foreach (ConfigNode.Value v in lang.values)
                    if (v.name.StartsWith("#MSL_")) d[v.name] = v.value;
            }
            return d;
        }

        static int Replace(ConfigNode node, Dictionary<string, string> text, Dictionary<string, string> english)
        {
            int n = 0;
            foreach (ConfigNode.Value v in node.values)
            {
                if (v.value == null || !v.value.StartsWith("#MSL_")) continue;
                string s;
                if (text.TryGetValue(v.value, out s) || english.TryGetValue(v.value, out s)) { v.value = s; n++; }
            }
            foreach (ConfigNode child in node.nodes) n += Replace(child, text, english);
            return n;
        }
    }
}
