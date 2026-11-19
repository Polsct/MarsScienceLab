// MSLStatusWindow - toolbar window with EDL progress, flight data, entry guidance, powered descent / sky crane and
// rover status. It reads the same module fields as the part menus (Sky Crane and Curiosity modules by name, no
// compile-time reference) and offers the buttons that apply at the current step. Persisted event times are read
// back after a load; the others are kept from what the window saw in this scene.
using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

namespace MSLAeroshell
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MSLStatusWindow : MonoBehaviour
    {
        const string IconUrl = "MarsScienceLab/Icons/toolbar";
        const float SamplePeriod = .25f;

        // kept across flight scenes of one game session
        static bool visible;
        const float Width = 360;
        // right of the screen, clear of the stock toolbar column (scaled with the UI)
        static Rect window = new Rect(Screen.width - Width - 70 * GameSettings.UI_SCALE, 90, Width, 100);
        static bool showSteps;
        // null = follow the step (open while the section matters), else the player's last click
        static bool? showFlight, showGuidance, showDescent, showRover;
        static bool showSettings;

        // Window size: the whole window drawn scaled through GUI.matrix, so the layout keeps its
        // unscaled units. Kept in the game's GameData/MarsScienceLab/PluginData, not in the save.
        const float ScaleMin = .6f, ScaleMax = 2f, ScaleStep = .1f;
        static float scale = 1f;
        static bool scaleLoaded;
        static string SettingsFile { get { return KSPUtil.ApplicationRootPath + "GameData/MarsScienceLab/PluginData/MSLStatusWindow.cfg"; } }

        ApplicationLauncherButton button;
        Texture2D icon;
        bool uiHidden;
        int windowId;
        float nextSample;
        float contentBottom;    // last drawn row, window coordinates: the window shrinks back to it when rows go away

        // modules found in the last sample (null when absent)
        Vessel active;
        ModuleMSLEDLSequencer seq;
        ModuleMSLEntryGuidance guidance;
        ModuleMSLParachute fallbackChute;
        PartModule realChute, descent, bridle, rig;
        Vessel descentVessel, roverVessel;
        bool cruiseOn, chuteOut, shieldOn, payloadOn;
        // RCS switched here, not in the part menus: cruise REA clusters, Sky Crane RCS pairs
        readonly List<ModuleRCS> cruiseRcs = new List<ModuleRCS>(), craneRcs = new List<ModuleRCS>();
        string descentPhase = "";
        // sampled here, not per OnGUI pass
        ModuleDecouple backshellSep;
        double propAmount, propMax, ec, ecMax;
        bool inAir;             // dynamic pressure worth showing Mach / q

        // EDL steps, in flight order; reached[i] = the vehicle has got there, at[i] = UT seen (or < 0)
        static readonly string[] stepKeys = {
            "#MSL_Aeroshell_UI_Win_step_cruise", "#MSL_Aeroshell_UI_Win_step_entry", "#MSL_Aeroshell_UI_Win_step_parachute",
            "#MSL_Aeroshell_UI_Win_step_heat_shield", "#MSL_Aeroshell_UI_Win_step_backshell", "#MSL_Aeroshell_UI_Win_step_powered",
            "#MSL_Aeroshell_UI_Win_step_sky_crane", "#MSL_Aeroshell_UI_Win_step_flyaway", "#MSL_Aeroshell_UI_Win_step_touchdown" };
        const int Cruise = 0, Entry = 1, Parachute = 2, HeatShield = 3, Backshell = 4, Powered = 5, SkyCrane = 6, Flyaway = 7, Touchdown = 8;
        readonly bool[] reached = new bool[stepKeys.Length];
        readonly double[] at = { -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        int step = -1;
        bool wasAboveAtmosphere, roverSeenFlying;
        float targetLat = float.NaN, targetLon = float.NaN;    // last guidance target, kept for the rover distance

        // Countdown to the next auto-EDL event from the sequencer's triggers (delays exactly, speed / Mach / altitude
        // from current rates). countdownStep = target step (-1 = none), countdown in game seconds.
        int countdownStep = -1;
        double countdown;
        double rateUT = -1, rateSpeed, rateMach, decel = double.NaN, machDrop = double.NaN;

        GUIStyle head, label, value, done, current, pending, small, note, noteRight;
        Texture2D barDone, barCurrent, barPending;

        static string T(string key) { return MSL.Localization.Text.Get(key); }

        void Start()
        {
            windowId = GetHashCode();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(RemoveButton);
            GameEvents.onHideUI.Add(HideUI);
            GameEvents.onShowUI.Add(ShowUI);
            if (ApplicationLauncher.Ready) AddButton();
            LoadSettings();
        }

        static void LoadSettings()
        {
            if (scaleLoaded) return;
            scaleLoaded = true;
            try
            {
                if (!System.IO.File.Exists(SettingsFile)) return;
                ConfigNode n = ConfigNode.Load(SettingsFile);
                float s;
                if (n != null && float.TryParse(n.GetValue("scale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s))
                    SetScale(s, false);
            }
            catch (Exception e) { Debug.LogWarning("[MSLStatusWindow] settings not read: " + e.Message); }
        }

        static void SaveSettings()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
                var n = new ConfigNode();
                n.AddValue("scale", scale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                n.Save(SettingsFile);
            }
            catch (Exception e) { Debug.LogWarning("[MSLStatusWindow] settings not saved: " + e.Message); }
        }

        // the window keeps its top-left corner on screen when the size changes
        static void SetScale(float s, bool save)
        {
            s = Mathf.Clamp(Mathf.Round(s * 100) / 100, ScaleMin, ScaleMax);
            if (Mathf.Approximately(s, scale)) return;
            window.x *= scale / s; window.y *= scale / s;
            window.height = 0;
            scale = s;
            if (save) SaveSettings();
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(RemoveButton);
            GameEvents.onHideUI.Remove(HideUI);
            GameEvents.onShowUI.Remove(ShowUI);
            RemoveButton(GameScenes.FLIGHT);
            ReleaseTyping();
        }

        void HideUI() { uiHidden = true; }
        void ShowUI() { uiHidden = false; }

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            icon = GameDatabase.Instance.GetTexture(IconUrl, false);
            if (icon == null) { icon = new Texture2D(38, 38); icon.SetPixels(new Color[38 * 38]); icon.Apply(); }
            button = ApplicationLauncher.Instance.AddModApplication(
                delegate { visible = true; }, delegate { visible = false; }, null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW, icon);
            if (visible) button.SetTrue(false);
        }

        void RemoveButton(GameScenes scene)
        {
            if (button == null) return;
            if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
        }

        // ---------------------------------------------------------------- reading the modules

        static object Get(PartModule m, string name)
        {
            if (m == null) return null;
            BaseField f = m.Fields[name];
            if (f != null) return f.GetValue(m);
            System.Reflection.FieldInfo fi = m.GetType().GetField(name);
            if (fi != null) return fi.GetValue(m);
            System.Reflection.PropertyInfo pi = m.GetType().GetProperty(name);
            return pi != null && pi.GetIndexParameters().Length == 0 ? pi.GetValue(m, null) : null;
        }
        static string Str(PartModule m, string name) { object o = Get(m, name); return o != null ? o.ToString() : ""; }
        static bool Flag(PartModule m, string name) { object o = Get(m, name); return o is bool && (bool)o; }
        static double Num(PartModule m, string name)
        {
            object o = Get(m, name);
            if (o is float) return (float)o;
            if (o is double) return (double)o;
            if (o is int) return (int)o;
            return double.NaN;
        }

        static PartModule Find(Vessel v, string module)
        {
            if (v == null) return null;
            foreach (Part p in v.parts)
            {
                PartModule m = MSL.Modules.Named(p, module);
                if (m != null) return m;
            }
            return null;
        }

        // on the active vessel first, else on the nearest loaded vessel (the descent stage flying away, the rover
        // after the player switched to the stage)
        PartModule FindNear(string module, out Vessel on)
        {
            on = null;
            PartModule best = Find(active, module);
            if (best != null) { on = active; return best; }
            double d = double.MaxValue;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == active) continue;
                PartModule m = Find(v, module);
                if (m == null) continue;
                double dd = Vector3d.Distance(v.CoMD, active.CoMD);
                if (dd < d) { d = dd; best = m; on = v; }
            }
            return best;
        }

        static bool HasPart(Vessel v, string name)
        {
            if (v == null) return false;
            foreach (Part p in v.parts) if (p.partInfo != null && p.partInfo.name == name) return true;
            return false;
        }

        static bool Attached(Part p, string node)
        {
            AttachNode n = p.FindAttachNode(node);
            return n != null && n.attachedPart != null;
        }

        void CollectRcs(Vessel v)
        {
            foreach (Part p in v.parts)
            {
                if (p.partInfo == null) continue;
                List<ModuleRCS> to = p.partInfo.name == "MSL.CS.REACluster" ? cruiseRcs : p.partInfo.name == "MSL.SC.RCS" ? craneRcs : null;
                if (to != null) to.AddRange(p.FindModulesImplementing<ModuleRCS>());
            }
        }

        static bool AnyOn(List<ModuleRCS> l)
        {
            foreach (ModuleRCS r in l) if (r != null && r.rcsEnabled) return true;
            return false;
        }

        void RcsToggle(List<ModuleRCS> l, string key)
        {
            if (l.Count == 0) return;
            bool on = AnyOn(l);
            bool set = GUILayout.Toggle(on, " " + T(key), GUILayout.Width((Width - 28) / 2));
            if (set != on) foreach (ModuleRCS r in l) if (r != null) r.rcsEnabled = set;
        }

        void Sample()
        {
            active = FlightGlobals.ActiveVessel;
            seq = null; guidance = null; fallbackChute = null; realChute = null;
            descent = bridle = rig = null; descentVessel = roverVessel = null;
            if (active == null) { step = -1; return; }
            double now = Planetarium.GetUniversalTime();

            seq = (ModuleMSLEDLSequencer)Find(active, "ModuleMSLEDLSequencer");
            guidance = (ModuleMSLEntryGuidance)Find(active, "ModuleMSLEntryGuidance");
            descent = FindNear("ModuleMSLPoweredDescent", out descentVessel);
            if (descent != null) bridle = MSL.Modules.Named(descent.part, "ModuleMSLBridle");
            rig = FindNear("ModuleCuriosityRig", out roverVessel);
            cruiseOn = HasPart(active, "MSL.CruiseStage");
            cruiseRcs.Clear(); craneRcs.Clear();
            CollectRcs(active);
            if (descentVessel != null && descentVessel != active) CollectRcs(descentVessel);
            descentPhase = descent != null ? Str(descent, "phase") : "";
            inAir = active.atmDensity > 0 && active.dynamicPressurekPa >= .01;
            propAmount = propMax = 0;
            if (descent != null) Propellant(descent.part, out propAmount, out propMax);
            ec = ecMax = 0;
            if (roverVessel != null) roverVessel.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out ec, out ecMax);

            chuteOut = false; shieldOn = payloadOn = false;
            backshellSep = null;
            if (seq != null)
            {
                Part chutePart = MSL.Modules.ChutePart(seq.part);
                fallbackChute = chutePart != null ? chutePart.FindModuleImplementing<ModuleMSLParachute>() : null;
                realChute = MSL.Modules.Named(chutePart, "RealChuteModule");
                chuteOut = fallbackChute != null ? fallbackChute.oneWasDeployed : Flag(realChute, "oneWasDeployed");
                shieldOn = Attached(seq.part, seq.heatShieldNode);
                payloadOn = Attached(seq.part, seq.payloadNode);
                foreach (ModuleDecouple d in seq.part.FindModulesImplementing<ModuleDecouple>())
                    if (d.explosiveNodeID == seq.payloadNode) backshellSep = d;
            }
            if (guidance != null) { targetLat = guidance.targetLatitude; targetLon = guidance.targetLongitude; }

            // what has been reached, from the vehicle as it is now
            bool phaseLater = descentPhase == "POWERED" || descentPhase == "SKYCRANE" || descentPhase == "FLYAWAY" || descentPhase == "DONE";
            bool roverDown = roverVessel != null && roverVessel.LandedOrSplashed && (descent == null || descentVessel != roverVessel);
            bool anyMSL = seq != null || descent != null || rig != null || cruiseOn;
            if (!anyMSL) { step = -1; return; }
            bool inAtmosphere = active.mainBody.atmosphere && active.altitude < active.mainBody.atmosphereDepth;
            if (roverVessel != null && !roverVessel.LandedOrSplashed) roverSeenFlying = true;

            Array.Clear(reached, 0, reached.Length);
            reached[Cruise] = true;
            reached[Touchdown] = roverDown && (phaseLater || seq == null);
            reached[Flyaway] = reached[Touchdown] || descentPhase == "FLYAWAY" || descentPhase == "DONE";
            reached[SkyCrane] = reached[Flyaway] || descentPhase == "SKYCRANE";
            reached[Powered] = reached[SkyCrane] || descentPhase == "POWERED";
            reached[Backshell] = reached[Powered] || (seq != null && !payloadOn) || (descent != null && seq == null && !cruiseOn);
            reached[HeatShield] = reached[Backshell] || (seq != null && chuteOut && !shieldOn);
            reached[Parachute] = reached[HeatShield] || chuteOut;
            reached[Entry] = reached[Parachute] || (!cruiseOn && inAtmosphere);
            step = 0;
            for (int i = 0; i < reached.Length; i++) if (reached[i]) step = i;

            // event times: persisted where the modules keep them, else when first seen here
            if (!cruiseOn && seq != null && active.altitude > active.mainBody.atmosphereDepth) wasAboveAtmosphere = true;
            if (reached[Entry] && at[Entry] < 0 && wasAboveAtmosphere) at[Entry] = now;
            if (seq != null && seq.mortarUT >= 0) at[Parachute] = seq.mortarUT;
            if (seq != null && seq.heatShieldUT >= 0 && !shieldOn) at[HeatShield] = seq.heatShieldUT;
            if (descent != null)
            {
                double phaseUT = Num(descent, "phaseUT");
                int k = descentPhase == "POWERED" ? Powered : descentPhase == "SKYCRANE" ? SkyCrane : descentPhase == "FLYAWAY" ? Flyaway : -1;
                if (k >= 0 && at[k] < 0 && phaseUT > 0) at[k] = phaseUT;
            }
            if (reached[Backshell] && at[Backshell] < 0 && reached[HeatShield] && !reached[Powered] && at[HeatShield] >= 0) at[Backshell] = now;
            if (at[Backshell] < 0 && at[Powered] >= 0 && descent != null) at[Backshell] = at[Powered] - Num(descent, "ignitionDelay");
            if (reached[Touchdown] && at[Touchdown] < 0 && roverSeenFlying) at[Touchdown] = now;

            SampleRates(now);
            Countdown(now);
        }

        // the game's Mach (FAR's when installed), else from the speed of sound; 0 = no air data (as the sequencer)
        static double MachOf(Vessel v)
        {
            if (v.atmDensity <= 0) return 0;
            if (v.mach > 0) return v.mach;
            return v.speedOfSound > 1 ? v.srfSpeed / v.speedOfSound : 0;
        }

        // deceleration (m/s per s) and Mach drop rate (per s) over game time, smoothed over about a second; a pause
        // keeps them, a gap (load, vessel switch, rails warp) starts them again
        void SampleRates(double now)
        {
            double mach = MachOf(active), dt = now - rateUT;
            if (dt == 0) return;
            if (rateUT < 0 || dt < 0 || dt > 5 || mach <= 0 || active.packed)
            {
                decel = machDrop = double.NaN;
            }
            else
            {
                double k = Math.Min(1, dt / 1.0);
                double d = (rateSpeed - active.srfSpeed) / dt, m = rateMach > 0 ? (rateMach - mach) / dt : double.NaN;
                decel = double.IsNaN(decel) ? d : decel + (d - decel) * k;
                machDrop = double.IsNaN(m) ? m : double.IsNaN(machDrop) ? m : machDrop + (m - machDrop) * k;
            }
            rateUT = now; rateSpeed = active.srfSpeed; rateMach = mach;
        }

        // seconds until x falls to limit at rate per second: 0 when there, infinite while not falling
        static double Until(double x, double limit, double rate)
        {
            if (x <= limit) return 0;
            return rate > 1e-6 ? (x - limit) / rate : double.PositiveInfinity;
        }

        // time to the next event the sequencer fires itself (auto EDL on, payload still in the backshell)
        void Countdown(double now)
        {
            countdownStep = -1;
            if (seq == null || !seq.autoEDL || !payloadOn || active.LandedOrSplashed) return;
            double mach = MachOf(active), t;
            int k;
            if (!chuteOut)
            {
                // mortar: (v <= mortarSpeed and Mach <= mortarMachMax) or Mach <= mortarMachMin, once entry was seen
                if (seq.mortarUT >= 0 || !seq.entered || mach <= 0 || !(decel > .5)) return;
                double tv = Until(active.srfSpeed, seq.mortarSpeed, decel);
                double tHi = Until(mach, seq.mortarMachMax, machDrop), tLo = Until(mach, seq.mortarMachMin, machDrop);
                t = Math.Min(Math.Max(tv, tHi), tLo);
                k = Parachute;
            }
            else if (shieldOn)
            {
                // heat shield: heatShieldDelay s after the mortar and Mach < heatShieldMach
                if (seq.mortarUT < 0 || mach <= 0) return;
                t = Math.Max(seq.heatShieldDelay - (now - seq.mortarUT), Until(mach, seq.heatShieldMach, machDrop));
                k = HeatShield;
            }
            else
            {
                // backshell: backshellDelay s after the heat shield and radar altitude <= backshellAgl
                double delay = seq.heatShieldUT >= 0 ? seq.backshellDelay - (now - seq.heatShieldUT) : 0;
                t = Math.Max(delay, Until(active.radarAltitude, seq.backshellAgl, -active.verticalSpeed));
                k = Backshell;
            }
            if (double.IsNaN(t) || double.IsInfinity(t) || t > 3600) return;
            countdownStep = k;
            countdown = Math.Max(0, t);
        }

        string CountdownText()
        {
            double t = Math.Ceiling(countdown);
            return t < 60 ? "T-" + t.ToString("F0") + " s" : "T-" + Math.Floor(t / 60).ToString("F0") + ":" + (t % 60).ToString("00");
        }

        void Update()
        {
            if (!visible || uiHidden || !HighLogic.LoadedSceneIsFlight) return;
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + SamplePeriod;
            Sample();
        }

        // the descent section matters from backshell separation (or once the landing is armed / the stage flies on
        // its own), the rover section from the sky crane lowering; before that they fold to one header line
        bool DescentMatters()
        {
            return reached[Backshell] || (descentPhase != "" && descentPhase != "OFF") || (descentVessel != null && descentVessel != active);
        }
        bool RoverMatters() { return reached[SkyCrane] || (roverVessel != null && roverVessel != active); }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            if (!visible || uiHidden || !HighLogic.LoadedSceneIsFlight) { ReleaseTyping(); return; }
            // scaled drawing: rects and the mouse below are in the scaled GUI space
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            try { DrawWindow(); }
            finally { GUI.matrix = oldMatrix; }
        }

        void DrawWindow()
        {
            // a click outside the window or Enter ends typing a target (IMGUI keeps the focus otherwise)
            Event ev = Event.current;
            if ((ev.type == EventType.MouseDown && !window.Contains(ev.mousePosition))
                || (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) && typingLocked))
                GUIUtility.keyboardControl = 0;
            GUI.skin = HighLogic.Skin;
            if (head == null) Styles();
            window = GUILayout.Window(windowId, window, Draw, T("#MSL_Aeroshell_UI_Win_title"), GUILayout.Width(Width));
            // GUILayout.Window only grows: when rows went away, size it to the content again next frame
            if (contentBottom > 0 && window.height > contentBottom + HighLogic.Skin.window.padding.bottom + 10) window.height = 0;
            float sw = Screen.width / scale, sh = Screen.height / scale;
            window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, sw - window.width));
            window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, sh - 40));
            if (Event.current.type == EventType.Repaint) LockWhileTyping();
        }

        void Styles()
        {
            head = new GUIStyle(HighLogic.Skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            label = new GUIStyle(HighLogic.Skin.label) { wordWrap = false };
            label.normal.textColor = new Color(.78f, .8f, .82f);
            value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            value.normal.textColor = Color.white;
            done = new GUIStyle(label);
            done.normal.textColor = new Color(.55f, .85f, .55f);
            current = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            current.normal.textColor = new Color(1f, .82f, .3f);
            pending = new GUIStyle(label);
            pending.normal.textColor = new Color(.6f, .6f, .6f);
            small = new GUIStyle(label) { wordWrap = true, fontSize = label.fontSize - 1 };
            note = new GUIStyle(small);
            note.normal.textColor = new Color(.55f, .85f, 1f);
            noteRight = new GUIStyle(note) { wordWrap = false, alignment = TextAnchor.MiddleRight };
            barDone = Solid(new Color(.45f, .75f, .45f)); barCurrent = Solid(new Color(1f, .82f, .3f)); barPending = Solid(new Color(.3f, .3f, .3f));
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
        }

        void Draw(int id)
        {
            if (step < 0 || active == null)
            {
                GUILayout.Label(T("#MSL_Aeroshell_UI_Win_none"), small);
            }
            else
            {
                DrawSteps();
                if (Section(ref showFlight, true, "#MSL_Aeroshell_UI_Win_sec_flight", Distance(active.altitude))) DrawFlight();
                if (guidance != null && !reached[Parachute]
                    && Section(ref showGuidance, true, "#MSL_Aeroshell_UI_grp_Guidance", guidance.guidanceEnabled ? guidance.rangeKm.ToString("F1") + " km" : ""))
                    DrawGuidance();
                if (descent != null && Section(ref showDescent, DescentMatters(), "#MSL_Aeroshell_UI_Win_sec_descent", Str(descent, "status"))) DrawDescent();
                if (rig != null && Section(ref showRover, RoverMatters(), "#MSL_Aeroshell_UI_Win_sec_rover", Str(rig, "status"))) DrawRover();
            }
            Section(ref showSettings, "#MSL_Aeroshell_UI_Win_sec_settings", showSettings ? "" : "   " + (scale * 100).ToString("F0") + " %");
            if (showSettings) DrawSettings();
            GUILayout.Space(0);
            if (Event.current.type == EventType.Repaint) contentBottom = GUILayoutUtility.GetLastRect().yMax;
            GUI.DragWindow();
        }

        // window size in steps (buttons, not a slider: the window rescales under the mouse while dragging)
        void DrawSettings()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("#MSL_Aeroshell_UI_Win_ui_scale"), label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("-", GUILayout.Width(28))) { SetScale(scale - ScaleStep, true); Relayout(); }
            GUILayout.Label((scale * 100).ToString("F0") + " %", value, GUILayout.Width(52));
            if (GUILayout.Button("+", GUILayout.Width(28))) { SetScale(scale + ScaleStep, true); Relayout(); }
            if (GUILayout.Button(T("#MSL_Aeroshell_UI_Win_reset"))) { SetScale(1f, true); Relayout(); }
            GUILayout.EndHorizontal();
        }

        // A click that changes what the window draws ends this GUI event, otherwise the Layout pass throws
        // "Getting control N's position..."; the next frame lays out the new state.
        static void Relayout() { GUIUtility.ExitGUI(); }

        void Section(ref bool open, string key, string summary)
        {
            GUILayout.Space(3);
            if (GUILayout.Button((open ? "[-] " : "[+] ") + T(key) + summary, head)) { open = !open; Relayout(); }
        }
        // open by default while it matters; a click keeps the player's choice; folded, the header carries a summary
        bool Section(ref bool? open, bool matters, string key, string summary)
        {
            bool shown = open ?? matters;
            string text = (shown ? "[-] " : "[+] ") + T(key);
            if (!shown && summary != "")
            {
                string with = text + "  ·  " + summary;
                if (head.CalcSize(new GUIContent(with)).x <= Width - 28) text = with;
            }
            GUILayout.Space(3);
            if (GUILayout.Button(text, head)) { open = !shown; Relayout(); }
            return shown;
        }

        // two short label/value pairs side by side; one per row when either does not fit its half
        void Pair(string k1, string v1, string k2, string v2)
        {
            float half = (Width - 28) / 2;
            if (Fits(T(k1), v1, half) && Fits(T(k2), v2, half))
            {
                GUILayout.BeginHorizontal();
                Cell(T(k1), v1, half);
                GUILayout.Space(8);
                Cell(T(k2), v2, half);
                GUILayout.EndHorizontal();
            }
            else { RowKey(k1, v1); RowKey(k2, v2); }
        }
        bool Fits(string name, string text, float w)
        {
            return label.CalcSize(new GUIContent(name)).x + value.CalcSize(new GUIContent(text)).x + 8 <= w;
        }
        void Cell(string name, string text, float w)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(w));
            GUILayout.Label(name, label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, value);
            GUILayout.EndHorizontal();
        }

        void Row(string name, string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, value);
            GUILayout.EndHorizontal();
        }
        void RowKey(string key, string text) { Row(T(key), text); }

        // the part-menu button, while the part menu shows it
        bool EventButton(PartModule m, string name)
        {
            if (m == null) return false;
            BaseEvent e = m.Events[name];
            if (e == null || !e.guiActive || !e.active) return false;
            if (GUILayout.Button(e.guiName)) { e.Invoke(); Relayout(); }
            return true;
        }

        string When(int i)
        {
            if (at[i] < 0) return "";
            if (at[Entry] >= 0 && i > Entry) return "EI+" + KSPUtil.PrintTimeCompact(Math.Max(0, at[i] - at[Entry]), false);
            return KSPUtil.PrintDateCompact(at[i], true, true);
        }

        // EDL progress: a segment bar, the current step and the next one; the full timed list folds out
        void DrawSteps()
        {
            int n = stepKeys.Length;
            Section(ref showSteps, "#MSL_Aeroshell_UI_Win_sec_edl", "   " + (step + 1) + " / " + n);
            Rect bar = GUILayoutUtility.GetRect(Width - 28, 8, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                float gap = 3, w = (bar.width - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                    GUI.DrawTexture(new Rect(bar.x + i * (w + gap), bar.y + 1, w, 6), i < step ? barDone : i == step ? barCurrent : barPending);
            }
            if (showSteps)
            {
                for (int i = 0; i < n; i++)
                {
                    GUIStyle s = i < step ? done : i == step ? current : pending;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((i < step ? "+  " : i == step ? ">  " : "-  ") + T(stepKeys[i]), s);
                    GUILayout.FlexibleSpace();
                    if (reached[i]) GUILayout.Label(When(i), s);
                    else if (i == countdownStep) GUILayout.Label(CountdownText(), noteRight);
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                // current step (and its time) left, the next one right, on one line
                string now = ">  " + T(stepKeys[step]), when = When(step);
                if (when != "") now += "   " + when;
                GUILayout.BeginHorizontal();
                GUILayout.Label(now, current);
                GUILayout.FlexibleSpace();
                if (step + 1 < n) GUILayout.Label(T("#MSL_Aeroshell_UI_Win_next_step") + "  " + T(stepKeys[step + 1]), pending);
                GUILayout.EndHorizontal();
            }
            // auto EDL and what the sequence waits for next, on one line when it fits
            if (seq != null && payloadOn)
            {
                string autoText = " " + T("#MSL_Aeroshell_UI_Auto_EDL");
                string waits = seq.autoEDL ? seq.status : "";
                if (waits != "" && countdownStep >= 0) waits += "   " + CountdownText();
                bool oneLine = waits == "" || HighLogic.Skin.toggle.CalcSize(new GUIContent(autoText)).x + note.CalcSize(new GUIContent(waits)).x + 16 <= Width - 28;
                GUILayout.BeginHorizontal();
                bool auto = GUILayout.Toggle(seq.autoEDL, autoText);
                if (auto != seq.autoEDL) { seq.autoEDL = auto; Relayout(); }
                GUILayout.FlexibleSpace();
                if (oneLine && waits != "") GUILayout.Label(waits, noteRight);
                GUILayout.EndHorizontal();
                if (!oneLine) GUILayout.Label(waits, note);
                if (chuteOut && !shieldOn) EventButton(backshellSep, "Decouple");
            }
        }

        void DrawFlight()
        {
            Vessel v = active;
            string g = v.geeForce.ToString("F2") + " g";
            // the altitude is in the stock altimeter: the radar altitude once it says more, beside the g load
            bool near = v.radarAltitude < 50000;
            Pair("#MSL_Aeroshell_UI_Win_speed", v.srfSpeed.ToString("F0") + " m/s", "#MSL_Aeroshell_UI_Win_vertical_speed", v.verticalSpeed.ToString("F0") + " m/s");
            if (near) Pair("#MSL_Aeroshell_UI_Win_agl", Distance(v.radarAltitude), "#MSL_Aeroshell_UI_Win_g_load", g);
            else RowKey("#MSL_Aeroshell_UI_Win_g_load", g);
            // Mach and q once there is air to speak of (not 0.00 kPa at the top of the atmosphere)
            if (inAir)
            {
                double mach = v.mach > 0 ? v.mach : v.speedOfSound > 1 ? v.srfSpeed / v.speedOfSound : 0;
                Pair("#MSL_Aeroshell_UI_Win_mach", mach.ToString("F2"), "#MSL_Aeroshell_UI_Win_dynamic_pressure", v.dynamicPressurekPa.ToString("F2") + " kPa");
            }
            if (cruiseRcs.Count > 0 || craneRcs.Count > 0)
            {
                GUILayout.BeginHorizontal();
                RcsToggle(cruiseRcs, "#MSL_Aeroshell_UI_Win_rcs_cruise");
                RcsToggle(craneRcs, "#MSL_Aeroshell_UI_Win_rcs_crane");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        static string Distance(double m)
        {
            return Math.Abs(m) >= 10000 ? (m / 1000).ToString("F1") + " km" : m.ToString("F0") + " m";
        }


        // The whole entry-guidance control (none of it is in the part menu): target before the
        // guidance starts, enable / disable, manual bank while it flies, the balance-mass jettisons while on board.
        string latText, lonText;
        const string LatField = "mslTargetLat", LonField = "mslTargetLon";
        void DrawGuidance()
        {
            ModuleMSLEntryGuidance g = guidance;
            bool setup = g.panelEntry && !g.guidanceEnabled, flying = g.panelEntry && g.guidanceEnabled;
            // the flight phases (entry attitude pre-bank, range control, heading alignment, manual bank) are not described here;
            // the faults still are
            if ((g.guidanceEnabled || g.status != T("#MSL_Aeroshell_UI_off")) && !RoutinePhase(g.status)) GUILayout.Label(g.status, note);
            if (setup)
            {
                g.targetLatitude = AngleField(LatField, "#MSL_Aeroshell_UI_Target_latitude", g.targetLatitude, 90f, ref latText);
                g.targetLongitude = AngleField(LonField, "#MSL_Aeroshell_UI_Target_longitude", g.targetLongitude, 180f, ref lonText);
            }
            else if (!g.guidanceEnabled) RowKey("#MSL_Aeroshell_UI_Win_target", g.targetLatitude.ToString("F2") + "°, " + g.targetLongitude.ToString("F2") + "°");
            if (g.guidanceEnabled)
            {
                Pair("#MSL_Aeroshell_UI_Win_range", g.rangeKm.ToString("F1") + " km",
                    "#MSL_Aeroshell_UI_Win_predicted_miss", (g.predictedKm - g.rangeKm).ToString("+0.0;-0.0") + " km");
                Pair("#MSL_Aeroshell_UI_Win_bank_command", g.bankCommand.ToString("F0") + "°", "#MSL_Aeroshell_UI_Win_bank", g.measuredBank.ToString("F0") + "°");
            }
            if (flying)
            {
                bool manual = GUILayout.Toggle(g.manualBank, " " + T("#MSL_Aeroshell_UI_Manual_bank"));
                if (manual != g.manualBank) { g.manualBank = manual; Relayout(); }
                if (g.manualBank)
                {
                    Row(T("#MSL_Aeroshell_UI_Manual_bank_angle"), g.manualBankAngle.ToString("F0") + "°");
                    g.manualBankAngle = Mathf.Round(GUILayout.HorizontalSlider(g.manualBankAngle, -85f, 85f));
                }
            }
            GUILayout.BeginHorizontal();
            if (setup && GUILayout.Button(T("#MSL_Aeroshell_UI_Enable_guidance"))) { g.EnableGuidance(); Relayout(); }
            if (g.guidanceEnabled && GUILayout.Button(T("#MSL_Aeroshell_UI_Disable_guidance"))) { g.DisableGuidance(); Relayout(); }
            if (setup && GUILayout.Button(T("#MSL_Aeroshell_UI_Target_nominal_entry_endpoint"))) { g.SetNominalTarget(); latText = lonText = null; Relayout(); }
            GUILayout.EndHorizontal();
            if (g.panelCruiseMasses || g.panelEntryMasses)
            {
                GUILayout.BeginHorizontal();
                if (g.panelCruiseMasses && GUILayout.Button(T("#MSL_Aeroshell_UI_Jettison_two_cruise_balance_masses"))) { g.JettisonCruiseMasses(); Relayout(); }
                if (g.panelEntryMasses && GUILayout.Button(T("#MSL_Aeroshell_UI_Jettison_six_entry_balance_masses"))) { g.JettisonEntryMasses(); Relayout(); }
                GUILayout.EndHorizontal();
            }
        }

        static bool RoutinePhase(string s)
        {
            return s.StartsWith(T("#MSL_Aeroshell_UI_range_control_RCS"), StringComparison.Ordinal)
                || s.StartsWith(T("#MSL_Aeroshell_UI_entry_attitude_pre_bank_RCS"), StringComparison.Ordinal)
                || s.StartsWith(T("#MSL_Aeroshell_UI_heading_alignment_RCS"), StringComparison.Ordinal)
                || s.StartsWith(T("#MSL_Aeroshell_UI_manual_bank_RCS"), StringComparison.Ordinal);
        }

        // A typed angle (degrees): the text follows the value while the field is not being edited; a valid entry is
        // clamped to +-limit and applied as typed.
        float AngleField(string control, string key, float v, float limit, ref string text)
        {
            bool editing = GUI.GetNameOfFocusedControl() == control;
            if (!editing || text == null) text = v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            GUILayout.BeginHorizontal();
            GUILayout.Label(T(key), label);
            GUILayout.FlexibleSpace();
            GUI.SetNextControlName(control);
            text = GUILayout.TextField(text, GUILayout.Width(110));
            GUILayout.Label("°", label);
            GUILayout.EndHorizontal();
            float parsed;
            if (editing && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                v = Mathf.Clamp(parsed, -limit, limit);
            return v;
        }

        // Typing a target must not fly the vessel (WASD, staging on space): lock the controls while a text field has focus.
        const string TypingLock = "MSLStatusWindowTyping";
        bool typingLocked;
        void ReleaseTyping()
        {
            if (!typingLocked) return;
            typingLocked = false; GUIUtility.keyboardControl = 0;
            InputLockManager.RemoveControlLock(TypingLock);
        }
        void LockWhileTyping()
        {
            string f = GUI.GetNameOfFocusedControl();
            bool typing = f == LatField || f == LonField;
            if (typing == typingLocked) return;
            typingLocked = typing;
            if (typing) InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, TypingLock);
            else InputLockManager.RemoveControlLock(TypingLock);
        }

        void DrawDescent()
        {
            bool apart = descentVessel != null && descentVessel != active;
            string prop = propMax > 0 ? (100 * propAmount / propMax).ToString("F0") + " %" : "";
            if (prop != "") Pair("#MSL_Aeroshell_UI_Win_phase", Str(descent, "status"), "#MSL_Aeroshell_UI_Win_propellant", prop);
            else RowKey("#MSL_Aeroshell_UI_Win_phase", Str(descent, "status"));
            // on the active vessel its radar altitude is the flight one; flying apart, where it is
            if (apart)
                Pair("#MSL_Aeroshell_UI_Win_stage_agl", Distance(descentVessel.radarAltitude),
                    "#MSL_Aeroshell_UI_Win_stage_distance", Distance(Vector3d.Distance(descentVessel.CoMD, active.CoMD)));
            if (bridle != null && (Flag(bridle, "lowering") || Num(bridle, "payout") > 0))
            {
                // lowering: length and load; after that its state says it all
                if (Flag(bridle, "lowering"))
                    Pair("#MSL_Aeroshell_UI_Win_bridle_out", Num(bridle, "payout").ToString("F1") + " m",
                        "#MSL_Aeroshell_UI_Win_bridle_tension", Num(bridle, "tension").ToString("F1") + " kN");
                else RowKey("#MSL_Aeroshell_UI_Win_bridle", Str(bridle, "status"));
            }
            GUILayout.BeginHorizontal();
            // auto EDL arms the landing itself at backshell separation
            if (!(seq != null && payloadOn && seq.autoEDL)) EventButton(descent, "ArmAutoLanding");
            EventButton(descent, "CancelAutoLanding");
            GUILayout.EndHorizontal();
        }

        // the descent engines' first propellant, in the descent stage (its part and the parts below it), not the
        // cruise stage's tanks of the same fuel while still attached
        static void Propellant(Part stage, out double amount, out double max)
        {
            amount = max = 0;
            var parts = new List<Part> { stage };
            int id = 0;
            for (int i = 0; i < parts.Count && i < 256; i++)
            {
                if (id == 0)
                    foreach (ModuleEngines e in parts[i].FindModulesImplementing<ModuleEngines>())
                        if (e.propellants.Count > 0) { id = e.propellants[0].id; break; }
                parts.AddRange(parts[i].children);
            }
            if (id == 0) return;
            foreach (Part p in parts)
            {
                PartResource r = p.Resources.Get(id);
                if (r != null) { amount += r.amount; max += r.maxAmount; }
            }
        }

        void DrawRover()
        {
            Vessel v = roverVessel;
            bool stowed = Flag(rig, "stowed"), deploying = Flag(rig, "MobilityDeploying");
            RowKey("#MSL_Aeroshell_UI_Win_rig", Str(rig, "status"));
            string mobility = T(stowed ? (deploying ? "#MSL_Aeroshell_UI_Win_mobility_deploying" : "#MSL_Aeroshell_UI_Win_mobility_stowed")
                : "#MSL_Aeroshell_UI_Win_mobility_deployed");
            if (ecMax > 0) Pair("#MSL_Aeroshell_UI_Win_mobility", mobility, "#MSL_Aeroshell_UI_Win_electric_charge", (100 * ec / ecMax).ToString("F0") + " %");
            else RowKey("#MSL_Aeroshell_UI_Win_mobility", mobility);
            if (v != null && v.LandedOrSplashed)
            {
                string speed = v.srfSpeed.ToString("F2") + " / " + Num(rig, "maxSpeed").ToString("F2") + " m/s";
                if (!float.IsNaN(targetLat))
                {
                    Vector3d t = v.mainBody.GetWorldSurfacePosition(targetLat, targetLon, 0) - v.mainBody.position;
                    Vector3d here = v.CoMD - v.mainBody.position;
                    double angle = Vector3d.Angle(t, here) * Math.PI / 180;
                    Pair("#MSL_Aeroshell_UI_Win_ground_speed", speed, "#MSL_Aeroshell_UI_Win_target_distance", Distance(v.mainBody.Radius * angle));
                }
                else RowKey("#MSL_Aeroshell_UI_Win_ground_speed", speed);
            }
            BaseField brakes = rig.Fields["brakes"];
            if (brakes != null && brakes.guiActive)
            {
                bool on = (bool)brakes.GetValue(rig);
                bool set = GUILayout.Toggle(on, " " + brakes.guiName);
                if (set != on) { brakes.SetValue(set, rig); Relayout(); }
            }
            // deploy only once out of the aeroshell (the sky crane deploys it itself); the rig only in the surface
            // pose (stowed it refuses with a message)
            bool outOfShell = reached[Powered] || (v != null && v.LandedOrSplashed);
            GUILayout.BeginHorizontal();
            if (outOfShell) EventButton(rig, "DeployMobility");
            if (!stowed && !deploying) EventButton(rig, "EnableRig");
            GUILayout.EndHorizontal();
        }
    }
}
