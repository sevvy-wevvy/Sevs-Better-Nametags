using BepInEx;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace SevsBetterNametags
{
    [BepInPlugin("com.sev.gorillatag.better-nametags", "Sevs Better Nametags", SevsBetterNametags.Config.CurrentModVersion)]
    public class Plugin : BaseUnityPlugin, IInRoomCallbacks, IMatchmakingCallbacks
    {
        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        private static bool _skipVersionCheck = false;

        internal static readonly Dictionary<string, Color> UserColors = new Dictionary<string, Color>();
        internal static readonly Dictionary<string, string> UserLabels = new Dictionary<string, string>();

        private const string ApiUrl = "https://sevvy-wevvy.com/mods/sev-verified-nametags/api.php";

        internal const string KEY_NAME = "BetterNametagsCN";
        internal const string KEY_PRONOUNS = "BetterNametagsPR";
        internal const string KEY_PRONCOLOR = "BetterNametagsPC";
        internal const string KEY_FONT = "BetterNametagsFO";
        private const string KEY_R1 = "BetterNametagsR1";
        private const string KEY_G1 = "BetterNametagsG1";
        private const string KEY_B1 = "BetterNametagsB1";
        private const string KEY_R2 = "BetterNametagsR2";
        private const string KEY_G2 = "BetterNametagsG2";
        private const string KEY_B2 = "BetterNametagsB2";
        private const string KEY_GRAD = "BetterNametagsGrad";
        private const string KEY_PRON_WO = "BetterNametagsPWO";
        private const string KEY_PRON_BO = "BetterNametagsPBO";

        private bool _guiVisible;
        private bool _debugMode;

        private string _inputName = "";
        private string _inputPronouns = "";
        private int _r1 = 9, _g1 = 9, _b1 = 9;
        private int _r2 = 0, _g2 = 9, _b2 = 9;
        private bool _useGradient;

        internal static float PronounWorldOffset = -0.0309f;
        internal static float PronounBoardOffset = -5f;

        private Texture2D _swatch1;
        private Texture2D _swatch2;

        internal int _fontIdx;
        internal readonly List<TMP_FontAsset> Fonts = new List<TMP_FontAsset>();
        internal string[] FontNames = new string[0];
        private static readonly Dictionary<int, TMP_FontAsset> _defaultFonts = new Dictionary<int, TMP_FontAsset>();

        internal static readonly Dictionary<int, (TMP_Text nameTmp, TMP_Text pronTmp)> RigTmps =
            new Dictionary<int, (TMP_Text, TMP_Text)>();

        private string _savedFontName = "";

        private string _debugWOStr = "-0.0309";
        private string _debugBOStr = "-5.0000";
        private float _lastPronounWorldOffset;
        private float _lastPronounBoardOffset;

        private float _scrollY;
        private float _totalContentH = 1000f;
        private bool _draggingScrollbar;
        private float _dragStartMouseY;
        private float _dragStartScrollY;

        private GUIStyle _stylePanelBg, _styleTitleBg, _styleSection, _styleDivider,
                         _styleLabel, _styleHeaderLabel, _styleSectionHeader,
                         _styleField, _styleBtn, _styleBtnRed, _styleToggle,
                         _styleSwatch, _styleScrollTrack, _styleScrollThumb, _styleCloseBtn,
                         _stylePreviewBox, _stylePreviewName, _stylePreviewPronoun, _styleSaveBtn;
        private bool _stylesReady;

        private Harmony _harmony;
        private bool _userIdWritten;
        private float _applyOnLoadTimer = -1f;

        private const float PanelW = 320f;
        private const float SbW = 10f;
        private const float TitleH = 48f;
        private const float ContentX = SbW + 12f;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            StartCoroutine(CheckVersionThenStart());
        }

        private IEnumerator CheckVersionThenStart()
        {
            if (!_skipVersionCheck)
            {
                var url = SevsBetterNametags.Config.ModVersionTxt + "?t=" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.timeout = 30;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        string latest = req.downloadHandler.text.Trim();
                        if (latest != SevsBetterNametags.Config.CurrentModVersion)
                        {
                            Logger.LogInfo($"[Sevs Better Nametags] Update available: {SevsBetterNametags.Config.CurrentModVersion} -> {latest}. Updating...");
                            yield return PerformSelfUpdate();
                            yield break;
                        }
                    }
                    else
                    {
                        Logger.LogInfo("[Sevs Better Nametags] Version check skipped: " + req.error);
                    }
                }
            }

            _harmony = new Harmony("com.sev.gorillatag.better-bametags");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            StartCoroutine(FetchColorsLoop());
            GorillaTagger.OnPlayerSpawned(() => Plugin.Instance.ScanFonts());

            LoadSettings();
            _swatch1 = MakeTex(1, 1, new Color(_r1 / 9f, _g1 / 9f, _b1 / 9f));
            _swatch2 = MakeTex(1, 1, new Color(_r2 / 9f, _g2 / 9f, _b2 / 9f));

            gameObject.AddComponent<SevsBetterNametags.Api>();

            Logger.LogInfo("[Sevs Better Nametags] Loaded!");
        }

        private IEnumerator PerformSelfUpdate()
        {
            string selfPath = Assembly.GetExecutingAssembly().Location;
            string deletePath = selfPath + ".delete";

            try
            {
                if (File.Exists(deletePath)) File.Delete(deletePath);
                File.Move(selfPath, deletePath);
            }
            catch (Exception e)
            {
                Logger.LogError("[Sevs Better Nametags] Self-update rename failed: " + e.Message);
                yield break;
            }

            using (UnityWebRequest req = UnityWebRequest.Get(SevsBetterNametags.Config.ModDownload + "?t=" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Logger.LogError("[Sevs Better Nametags] Self-update download failed: " + req.error);
                    try { File.Move(deletePath, selfPath); } catch { }
                    yield break;
                }

                try
                {
                    File.WriteAllBytes(selfPath, req.downloadHandler.data);
                }
                catch (Exception e)
                {
                    Logger.LogError("[Sevs Better Nametags] Self-update write failed: " + e.Message);
                    try { File.Move(deletePath, selfPath); } catch { }
                    yield break;
                }
            }

            Logger.LogInfo("[Sevs Better Nametags] Download complete. Injecting new version...");
            InjectNewVersion(selfPath);

            try { File.Delete(deletePath); } catch { }
            Destroy(this);
        }

        private void InjectNewVersion(string dllPath)
        {
            try
            {
                Assembly loaded = Assembly.Load(File.ReadAllBytes(dllPath));
                foreach (Type type in loaded.GetTypes())
                {
                    if (!typeof(BaseUnityPlugin).IsAssignableFrom(type) || type.IsAbstract) continue;
                    var meta = type.GetCustomAttributes(typeof(BepInPlugin), true).FirstOrDefault() as BepInPlugin;
                    if (meta == null) continue;

                    _skipVersionCheck = true;
                    GameObject go = new GameObject("Updated SevNametags");
                    DontDestroyOnLoad(go);
                    go.AddComponent(type);

                    Logger.LogInfo("[Sevs Better Nametags] Self-update inject complete: " + meta.Name + " " + meta.Version);
                    break;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("[Sevs Better Nametags] Self-update inject failed: " + e.Message);
            }
        }

        private void OnEnable()  => PhotonNetwork.AddCallbackTarget(this);
        private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

        private void Update()
        {
            if (!_userIdWritten)
            {
                try
                {
                    string uid = NetworkSystem.Instance?.LocalPlayer?.UserId;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        System.IO.File.WriteAllText(
                            System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "UserId.txt"), uid);
                        Logger.LogInfo("[Sevs Better Nametags] Wrote UserId.txt");
                        _userIdWritten = true;
                        PushLocalProps();
                        _applyOnLoadTimer = Time.time + 2.5f;
                    }
                }
                catch { }
            }

            if (_applyOnLoadTimer > 0f && Time.time >= _applyOnLoadTimer)
            {
                _applyOnLoadTimer = -1f;
                try { foreach (var rig in UnityEngine.Object.FindObjectsOfType<VRRig>()) ApplyCustomDisplay(rig); }
                catch { }
                try
                {
                    var m = typeof(GorillaScoreBoard).GetMethod("RedrawPlayerLines",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    foreach (var board in UnityEngine.Object.FindObjectsOfType<GorillaScoreBoard>())
                        m?.Invoke(board, null);
                }
                catch { }
            }

            if (UnityInput.Current.GetKey(KeyCode.RightAlt))
            {
                if (UnityInput.Current.GetKeyDown(KeyCode.N))
                    _guiVisible = !_guiVisible;
                if (UnityInput.Current.GetKeyDown(KeyCode.D))
                {
                    _debugMode = !_debugMode;
                    _guiVisible = true;
                }
            }

            if (_debugMode)
            {
                if (PronounWorldOffset != _lastPronounWorldOffset)
                {
                    _lastPronounWorldOffset = PronounWorldOffset;
                    foreach (var kv in RigTmps)
                        if (kv.Value.nameTmp != null && kv.Value.pronTmp != null)
                            kv.Value.pronTmp.transform.localPosition =
                                kv.Value.nameTmp.transform.localPosition + new Vector3(0f, PronounWorldOffset, 0f);
                }
                if (PronounBoardOffset != _lastPronounBoardOffset)
                {
                    _lastPronounBoardOffset = PronounBoardOffset;
                    foreach (var kv in ScoreBoardStartPatch.BoardTmps)
                        if (kv.Value.origTmp != null && kv.Value.pronTmp != null)
                            kv.Value.pronTmp.transform.localPosition =
                                kv.Value.origTmp.transform.localPosition
                                + new Vector3(0f, PronounBoardOffset, 0f);
                }
            }
        }

        public void OnJoinedRoom()
        {
            PushLocalProps();
            _applyOnLoadTimer = Time.time + 1.5f;
        }

        public void OnLeftRoom() { }
        public void OnCreatedRoom() { }
        public void OnCreateRoomFailed(short c, string m) { }
        public void OnJoinRoomFailed(short c, string m) { }
        public void OnJoinRandomFailed(short c, string m) { }
        public void OnFriendListUpdate(List<FriendInfo> l) { }
        public void OnPreLeavingRoom() { }

        public void OnPlayerPropertiesUpdate(Player target, ExitGames.Client.Photon.Hashtable changed)
        {
            if (!changed.ContainsKey(KEY_NAME) && !changed.ContainsKey(KEY_PRONOUNS) &&
                !changed.ContainsKey(KEY_PRONCOLOR) && !changed.ContainsKey(KEY_FONT)) return;
            try
            {
                foreach (var rig in UnityEngine.Object.FindObjectsOfType<VRRig>())
                    if (rig.Creator?.ActorNumber == target.ActorNumber) { ApplyCustomDisplay(rig); break; }
            }
            catch { }
        }

        public void OnPlayerEnteredRoom(Player p) { }
        public void OnPlayerLeftRoom(Player p) { }
        public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable p) { }
        public void OnMasterClientSwitched(Player p) { }

        private void OnGUI()
        {
            if (!_guiVisible) return;
            if (!_stylesReady) BuildStyles();

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.5f, 1.4f);
            float vH = Screen.height / scale;
            float viewH = vH - TitleH - 4f;
            float cw = PanelW - ContentX - 8f;

            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

            GUI.Box(new Rect(0, 0, PanelW, vH), "", _stylePanelBg);
            GUI.Box(new Rect(0, 0, PanelW, TitleH), "", _styleTitleBg);

            string titleText = _debugMode ? "DEBUG  ·  SEVS BETTER NAMETAGS" : "SEVS BETTER NAMETAGS";
            GUI.Label(new Rect(ContentX, 6, cw - 36, 18), titleText, new GUIStyle(_styleSectionHeader) { fontSize = 11 });
            GUI.Label(new Rect(ContentX, 24, cw - 36, 16), "RAlt+N  toggle    RAlt+D  debug", new GUIStyle(_styleLabel) { fontSize = 9 });

            GUI.Box(new Rect(0, TitleH - 1, PanelW, 2), "", _styleDivider);

            if (GUI.Button(new Rect(PanelW - 30, 10, 24, 24), "×", _styleCloseBtn))
                _guiVisible = false;

            HandleScrollInput(new Rect(0, TitleH, PanelW, viewH), viewH);
            DrawScrollbar(new Rect(2, TitleH + 4, SbW, viewH - 8), viewH);

            _scrollY = Mathf.Clamp(_scrollY, 0f, Mathf.Max(0f, _totalContentH - viewH));

            GUI.BeginGroup(new Rect(ContentX, TitleH, cw, viewH));

            float y = -_scrollY + 10f;
            float startY = y;

            y = _debugMode ? DrawDebugMenu(y, cw) : DrawMainMenu(y, cw);

            _totalContentH = y + _scrollY - startY;
            GUI.EndGroup();

            GUI.matrix = prev;
        }

        private float DrawMainMenu(float y, float cw)
        {
            string previewName = string.IsNullOrEmpty(_inputName) ? "YourName" : _inputName;
            Color pc1 = new Color(_r1 / 9f, _g1 / 9f, _b1 / 9f);
            Color pc2 = new Color(_r2 / 9f, _g2 / 9f, _b2 / 9f);
            string fontLabel = _fontIdx > 0 && _fontIdx < FontNames.Length ? FontNames[_fontIdx] : "Default";

            GUI.Box(new Rect(0, y, cw, 66), "", _stylePreviewBox);
            GUI.Label(new Rect(8, y + 6, cw - 16, 22), previewName, _stylePreviewName);
            if (!string.IsNullOrEmpty(_inputPronouns))
            {
                string coloredPron = _useGradient
                    ? GradientText(_inputPronouns, pc1, pc2)
                    : $"<color=#{ColorToHex(pc1)}>{_inputPronouns}</color>";
                GUI.Label(new Rect(18, y + 30, cw - 26, 16), coloredPron, _stylePreviewPronoun);
            }
            GUI.Label(new Rect(8, y + 51, cw - 16, 13), $"Font: {fontLabel}",
                new GUIStyle(_styleLabel) { fontSize = 9 });
            y += 74;

            y = Section(0, y, cw, "DISPLAY NAME  ·  max 20");
            _inputName = GUI.TextField(new Rect(0, y, cw, 30), _inputName, 20, _styleField); y += 38;

            y = Section(0, y, cw, "PRONOUNS  ·  max 20");
            GUI.Label(new Rect(0, y, cw, 16), "Shown below your name at smaller size.", _styleLabel); y += 20;
            _inputPronouns = GUI.TextField(new Rect(0, y, cw, 30), _inputPronouns, 20, _styleField); y += 38;

            y = Section(0, y, cw, "PRONOUN COLOR");
            y = DrawColorBlock(0, y, cw, "Color 1", ref _r1, ref _g1, ref _b1, _swatch1); y += 10;

            _useGradient = GUI.Toggle(new Rect(0, y, cw, 22), _useGradient, "  Gradient", _styleToggle); y += 30;
            if (_useGradient)
            {
                y = DrawColorBlock(0, y, cw, "Color 2", ref _r2, ref _g2, ref _b2, _swatch2); y += 10;
            }

            y = Section(0, y, cw, $"FONT  ·  {Fonts.Count} available");
            float fgh = Mathf.Ceil(FontNames.Length / 2f) * 26f;
            _fontIdx = GUI.SelectionGrid(new Rect(0, y, cw, fgh), _fontIdx, FontNames, 2,
                new GUIStyle(_styleBtn) { fontSize = 10, fixedHeight = 22 });
            y += fgh + 12;

            GUI.Box(new Rect(0, y, cw, 1), "", _styleDivider); y += 8;

            if (GUI.Button(new Rect(0, y, cw, 38), "✔  APPLY & SAVE", _styleSaveBtn))
            {
                _inputName = StripTags(_inputName);
                _inputPronouns = StripTags(_inputPronouns);
                SaveSettings();
                PushLocalProps();
            }
            y += 46;

            if (GUI.Button(new Rect(0, y, cw, 28), "↺  Reset to Defaults", _styleBtnRed))
            {
                _inputName = _inputPronouns = "";
                _r1 = _g1 = _b1 = 9;
                _r2 = 0; _g2 = 9; _b2 = 9;
                _useGradient = false;
                _fontIdx = 0;
                UpdateSwatch(_swatch1, _r1, _g1, _b1);
                UpdateSwatch(_swatch2, _r2, _g2, _b2);
                SaveSettings();
                PushLocalProps();
            }
            y += 38;

            return y;
        }

        private float DrawDebugMenu(float y, float cw)
        {
            GUI.Label(new Rect(0, y, cw, 16), "Changes apply live. Not saved — for tuning only.", _styleLabel); y += 22;

            y = Section(0, y, cw, "VRRIG PRONOUN Y OFFSET");
            float newWO = GUI.HorizontalSlider(new Rect(0, y, cw, 16), Mathf.Clamp(PronounWorldOffset, -0.15f, 0.15f), -0.15f, 0.15f); y += 22;
            if (newWO != PronounWorldOffset)
            {
                PronounWorldOffset = newWO;
                _debugWOStr = PronounWorldOffset.ToString("F4");
            }
            string editedWO = GUI.TextField(new Rect(0, y, cw, 28), _debugWOStr, _styleField); y += 34;
            if (editedWO != _debugWOStr)
            {
                _debugWOStr = editedWO;
                if (float.TryParse(editedWO, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsedWO))
                    PronounWorldOffset = parsedWO;
            }

            y = Section(0, y, cw, "LEADERBOARD PRONOUN Y OFFSET");
            float newBO = GUI.HorizontalSlider(new Rect(0, y, cw, 16), Mathf.Clamp(PronounBoardOffset, -20f, 0f), -20f, 0f); y += 22;
            if (newBO != PronounBoardOffset)
            {
                PronounBoardOffset = newBO;
                _debugBOStr = PronounBoardOffset.ToString("F4");
            }
            string editedBO = GUI.TextField(new Rect(0, y, cw, 28), _debugBOStr, _styleField); y += 34;
            if (editedBO != _debugBOStr)
            {
                _debugBOStr = editedBO;
                if (float.TryParse(editedBO, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsedBO))
                    PronounBoardOffset = parsedBO;
            }

            y += 8;
            if (GUI.Button(new Rect(0, y, cw, 26), "↺  Reset to Defaults", _styleBtnRed))
            {
                PronounWorldOffset = -0.02f;
                PronounBoardOffset = -0.015f;
                _debugWOStr = PronounWorldOffset.ToString("F4");
                _debugBOStr = PronounBoardOffset.ToString("F4");
            }
            y += 34;

            return y;
        }

        private void HandleScrollInput(Rect panelRect, float viewH)
        {
            var e = Event.current;

            if (e.type == EventType.ScrollWheel && panelRect.Contains(e.mousePosition))
            {
                _scrollY += e.delta.y * 18f;
                e.Use();
                return;
            }

            Rect sbRect = new Rect(2, TitleH + 4, SbW, viewH - 8);

            if (e.type == EventType.MouseDown && sbRect.Contains(e.mousePosition))
            {
                float sbH = sbRect.height;
                float maxS = Mathf.Max(0f, _totalContentH - viewH);
                float thumbH = Mathf.Max(28f, sbH * (viewH / Mathf.Max(viewH, _totalContentH)));
                float thumbY = sbRect.y + (sbH - thumbH) * (_scrollY / Mathf.Max(1f, maxS));

                _draggingScrollbar = true;
                _dragStartMouseY = e.mousePosition.y;
                _dragStartScrollY = _scrollY;

                if (e.mousePosition.y < thumbY || e.mousePosition.y > thumbY + thumbH)
                {
                    float ratio = (e.mousePosition.y - sbRect.y) / sbH;
                    _scrollY = ratio * maxS;
                }
                e.Use();
            }

            if (_draggingScrollbar)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float sbH = viewH - 8f;
                    float maxS = Mathf.Max(0f, _totalContentH - viewH);
                    float delta = (e.mousePosition.y - _dragStartMouseY) / sbH * maxS;
                    _scrollY = _dragStartScrollY + delta;
                    e.Use();
                }
                if (e.type == EventType.MouseUp)
                    _draggingScrollbar = false;
            }
        }

        private void DrawScrollbar(Rect sb, float viewH)
        {
            GUI.Box(sb, "", _styleScrollTrack);

            float maxScroll = Mathf.Max(1f, _totalContentH - viewH);
            float thumbH = Mathf.Max(28f, sb.height * (viewH / Mathf.Max(viewH, _totalContentH)));
            float thumbY = sb.y + (sb.height - thumbH) * Mathf.Clamp01(_scrollY / maxScroll);

            GUI.Box(new Rect(sb.x + 1, thumbY, sb.width - 2, thumbH), "", _styleScrollThumb);
        }

        private float Section(float x, float y, float w, string title)
        {
            y += 8;
            GUI.Box(new Rect(x, y, w, 26), "", _styleSection);
            GUI.Box(new Rect(x, y, 3, 26), "", _styleScrollThumb);
            GUI.Label(new Rect(x + 10, y + 4, w - 14, 18), title, _styleHeaderLabel);
            return y + 32;
        }

        private float DrawColorBlock(float x, float y, float w, string label, ref int r, ref int g, ref int b, Texture2D swatch)
        {
            GUI.Label(new Rect(x, y, 60, 16), label, _styleLabel); y += 20;
            int nr = SliderRow(x, y, w, "R", r, new Color(1f, 0.38f, 0.38f)); y += 26;
            int ng = SliderRow(x, y, w, "G", g, new Color(0.38f, 1f, 0.38f)); y += 26;
            int nb = SliderRow(x, y, w, "B", b, new Color(0.38f, 0.68f, 1f)); y += 26;
            if (nr != r || ng != g || nb != b) UpdateSwatch(swatch, nr, ng, nb);
            r = nr; g = ng; b = nb;
            _styleSwatch.normal.background = swatch;
            GUI.Box(new Rect(x, y, w, 32), "", _styleSwatch);
            GUI.Label(new Rect(x + 6, y + 9, w - 12, 16),
                $"#{ColorToHex(new Color(r / 9f, g / 9f, b / 9f))}   R:{r}  G:{g}  B:{b}",
                new GUIStyle(_styleLabel) { fontSize = 9, normal = { textColor = new Color(1f, 1f, 1f, 0.65f) } });
            return y + 38;
        }

        private int SliderRow(float x, float y, float w, string ch, int val, Color chColor)
        {
            GUI.Label(new Rect(x, y + 2, 14, 16), ch,
                new GUIStyle(_styleLabel) { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = chColor } });
            int nv = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 18, y + 4, w - 36, 14), val, 0f, 9f));
            GUI.Label(new Rect(x + w - 16, y + 2, 16, 16), nv.ToString(),
                new GUIStyle(_styleLabel) { fontSize = 10, alignment = TextAnchor.MiddleRight });
            return nv;
        }

        private void BuildStyles()
        {
            _stylesReady = true;

            Color bg = new Color(0.06f, 0.06f, 0.11f, 0.98f);
            Color titleBg = new Color(0.09f, 0.05f, 0.18f, 1f);
            Color secBg = new Color(0.12f, 0.08f, 0.22f, 1f);
            Color accent = new Color(0.62f, 0.25f, 1.00f, 1f);
            Color accentDim = new Color(0.45f, 0.16f, 0.75f, 1f);
            Color btnBg = new Color(0.14f, 0.09f, 0.26f, 1f);
            Color btnHov = new Color(0.26f, 0.16f, 0.46f, 1f);
            Color divCol = new Color(0.55f, 0.20f, 0.90f, 0.6f);
            Color textMain = new Color(0.95f, 0.93f, 1.00f, 1f);
            Color textDim = new Color(0.58f, 0.56f, 0.68f, 1f);
            Color prevBg = new Color(0.04f, 0.04f, 0.09f, 1f);
            var ro0 = new RectOffset(0, 0, 0, 0);

            _stylePanelBg = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, bg) },
                border = ro0, padding = ro0, margin = ro0
            };
            _styleTitleBg = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, titleBg) },
                border = ro0, padding = ro0, margin = ro0
            };
            _styleDivider = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, divCol) },
                border = ro0, padding = ro0, margin = ro0
            };
            _styleSection = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, secBg) },
                border = new RectOffset(2, 2, 2, 2), padding = ro0
            };
            _styleSectionHeader = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = accent },
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 0, 0, 0)
            };
            _styleHeaderLabel = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.75f, 0.70f, 0.92f, 1f) },
                fontSize = 10, fontStyle = FontStyle.Bold
            };
            _styleLabel = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = textDim },
                fontSize = 10, wordWrap = true
            };
            _styleField = new GUIStyle(GUI.skin.textField)
            {
                normal = { background = MakeTex(2, 2, new Color(0.03f, 0.03f, 0.09f, 1f)), textColor = textMain },
                focused = { background = MakeTex(2, 2, new Color(0.08f, 0.05f, 0.18f, 1f)), textColor = Color.white },
                fontSize = 13, padding = new RectOffset(8, 8, 6, 6),
                border = new RectOffset(1, 1, 1, 1)
            };
            _styleBtn = new GUIStyle(GUI.skin.button)
            {
                normal = { background = MakeTex(2, 2, btnBg), textColor = textMain },
                hover = { background = MakeTex(2, 2, btnHov), textColor = Color.white },
                active = { background = MakeTex(2, 2, accentDim), textColor = Color.white },
                fontSize = 11, fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 5, 5)
            };
            _styleSaveBtn = new GUIStyle(GUI.skin.button)
            {
                normal = { background = MakeTex(2, 2, new Color(0.18f, 0.08f, 0.36f, 1f)), textColor = new Color(0.88f, 0.72f, 1.00f) },
                hover = { background = MakeTex(2, 2, new Color(0.35f, 0.14f, 0.65f, 1f)), textColor = Color.white },
                active = { background = MakeTex(2, 2, accent), textColor = Color.white },
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 8, 8)
            };
            _styleBtnRed = new GUIStyle(GUI.skin.button)
            {
                normal = { background = MakeTex(2, 2, new Color(0.22f, 0.05f, 0.05f, 1f)), textColor = new Color(1f, 0.50f, 0.50f) },
                hover = { background = MakeTex(2, 2, new Color(0.44f, 0.09f, 0.09f, 1f)), textColor = Color.white },
                active = { background = MakeTex(2, 2, new Color(0.65f, 0.12f, 0.12f, 1f)), textColor = Color.white },
                fontSize = 11, fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 5, 5)
            };
            _styleCloseBtn = new GUIStyle(GUI.skin.button)
            {
                normal = { background = MakeTex(2, 2, new Color(0.28f, 0.04f, 0.04f, 0.85f)), textColor = new Color(1f, 0.45f, 0.45f) },
                hover = { background = MakeTex(2, 2, new Color(0.70f, 0.08f, 0.08f, 1f)), textColor = Color.white },
                fontSize = 15, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, padding = ro0
            };
            _styleToggle = new GUIStyle(GUI.skin.toggle)
            {
                normal = { textColor = textMain },
                fontSize = 11
            };
            _styleSwatch = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _swatch1 },
                border = new RectOffset(1, 1, 1, 1), padding = ro0
            };
            _styleScrollTrack = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, new Color(0.03f, 0.02f, 0.07f, 0.9f)) },
                border = new RectOffset(3, 3, 3, 3), padding = ro0
            };
            _styleScrollThumb = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, new Color(0.48f, 0.16f, 0.82f, 0.95f)) },
                border = new RectOffset(3, 3, 3, 3), padding = ro0
            };
            _stylePreviewBox = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, prevBg) },
                border = new RectOffset(2, 2, 2, 2), padding = ro0
            };
            _stylePreviewName = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                fontSize = 13, fontStyle = FontStyle.Bold, richText = true
            };
            _stylePreviewPronoun = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = textDim },
                fontSize = 10, richText = true
            };
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var t = new Texture2D(w, h);
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        private static void UpdateSwatch(Texture2D tex, int r, int g, int b)
        {
            tex.SetPixel(0, 0, new Color(r / 9f, g / 9f, b / 9f));
            tex.Apply();
        }

        private void ScanFonts()
        {
            Fonts.Clear();
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f != null) Fonts.Add(f);
            Fonts.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            FontNames = new string[Fonts.Count + 1];
            FontNames[0] = "Default";
            for (int i = 0; i < Fonts.Count; i++) FontNames[i + 1] = Fonts[i].name;

            foreach (var f in Fonts)
                try { TMPro.MaterialReferenceManager.AddFontAsset(f); } catch { }

            _fontIdx = 0;
            for (int i = 1; i < FontNames.Length; i++)
                if (FontNames[i] == _savedFontName) { _fontIdx = i; break; }
        }

        private void LoadSettings()
        {
            _inputName = PlayerPrefs.GetString(KEY_NAME, "");
            _inputPronouns = PlayerPrefs.GetString(KEY_PRONOUNS, "");
            _r1 = PlayerPrefs.GetInt(KEY_R1, 9); _g1 = PlayerPrefs.GetInt(KEY_G1, 9); _b1 = PlayerPrefs.GetInt(KEY_B1, 9);
            _r2 = PlayerPrefs.GetInt(KEY_R2, 0); _g2 = PlayerPrefs.GetInt(KEY_G2, 9); _b2 = PlayerPrefs.GetInt(KEY_B2, 9);
            _useGradient = PlayerPrefs.GetInt(KEY_GRAD, 0) == 1;

            PronounWorldOffset = PlayerPrefs.GetFloat(KEY_PRON_WO, -0.0309f);
            PronounBoardOffset = PlayerPrefs.GetFloat(KEY_PRON_BO, -5f);
            _lastPronounWorldOffset = PronounWorldOffset;
            _lastPronounBoardOffset = PronounBoardOffset;
            _debugWOStr = PronounWorldOffset.ToString("F4");
            _debugBOStr = PronounBoardOffset.ToString("F4");

            _savedFontName = PlayerPrefs.GetString(KEY_FONT, "");
            _fontIdx = 0;
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetString(KEY_NAME, _inputName);
            PlayerPrefs.SetString(KEY_PRONOUNS, _inputPronouns);
            PlayerPrefs.SetInt(KEY_R1, _r1); PlayerPrefs.SetInt(KEY_G1, _g1); PlayerPrefs.SetInt(KEY_B1, _b1);
            PlayerPrefs.SetInt(KEY_R2, _r2); PlayerPrefs.SetInt(KEY_G2, _g2); PlayerPrefs.SetInt(KEY_B2, _b2);
            PlayerPrefs.SetInt(KEY_GRAD, _useGradient ? 1 : 0);

            _savedFontName = _fontIdx > 0 ? FontNames[_fontIdx] : "";
            PlayerPrefs.SetString(KEY_FONT, _savedFontName);
            PlayerPrefs.Save();
        }

        internal void PushLocalProps()
        {
            try
            {
                Color c1 = new Color(_r1 / 9f, _g1 / 9f, _b1 / 9f);
                Color c2 = new Color(_r2 / 9f, _g2 / 9f, _b2 / 9f);
                string colorData = _useGradient ? $"#{ColorToHex(c1)},#{ColorToHex(c2)}" : $"#{ColorToHex(c1)}";
                PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
                {
                    { KEY_NAME, Sanitize(_inputName) },
                    { KEY_PRONOUNS, Sanitize(_inputPronouns) },
                    { KEY_PRONCOLOR, colorData },
                    { KEY_FONT, _fontIdx > 0 ? FontNames[_fontIdx] : "" }
                });
            }
            catch (Exception ex) { Logger.LogWarning("[Sevs Better Nametags] PushLocalProps: " + ex.Message); }
        }

        private IEnumerator FetchColorsLoop()
        {
            using (var req = UnityWebRequest.Get(ApiUrl + "?action=get_all"))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    ParseColors(req.downloadHandler.text);
                    Logger.LogInfo($"[Sevs Better Nametags] Loaded {UserColors.Count} color entries.");
                }
            }
        }

        private static string ExtractJsonField(string obj, string key)
        {
            var m = Regex.Match(obj, "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void ParseColors(string json)
        {
            try
            {
                UserColors.Clear();
                UserLabels.Clear();

                var em = Regex.Match(json, "\"entries\"\\s*:\\s*\\[(.+?)\\]", RegexOptions.Singleline);
                if (!em.Success) { Instance.Logger.LogWarning("[Sevs Better Nametags] ParseColors: no entries array"); return; }

                foreach (Match entry in Regex.Matches(em.Groups[1].Value, "\\{[^}]+\\}"))
                {
                    string uid = ExtractJsonField(entry.Value, "user_id");
                    string color = ExtractJsonField(entry.Value, "color");
                    string label = ExtractJsonField(entry.Value, "label") ?? "";
                    if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(color)) continue;
                    if (ColorUtility.TryParseHtmlString(color, out Color c)) { UserColors[uid] = c; UserLabels[uid] = label; }
                }
            }
            catch (Exception ex) { Instance.Logger.LogError("[Sevs Better Nametags] ParseColors: " + ex); }
        }

        internal const int MaxNameLen = 20;

        internal static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return Regex.Replace(s, "<[^>]*>", "");
        }

        internal static string Sanitize(string s)
        {
            string clean = StripTags(s ?? "");
            return clean.Length > MaxNameLen ? clean.Substring(0, MaxNameLen) : clean;
        }

        internal static string ColorToHex(Color c)
            => ((int)(c.r * 255)).ToString("X2")
             + ((int)(c.g * 255)).ToString("X2")
             + ((int)(c.b * 255)).ToString("X2");

        internal static string GradientText(string text, Color c1, Color c2)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                float t = text.Length == 1 ? 0f : (float)i / (text.Length - 1);
                sb.Append("<color=#").Append(ColorToHex(Color.Lerp(c1, c2, t))).Append(">").Append(text[i]).Append("</color>");
            }
            return sb.ToString();
        }

        internal static string BuildPronounsMarkup(string pronouns, string colorData, float nameFontSize)
        {
            string clean = StripTags(pronouns ?? "");
            if (string.IsNullOrEmpty(clean)) return "";

            int pronounSize = Mathf.Max(1, Mathf.RoundToInt(nameFontSize * 2f / 3f));
            string content;
            if (!string.IsNullOrEmpty(colorData) && colorData.Contains(","))
            {
                string[] parts = colorData.Split(',');
                if (ColorUtility.TryParseHtmlString(parts[0].Trim(), out Color c1) &&
                    ColorUtility.TryParseHtmlString(parts[1].Trim(), out Color c2))
                    content = GradientText(clean, c1, c2);
                else content = clean;
            }
            else if (!string.IsNullOrEmpty(colorData) && ColorUtility.TryParseHtmlString(colorData.Trim(), out Color sc))
                content = $"<color=#{ColorToHex(sc)}>{clean}</color>";
            else
                content = clean;

            return $"<size={pronounSize}>{content}</size>";
        }

        internal static bool HasFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName) || Instance == null) return false;
            foreach (var f in Instance.Fonts)
                if (f != null && f.name == fontName) return true;
            return false;
        }

        internal static Player GetPhotonPlayerByUserId(string userId)
        {
            try { foreach (var p in PhotonNetwork.PlayerList) if (p.UserId == userId) return p; } catch { }
            return null;
        }

        internal static Player GetPhotonPlayerByActor(int actorNumber)
        {
            try { foreach (var p in PhotonNetwork.PlayerList) if (p.ActorNumber == actorNumber) return p; } catch { }
            return null;
        }

        internal static void ApplyCustomDisplay(VRRig rig)
        {
            try
            {
                if (rig == null || rig.Creator == null) return;

                int rigId = ((UnityEngine.Object)(object)rig).GetInstanceID();

                if (RigTmps.TryGetValue(rigId, out var existing) && existing.nameTmp == null)
                    RigTmps.Remove(rigId);

                if (!RigTmps.ContainsKey(rigId))
                {
                    var origTmp = (TMP_Text)rig.playerText1;
                    var origGO = origTmp.gameObject;

                    var nameGO = UnityEngine.Object.Instantiate(origGO, origGO.transform.parent);
                    nameGO.name = "SevName";
                    var nameTmp = nameGO.GetComponent<TMP_Text>();
                    nameTmp.richText = true;

                    var pronGO = UnityEngine.Object.Instantiate(origGO, origGO.transform.parent);
                    pronGO.name = "SevPronouns";
                    var pronTmp = pronGO.GetComponent<TMP_Text>();
                    pronTmp.richText = true;
                    pronTmp.fontSize = origTmp.fontSize * 0.65f;

                    origGO.SetActive(false);

                    RigTmps[rigId] = (nameTmp, pronTmp);
                }

                var (myName, myPron) = RigTmps[rigId];

                myPron.transform.localPosition = myName.transform.localPosition
                    + new Vector3(0f, PronounWorldOffset, 0f);

                var photon = GetPhotonPlayerByActor(rig.Creator.ActorNumber);
                var props = photon?.CustomProperties;

                string customName = null;
                if (props != null && props.TryGetValue(KEY_NAME, out object nObj))
                    customName = Sanitize(nObj?.ToString() ?? "");
                string displayName = !string.IsNullOrEmpty(customName)
                    ? customName : Sanitize(rig.playerNameVisible ?? rig.Creator.DefaultName);
                string nameMarkup = UserColors.TryGetValue(rig.Creator.UserId, out Color api)
                    ? $"<color=#{ColorToHex(api)}>{displayName}</color>" : displayName;

                myName.color = Color.white;
                myName.text = nameMarkup;

                string pronText = "";
                if (props != null
                    && props.TryGetValue(KEY_PRONOUNS, out object prObj)
                    && props.TryGetValue(KEY_PRONCOLOR, out object pcObj))
                {
                    string built = BuildPronounsMarkup(Sanitize(prObj?.ToString() ?? ""), pcObj?.ToString() ?? "", myName.fontSize);
                    if (!string.IsNullOrEmpty(built)) pronText = built;
                }
                myPron.color = Color.white;
                myPron.text = pronText;

                string fontName = "";
                if (props != null && props.TryGetValue(KEY_FONT, out object foObj))
                    fontName = foObj?.ToString() ?? "";
                ApplyFont(rigId, myName, myPron, fontName);
            }
            catch { }
        }

        private static void ApplyFont(int rigId, TMP_Text nameTmp, TMP_Text pronTmp, string fontName)
        {
            try
            {
                if (!_defaultFonts.ContainsKey(rigId)) _defaultFonts[rigId] = nameTmp.font;

                TMP_FontAsset font = null;
                if (!string.IsNullOrEmpty(fontName) && Instance != null)
                    foreach (var f in Instance.Fonts)
                        if (f != null && f.name == fontName) { font = f; break; }
                if (font == null) font = _defaultFonts[rigId];

                nameTmp.font = font;
                pronTmp.font = font;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GorillaScoreBoard), "Start", MethodType.Normal)]
    internal class ScoreBoardStartPatch
    {
        internal static readonly Dictionary<int, (TMP_Text origTmp, TMP_Text pronTmp, TMP_Text localNameTmp)> BoardTmps =
            new Dictionary<int, (TMP_Text, TMP_Text, TMP_Text)>();

        internal static void Setup(GorillaScoreBoard board)
        {
            try
            {
                var origTmp = (TMP_Text)board.boardText;
                if (origTmp == null) { Plugin.Log?.LogWarning("[Board Setup] boardText is null"); return; }
                origTmp.richText = true;
                origTmp.fontStyle = TMPro.FontStyles.Normal;

                var go = origTmp.gameObject;
                foreach (var n in new[] { "SevBoardPronouns", "SevBoardLocalName" })
                {
                    var c = go.transform.Find(n);
                    if (c != null) UnityEngine.Object.Destroy(c.gameObject);
                    var p = go.transform.parent;
                    if (p != null) { var s = p.Find(n); if (s != null) UnityEngine.Object.Destroy(s.gameObject); }
                }
            }
            catch (Exception ex) { Plugin.Log?.LogError("[Board Setup] " + ex); }
        }

        private static void Postfix(GorillaScoreBoard __instance) => Setup(__instance);
    }

    [HarmonyPatch(typeof(GorillaScoreBoard), "RedrawPlayerLines", MethodType.Normal)]
    internal class RedrawPlayerLinesPatch
    {
        private const string PronOpen = "<voffset=-0.55em>";
        private const string PronClose = "</voffset>";

        private static void Postfix(GorillaScoreBoard __instance)
        {
            try
            {
                var origTmp = (TMP_Text)__instance.boardText;
                if (origTmp == null) return;
                origTmp.richText = true;

                var players = new List<GorillaPlayerScoreboardLine>();
                for (int i = 0; i < __instance.lines.Count; i++)
                {
                    var ln = __instance.lines[i];
                    if (ln.IsLineActive() && ln.IsPlayerInRoom()) players.Add(ln);
                }

                string[] nameParts = origTmp.text.Split('\n');
                float boardFontSz = origTmp.fontSize;

                var nameSb = new StringBuilder();

                for (int i = 0; i < nameParts.Length; i++)
                {
                    if (i > 0) nameSb.Append('\n');

                    int pidx = i - 2;
                    if (pidx < 0 || pidx >= players.Count)
                    {
                        nameSb.Append(nameParts[i]);
                        continue;
                    }

                    var photonPlayer = players[pidx].linePlayer;
                    int actorNum = photonPlayer?.ActorNumber ?? -1;
                    var photon = actorNum >= 0 ? Plugin.GetPhotonPlayerByActor(actorNum) : null;
                    string userId = photonPlayer?.UserId ?? "";

                    string displayName = null;
                    if (photon?.CustomProperties.TryGetValue(Plugin.KEY_NAME, out object nObj) == true)
                        displayName = Plugin.Sanitize(nObj?.ToString() ?? "");
                    if (string.IsNullOrEmpty(displayName))
                        displayName = Plugin.Sanitize(Regex.Replace(nameParts[i], "<[^>]+>", "").TrimStart(' '));

                    string fontName = null;
                    if (photon?.CustomProperties.TryGetValue(Plugin.KEY_FONT, out object foObj) == true)
                    {
                        string fn = foObj?.ToString();
                        if (!string.IsNullOrEmpty(fn) && Plugin.HasFont(fn)) fontName = fn;
                    }

                    nameSb.Append(' ');
                    if (fontName != null) nameSb.Append("<font=\"").Append(fontName).Append("\">");
                    if (!string.IsNullOrEmpty(userId) && Plugin.UserColors.TryGetValue(userId, out Color c))
                        nameSb.Append("<color=#").Append(Plugin.ColorToHex(c)).Append(">").Append(displayName).Append("</color>");
                    else
                        nameSb.Append(displayName);
                    if (fontName != null) nameSb.Append("</font>");

                    object prObj = null, pcObj = null;
                    bool hasPron = photon?.CustomProperties.TryGetValue(Plugin.KEY_PRONOUNS, out prObj) == true;
                    bool hasPCol = hasPron && photon.CustomProperties.TryGetValue(Plugin.KEY_PRONCOLOR, out pcObj);
                    if (hasPron && hasPCol)
                    {
                        string pm = Plugin.BuildPronounsMarkup(
                            Plugin.Sanitize(prObj?.ToString() ?? ""),
                            pcObj?.ToString() ?? "",
                            boardFontSz);
                        if (!string.IsNullOrEmpty(pm))
                        {
                            nameSb.Append("<pos=0> ").Append(PronOpen);
                            if (fontName != null) nameSb.Append("<font=\"").Append(fontName).Append("\">");
                            nameSb.Append(pm);
                            if (fontName != null) nameSb.Append("</font>");
                            nameSb.Append(PronClose);
                        }
                    }
                }

                origTmp.text = nameSb.ToString();
                origTmp.ForceMeshUpdate();
            }
            catch (Exception ex) { Plugin.Log?.LogError("[Board Redraw] " + ex); }
        }
    }

    [HarmonyPatch(typeof(VRRig), "SerializeReadShared")]
    internal class VRRigSerializeReadSharedPatch
    {
        private static void Postfix(VRRig __instance) => Plugin.ApplyCustomDisplay(__instance);
    }

    [HarmonyPatch(typeof(VRRig), "UpdateName", new[] { typeof(bool) })]
    internal class VRRigUpdateNamePatch
    {
        private static void Postfix(VRRig __instance) => Plugin.ApplyCustomDisplay(__instance);
    }
}
