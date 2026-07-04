using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace LoomColorfulReapingKnives
{
    // Standalone visual-only recolor module. Reaper Stance now includes these controls itself,
    // so this sidecar goes inert when Reaper Stance is present.
    public static class Bootstrap
    {
        private static bool s_checkedReaperStance;
        private static bool s_reaperStancePresent;

        public static void Tick()
        {
            try
            {
                if (GameState.IsLoading || PartyMemberAI.PartyMembers == null)
                {
                    return;
                }

                if (ReaperStancePresent())
                {
                    return;
                }

                ReaperFx.EnsureSpawned();
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomColorfulReapingKnives] " + ex);
            }
        }

        private static bool ReaperStancePresent()
        {
            if (!s_checkedReaperStance)
            {
                s_checkedReaperStance = true;
                s_reaperStancePresent =
                    Type.GetType("LoomReapingKnivesModal.Bootstrap, LoomReapingKnivesModal") != null;
            }
            return s_reaperStancePresent;
        }
    }

    // Runtime visual tweaks for Reaping Knives: recolor the forearm blade particles to a
    // user-chosen color. Also hosts a small F9 overlay (separate from other mods' menus)
    // for tuning.
    public class ReaperFx : MonoBehaviour
    {
        private enum Capturing { None, Menu }

        private static ReaperFx s_instance;

        public static void EnsureSpawned()
        {
            if (s_instance != null)
            {
                return;
            }

            try
            {
                GameObject go = new GameObject("LoomReaperStanceFx");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_instance = go.AddComponent<ReaperFx>();
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomColorfulReapingKnives] fx spawn failed: " + ex);
            }
        }

        private bool m_recolor = true;
        private Color m_color = new Color(0.12f, 0.32f, 1f, 1f); // deep blue default
        private float m_brightness = 2f;
        private float m_opacity = 1f;
        private bool m_advanced;
        private bool m_individualColors;

        private KeyCode m_menuKey = KeyCode.F9;
        private bool m_menuOpen;
        private Capturing m_capturing = Capturing.None;
        private bool m_inputDisabledByUs;
        private Rect m_window = new Rect(0f, 60f, 320f, 0f);
        private bool m_windowPlaced;
        private string m_configPath;

        private struct Orig
        {
            public ParticleSystem.MinMaxGradient start;
            public float startAlpha;
            public bool lifetimeEnabled;
            public ParticleSystem.MinMaxGradient lifetime;
            public bool emissionEnabled;
            public bool rendererCaptured;
            public bool rendererEnabled;
            public bool hasTint;
            public Color tint;
            public bool hasColor;
            public Color color;
        }

        private readonly Dictionary<int, Orig> m_orig = new Dictionary<int, Orig>();
        private static readonly string[] s_layers = new string[]
        {
            "blade01",
            "blade01/blade02",
            "blade01/blade03",
            "blade01/mist",
            "blade01/mist_movement",
            "blade01/spark"
        };

        private readonly Dictionary<string, float> m_bladeOpacity = new Dictionary<string, float>();
        private readonly Dictionary<string, Color> m_bladeColor = new Dictionary<string, Color>();

        private void Awake()
        {
            try { m_configPath = Path.Combine(Application.persistentDataPath, "LoomReaperStance.cfg"); }
            catch { m_configPath = "LoomReaperStance.cfg"; }
            EnsureLayerDefaults();
            LoadConfig();
        }

        private void Update()
        {
            if (m_capturing == Capturing.None && SafeKeyInputAvailable()
                && m_menuKey != KeyCode.None && Input.GetKeyDown(m_menuKey))
            {
                SetMenuOpen(!m_menuOpen);
            }

            // Re-assert while open so we coexist with other mod menus that also block input.
            if (m_menuOpen)
            {
                GameInput.DisableInput = true;
            }
        }

        private void LateUpdate()
        {
            if (GameState.IsLoading || PartyMemberAI.PartyMembers == null)
            {
                return;
            }

            // Out of combat: drop colour caches.
            if (!GameState.InCombat)
            {
                m_orig.Clear();
                return;
            }

            try
            {
                for (int i = 0; i < PartyMemberAI.PartyMembers.Length; i++)
                {
                    PartyMemberAI pm = PartyMemberAI.PartyMembers[i];
                    if (pm == null || pm.Secondary)
                    {
                        continue;
                    }
                    ProcessCharacter(pm.gameObject);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomColorfulReapingKnives] fx process: " + ex);
            }
        }

        private void ProcessCharacter(GameObject owner)
        {
            ParticleSystem[] systems = owner.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                return;
            }

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (AncestorContains(ps.transform, "reaping_knives"))
                {
                    EnsureCaptured(ps);
                    if (m_recolor)
                    {
                        ApplyColor(ps);
                    }
                    else
                    {
                        RestoreColor(ps);
                    }
                }
            }
        }

        private void EnsureCaptured(ParticleSystem ps)
        {
            int id = ps.GetInstanceID();
            if (m_orig.ContainsKey(id))
            {
                return;
            }

            Orig o = default(Orig);
            o.start = ps.main.startColor;
            o.startAlpha = StartAlpha(o.start);
            ParticleSystem.ColorOverLifetimeModule lifetime = ps.colorOverLifetime;
            o.lifetimeEnabled = lifetime.enabled;
            o.lifetime = lifetime.color;
            ParticleSystem.EmissionModule emission = ps.emission;
            o.emissionEnabled = emission.enabled;
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            if (r != null)
            {
                o.rendererCaptured = true;
                o.rendererEnabled = r.enabled;
                if (r.material != null)
                {
                    Material mm = r.material;
                    if (mm.HasProperty("_TintColor")) { o.hasTint = true; o.tint = mm.GetColor("_TintColor"); }
                    if (mm.HasProperty("_Color")) { o.hasColor = true; o.color = mm.GetColor("_Color"); }
                }
            }
            m_orig[id] = o;
        }

        private void ApplyColor(ParticleSystem ps)
        {
            Orig o;
            if (!m_orig.TryGetValue(ps.GetInstanceID(), out o))
            {
                return;
            }

            string layer = BladeLayerKey(ps.transform);
            bool blade = IsAllowedLayer(layer);
            float layerOpacity = blade ? GetBladeLayerOpacity(layer) : 0f;
            float effectiveOpacity = m_opacity * layerOpacity;
            bool off = !blade || effectiveOpacity <= 0.001f || m_brightness <= 0.001f;
            Color sourceColor = m_individualColors && blade ? GetBladeLayerColor(layer) : m_color;
            Color rgb = Boost(sourceColor, m_brightness);
            float alpha = o.startAlpha * effectiveOpacity;

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = o.emissionEnabled && !off;
            if (r != null)
            {
                r.enabled = o.rendererEnabled && !off;
            }
            if (off)
            {
                ps.Clear();
            }

            ParticleSystem.MainModule main = ps.main;
            main.startColor = new Color(rgb.r, rgb.g, rgb.b, alpha);

            ParticleSystem.ColorOverLifetimeModule lifetime = ps.colorOverLifetime;
            if (off)
            {
                lifetime.enabled = o.lifetimeEnabled;
                lifetime.color = o.lifetime;
            }
            else if (o.lifetimeEnabled)
            {
                lifetime.enabled = true;
                lifetime.color = ColorizeGradient(o.lifetime, rgb, effectiveOpacity);
            }

            if (r != null && r.material != null)
            {
                Material m = r.material; // per-renderer instance (created once, then cached)
                if (o.hasTint) { m.SetColor("_TintColor", new Color(rgb.r, rgb.g, rgb.b, o.tint.a * effectiveOpacity)); }
                if (o.hasColor) { m.SetColor("_Color", new Color(rgb.r, rgb.g, rgb.b, o.color.a * effectiveOpacity)); }
            }
        }

        private void EnsureLayerDefaults()
        {
            for (int i = 0; i < s_layers.Length; i++)
            {
                string layer = s_layers[i];
                if (!m_bladeOpacity.ContainsKey(layer))
                {
                    m_bladeOpacity[layer] = DefaultLayerOpacity(layer);
                }
                if (!m_bladeColor.ContainsKey(layer))
                {
                    m_bladeColor[layer] = m_color;
                }
            }
        }

        private static float DefaultLayerOpacity(string layer)
        {
            if (layer == "blade01/blade03")
            {
                return 0.8f;
            }
            if (layer == "blade01/mist" || layer == "blade01/mist_movement")
            {
                return 0.6f;
            }
            return 1f;
        }

        private float GetBladeLayerOpacity(string layer)
        {
            float value;
            return m_bladeOpacity.TryGetValue(layer, out value) ? Mathf.Clamp01(value) : DefaultLayerOpacity(layer);
        }

        private Color GetBladeLayerColor(string layer)
        {
            Color value;
            return m_bladeColor.TryGetValue(layer, out value) ? value : m_color;
        }

        private static bool IsAllowedLayer(string layer)
        {
            for (int i = 0; i < s_layers.Length; i++)
            {
                if (string.Equals(s_layers[i], layer, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsInBlade01Tree(Transform t)
        {
            Transform p = t;
            int guard = 0;
            while (p != null && guard < 8)
            {
                if (IsBlade01Self(p))
                {
                    return true;
                }
                p = p.parent;
                guard++;
            }
            return false;
        }

        private static bool IsBlade01Self(Transform t)
        {
            return t != null && string.Equals(CleanLayerName(t.name), "blade01", StringComparison.OrdinalIgnoreCase);
        }

        private static string BladeLayerKey(Transform t)
        {
            if (t == null)
            {
                return "unknown";
            }
            if (IsBlade01Self(t))
            {
                return "blade01";
            }
            return IsInBlade01Tree(t) ? ("blade01/" + CleanLayerName(t.name)) : CleanLayerName(t.name);
        }

        private static string CleanLayerName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unnamed";
            }
            return name.Replace("(Clone)", "").Trim();
        }

        private static Color Boost(Color color, float intensity)
        {
            return new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a);
        }

        private static ParticleSystem.MinMaxGradient ColorizeGradient(ParticleSystem.MinMaxGradient source, Color rgb, float alphaScale)
        {
            switch (source.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(TintColor(source.color, rgb, alphaScale));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(
                        TintColor(source.colorMin, rgb, alphaScale),
                        TintColor(source.colorMax, rgb, alphaScale));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(TintGradient(source.gradient, rgb, alphaScale));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(
                        TintGradient(source.gradientMin, rgb, alphaScale),
                        TintGradient(source.gradientMax, rgb, alphaScale));
                default:
                    return source;
            }
        }

        private static Color TintColor(Color original, Color rgb, float alphaScale)
        {
            return new Color(rgb.r, rgb.g, rgb.b, original.a * alphaScale);
        }

        private static Gradient TintGradient(Gradient original, Color rgb, float alphaScale)
        {
            Gradient gradient = new Gradient();
            GradientColorKey[] colors = original.colorKeys;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i].color = new Color(rgb.r, rgb.g, rgb.b, 1f);
            }

            GradientAlphaKey[] alphas = original.alphaKeys;
            for (int i = 0; i < alphas.Length; i++)
            {
                alphas[i].alpha *= alphaScale;
            }

            gradient.SetKeys(colors, alphas);
            return gradient;
        }

        private static float StartAlpha(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return g.color.a;
                case ParticleSystemGradientMode.TwoColors:
                    return Mathf.Min(g.colorMin.a, g.colorMax.a);
                default:
                    return 1f;
            }
        }

        private void RestoreColor(ParticleSystem ps)
        {
            Orig o;
            if (!m_orig.TryGetValue(ps.GetInstanceID(), out o))
            {
                return;
            }

            ParticleSystem.MainModule main = ps.main;
            main.startColor = o.start;

            ParticleSystem.ColorOverLifetimeModule lifetime = ps.colorOverLifetime;
            lifetime.enabled = o.lifetimeEnabled;
            lifetime.color = o.lifetime;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = o.emissionEnabled;

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            if (r != null && o.rendererCaptured)
            {
                r.enabled = o.rendererEnabled;
            }
            if (r != null && r.material != null)
            {
                Material m = r.material;
                if (o.hasTint) { m.SetColor("_TintColor", o.tint); }
                if (o.hasColor) { m.SetColor("_Color", o.color); }
            }
        }

        private static bool AncestorContains(Transform t, string token)
        {
            Transform p = t;
            int guard = 0;
            while (p != null && guard < 16)
            {
                if (p.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                p = p.parent;
                guard++;
            }
            return false;
        }

        private void OnGUI()
        {
            if (m_capturing != Capturing.None)
            {
                HandleCaptureEvent();
            }

            if (!m_menuOpen)
            {
                return;
            }

            if (!m_windowPlaced)
            {
                m_window.x = Screen.width - m_window.width - 30f; // right side, away from F10 menu
                m_windowPlaced = true;
            }

            m_window = GUILayout.Window(0x52454150, m_window, DrawWindow, "Reaper Stance");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(4f);
            m_recolor = GUILayout.Toggle(m_recolor, " Recolor Reaping Knives blades");

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Color:", GUILayout.Width(44f));
            Color prev = GUI.color;
            GUI.color = m_color;
            GUILayout.Box("            ");
            GUI.color = prev;
            GUILayout.EndHorizontal();

            m_color.r = ColorRow("R", m_color.r);
            m_color.g = ColorRow("G", m_color.g);
            m_color.b = ColorRow("B", m_color.b);
            m_opacity = SliderRow("Opacity", m_opacity, 0f, 1f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Deep Blue")) { m_color = new Color(0.12f, 0.32f, 1f, 1f); }
            if (GUILayout.Button("Purple")) { m_color = new Color(0.7f, 0.2f, 1f, 1f); }
            if (GUILayout.Button("Cyan")) { m_color = new Color(0.1f, 0.9f, 1f, 1f); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Green")) { m_color = new Color(0.2f, 1f, 0.3f, 1f); }
            if (GUILayout.Button("Red")) { m_color = new Color(1f, 0.2f, 0.15f, 1f); }
            if (GUILayout.Button("White")) { m_color = new Color(1f, 1f, 1f, 1f); }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            m_advanced = GUILayout.Toggle(m_advanced, " Advanced");
            if (m_advanced)
            {
                GUILayout.Space(4f);
                m_individualColors = GUILayout.Toggle(m_individualColors, " Individually color components");

                for (int i = 0; i < s_layers.Length; i++)
                {
                    string layer = s_layers[i];
                    GUILayout.Space(4f);
                    m_bladeOpacity[layer] = SliderRow(layer, GetBladeLayerOpacity(layer), 0f, 1f);
                    if (m_individualColors)
                    {
                        Color c = GetBladeLayerColor(layer);
                        c.r = ColorRow("  R", c.r);
                        c.g = ColorRow("  G", c.g);
                        c.b = ColorRow("  B", c.b);
                        m_bladeColor[layer] = c;
                    }
                }
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Open this menu:", GUILayout.Width(120f));
            string mk = (m_capturing == Capturing.Menu) ? "press a key..." : m_menuKey.ToString();
            if (GUILayout.Button(mk)) { m_capturing = Capturing.Menu; }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            if (GUILayout.Button("Close"))
            {
                SetMenuOpen(false);
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private static float ColorRow(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(16f));
            float v = GUILayout.HorizontalSlider(value, 0f, 1f);
            GUILayout.Label(Mathf.RoundToInt(v * 255f).ToString(), GUILayout.Width(32f));
            GUILayout.EndHorizontal();
            return v;
        }

        private static float SliderRow(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(112f));
            float v = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(v.ToString("0.00", CultureInfo.InvariantCulture), GUILayout.Width(38f));
            GUILayout.EndHorizontal();
            return v;
        }

        private void HandleCaptureEvent()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
            {
                return;
            }
            KeyCode k = e.keyCode;
            if (k == KeyCode.None)
            {
                return;
            }
            if (k != KeyCode.Escape && m_capturing == Capturing.Menu)
            {
                m_menuKey = k;
            }
            m_capturing = Capturing.None;
            e.Use();
            SaveConfig();
        }

        private void SetMenuOpen(bool open)
        {
            if (open == m_menuOpen)
            {
                return;
            }
            m_menuOpen = open;
            if (open)
            {
                if (!GameInput.DisableInput)
                {
                    GameInput.DisableInput = true;
                    m_inputDisabledByUs = true;
                }
            }
            else
            {
                m_capturing = Capturing.None;
                if (m_inputDisabledByUs)
                {
                    GameInput.DisableInput = false;
                    m_inputDisabledByUs = false;
                }
                SaveConfig();
            }
        }

        private static bool SafeKeyInputAvailable()
        {
            try { return UIWindowManager.KeyInputAvailable; }
            catch { return true; }
        }

        private void LoadConfig()
        {
            try
            {
                if (m_configPath == null || !File.Exists(m_configPath))
                {
                    return;
                }
                foreach (string raw in File.ReadAllLines(m_configPath))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "recolor": m_recolor = ParseBool(val, m_recolor); break;
                        case "colorR": m_color.r = ParseFloat(val, m_color.r); break;
                        case "colorG": m_color.g = ParseFloat(val, m_color.g); break;
                        case "colorB": m_color.b = ParseFloat(val, m_color.b); break;
                        case "coreColorR": m_color.r = ParseFloat(val, m_color.r); break;
                        case "coreColorG": m_color.g = ParseFloat(val, m_color.g); break;
                        case "coreColorB": m_color.b = ParseFloat(val, m_color.b); break;
                        case "brightness":
                        case "coreIntensity":
                            m_brightness = ParseFloatRange(val, m_brightness, 0f, 4f);
                            break;
                        case "opacity":
                            m_opacity = ParseFloat(val, m_opacity);
                            break;
                        case "advanced": m_advanced = ParseBool(val, m_advanced); break;
                        case "individualColors": m_individualColors = ParseBool(val, m_individualColors); break;
                        case "menuKey": m_menuKey = ParseKey(val, m_menuKey); break;
                    }
                    if (key.StartsWith("bladeOpacity.", StringComparison.Ordinal))
                    {
                        string layer = Uri.UnescapeDataString(key.Substring("bladeOpacity.".Length));
                        if (IsAllowedLayer(layer))
                        {
                            m_bladeOpacity[layer] = ParseFloat(val, DefaultLayerOpacity(layer));
                        }
                    }
                    if (key.StartsWith("bladeColor.", StringComparison.Ordinal))
                    {
                        string[] parts = key.Split('.');
                        if (parts.Length == 3)
                        {
                            string layer = Uri.UnescapeDataString(parts[1]);
                            if (IsAllowedLayer(layer))
                            {
                                Color color = GetBladeLayerColor(layer);
                                if (parts[2] == "r") { color.r = ParseFloat(val, color.r); }
                                else if (parts[2] == "g") { color.g = ParseFloat(val, color.g); }
                                else if (parts[2] == "b") { color.b = ParseFloat(val, color.b); }
                                m_bladeColor[layer] = color;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomColorfulReapingKnives] fx load config: " + ex);
            }
        }

        private void SaveConfig()
        {
            try
            {
                if (m_configPath == null)
                {
                    return;
                }
                List<string> lines = new List<string>();
                lines.Add("# Loom Reaper Stance visual settings");
                lines.Add("recolor=" + (m_recolor ? "1" : "0"));
                lines.Add("colorR=" + m_color.r.ToString("0.###", CultureInfo.InvariantCulture));
                lines.Add("colorG=" + m_color.g.ToString("0.###", CultureInfo.InvariantCulture));
                lines.Add("colorB=" + m_color.b.ToString("0.###", CultureInfo.InvariantCulture));
                lines.Add("brightness=" + m_brightness.ToString("0.###", CultureInfo.InvariantCulture));
                lines.Add("opacity=" + m_opacity.ToString("0.###", CultureInfo.InvariantCulture));
                lines.Add("advanced=" + (m_advanced ? "1" : "0"));
                lines.Add("individualColors=" + (m_individualColors ? "1" : "0"));
                for (int i = 0; i < s_layers.Length; i++)
                {
                    string layer = s_layers[i];
                    string escaped = Uri.EscapeDataString(layer);
                    lines.Add("bladeOpacity." + escaped + "="
                        + GetBladeLayerOpacity(layer).ToString("0.###", CultureInfo.InvariantCulture));
                    Color color = GetBladeLayerColor(layer);
                    lines.Add("bladeColor." + escaped + ".r=" + color.r.ToString("0.###", CultureInfo.InvariantCulture));
                    lines.Add("bladeColor." + escaped + ".g=" + color.g.ToString("0.###", CultureInfo.InvariantCulture));
                    lines.Add("bladeColor." + escaped + ".b=" + color.b.ToString("0.###", CultureInfo.InvariantCulture));
                }
                lines.Add("menuKey=" + m_menuKey);
                File.WriteAllLines(m_configPath, lines.ToArray());
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoomColorfulReapingKnives] fx save config: " + ex);
            }
        }

        private static bool ParseBool(string v, bool fallback)
        {
            if (v == "1" || v.ToLowerInvariant() == "true") { return true; }
            if (v == "0" || v.ToLowerInvariant() == "false") { return false; }
            return fallback;
        }

        private static float ParseFloat(string v, float fallback)
        {
            float f;
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { return Mathf.Clamp01(f); }
            return fallback;
        }

        private static float ParseFloatRange(string v, float fallback, float min, float max)
        {
            float f;
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { return Mathf.Clamp(f, min, max); }
            return fallback;
        }

        private static KeyCode ParseKey(string s, KeyCode fallback)
        {
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), s, true); }
            catch { return fallback; }
        }
    }
}
