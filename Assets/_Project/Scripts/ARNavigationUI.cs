using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Google Maps–style AR Navigation HUD for PIEAS Campus.
/// Creates the entire Canvas hierarchy programmatically — no scene setup needed.
/// 
/// Layout (Search Screen):
///   Top 9%  : Dark app bar  ("AR PathFinder | PIEAS Campus")
///   Mid 19% : AR camera shows through (transparent gap)
///   Bot 72% : Dark bottom sheet — search bar + campus location cards
///
/// Layout (Navigation Screen):
///   Top 15% : Turn instruction banner  (arrow + text + distance to turn)
///   Mid 55% : AR camera shows through  (route overlays rendered here)
///   Bot 30% : Destination card  (name + distance remaining + ETA + End button)
///   PIP     : Minimap picture-in-picture anchored bottom-left
/// </summary>
namespace ARLocation.MapboxRoutes.SampleProject
{
    public class ARNavigationUI : MonoBehaviour
    {
        // ── Google Maps Dark Color Palette ───────────────────────────────────
        static readonly Color C_PanelBg  = new Color(0.118f, 0.125f, 0.133f, 0.93f); // #1E2024
        static readonly Color C_CardBg   = new Color(0.152f, 0.160f, 0.173f, 0.97f); // #272B2D
        static readonly Color C_RowBg    = new Color(0.185f, 0.196f, 0.210f, 1.00f); // #2F3236
        static readonly Color C_Blue     = new Color(0.259f, 0.522f, 0.957f, 1.00f); // #4285F4
        static readonly Color C_Green    = new Color(0.055f, 0.620f, 0.349f, 1.00f); // #0E9E59
        static readonly Color C_Red      = new Color(0.898f, 0.224f, 0.208f, 1.00f); // #E53935
        static readonly Color C_TextHi   = new Color(0.940f, 0.940f, 0.940f, 1.00f); // near-white
        static readonly Color C_TextLo   = new Color(0.600f, 0.600f, 0.650f, 1.00f); // muted
        static readonly Color C_Divider  = new Color(1.000f, 1.000f, 1.000f, 0.07f); // thin line

        // ── Public Events (MenuController subscribes to these) ───────────────
        public event Action<string> OnSearchRequested;
        public event Action<int>    OnLocationSelected;
        public event Action<int>    OnSearchResultSelected;
        public event Action         OnEndNavigation;

        // ── Internal UI References ───────────────────────────────────────────
        Canvas      _canvas;
        GameObject  _searchScreen;
        GameObject  _navScreen;

        // Search screen
        InputField  _searchInput;
        Transform   _listContent;
        Text        _errorText;
        Text        _successText;
        Text        _arStatusSearch;

        // Navigation screen
        Text        _arrowText;
        Text        _instrText;
        Text        _instrDistText;
        Text        _destText;
        Text        _distText;
        Text        _etaText;
        RawImage    _minimapImg;

        Font  _font;
        bool  _built = false;

        // Cache to restore after search
        List<(string name, string desc)> _cachedLocations = new List<(string, string)>();

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Call once after adding component. Assigns minimap render texture.</summary>
        public void Initialize(Texture minimapTexture)
        {
            if (_built) return;
            EnsureEventSystem();
            _font = GetFont();
            BuildCanvas();
            _built = true;
            if (_minimapImg != null && minimapTexture != null)
                _minimapImg.texture = minimapTexture;

            // Fallback: show default PIEAS campus locations immediately.
            // MenuController.Start() will call SetLocationsList() with the full
            // CampusLocations data shortly after — this just fills the gap.
            if (_cachedLocations.Count == 0)
            {
                SetLocationsList(new List<(string, string)>
                {
                    ("C-Block",         "PIEAS C Block"),
                    ("D-Block",         "PIEAS D Block"),
                    ("Central Library", "PIEAS Library"),
                    ("Auditorium",      "Inaam-ur-Rehman Auditorium"),
                    ("DNE",             "Dept. of Nuclear Engineering"),
                });
            }
        }

        public void ShowSearchScreen()
        {
            if (!_built) return;
            if (_searchScreen) _searchScreen.SetActive(true);
            if (_navScreen)    _navScreen.SetActive(false);
        }

        public void ShowNavScreen()
        {
            if (!_built) return;
            if (_searchScreen) _searchScreen.SetActive(false);
            if (_navScreen)    _navScreen.SetActive(true);
        }

        public void SetMinimapTexture(Texture tex)
        {
            if (_minimapImg) _minimapImg.texture = tex;
        }

        /// <summary>Populate the campus location cards in the scroll list.</summary>
        public void SetLocationsList(List<(string name, string desc)> locations)
        {
            _cachedLocations = locations ?? new List<(string, string)>();
            RebuildList(_cachedLocations, i => OnLocationSelected?.Invoke(i));
        }

        /// <summary>Replace the list with search results. User can scroll up to dismiss.</summary>
        public void ShowSearchResults(List<string> results)
        {
            var items = new List<(string, string)>();
            foreach (var r in results) items.Add((r, "Tap to navigate here"));
            RebuildList(items, i => OnSearchResultSelected?.Invoke(i));
        }

        /// <summary>Restore original campus location cards after a search.</summary>
        public void RestoreLocationsList()
        {
            RebuildList(_cachedLocations, i => OnLocationSelected?.Invoke(i));
        }

        public void ShowError(string msg)
        {
            if (!_built || !_errorText) return;
            _errorText.text = msg ?? "";
            _errorText.gameObject.SetActive(!string.IsNullOrEmpty(msg));
            if (_successText) _successText.gameObject.SetActive(false);
        }

        public void ShowSuccess(string msg)
        {
            if (!_built || !_successText) return;
            _successText.text = msg ?? "";
            _successText.gameObject.SetActive(!string.IsNullOrEmpty(msg));
            if (_errorText) _errorText.gameObject.SetActive(false);
            if (!string.IsNullOrEmpty(msg)) StartCoroutine(ClearSuccessAfter(5f));
        }

        /// <summary>Update the turn instruction banner (top of nav screen).</summary>
        public void UpdateInstruction(string arrow, string instruction, float distToTurnMeters)
        {
            if (!_built) return;
            if (_arrowText)    _arrowText.text    = arrow;
            if (_instrText)    _instrText.text    = instruction;
            if (_instrDistText) _instrDistText.text = distToTurnMeters >= 0
                ? FmtDist(distToTurnMeters)
                : "";
        }

        public void SetDestinationName(string name)
        {
            if (_destText) _destText.text = name;
        }

        /// <summary>Update the bottom card distance + ETA (total remaining).</summary>
        public void UpdateDistanceRemaining(float meters)
        {
            if (!_built) return;
            if (_distText) _distText.text = FmtDist(meters);
            if (_etaText)
            {
                float min = meters / 83f; // ~5 km/h walking pace
                _etaText.text = min < 1    ? "Arriving now" :
                                min < 60   ? $"{Mathf.CeilToInt(min)} min" :
                                             $"{Mathf.FloorToInt(min / 60)}h {Mathf.CeilToInt(min % 60)}m";
            }
        }

        /// <summary>Show AR state in the small badge on the search screen.</summary>
        public void UpdateARStatus(string arState)
        {
            if (_arStatusSearch)
            {
                _arStatusSearch.text = $"AR  {arState}";
                // Color code: green = tracking, yellow = initializing, red = unsupported
                bool good    = arState.Contains("SessionTracking");
                bool bad     = arState.Contains("Unsupported") || arState.Contains("None");
                _arStatusSearch.color = good ? C_Green : bad ? C_Red : new Color(1f, 0.8f, 0.2f);
            }
        }

        // ── Canvas Construction ──────────────────────────────────────────────

        void BuildCanvas()
        {
            var root = new GameObject("ARNav_Canvas");
            root.transform.SetParent(transform, false);

            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            _searchScreen = BuildSearchScreen(root.transform);
            _navScreen    = BuildNavScreen(root.transform);
            _navScreen.SetActive(false);
        }

        // ── Search Screen ────────────────────────────────────────────────────

        GameObject BuildSearchScreen(Transform parent)
        {
            // Full-screen transparent container
            var screen = MakePanel(parent, "SearchScreen", Color.clear, 0, 1, 0, 1);

            // ─ App Bar (top 9%) ─
            var bar = MakePanel(screen.transform, "AppBar", C_PanelBg, 0, 1, 0.91f, 1f);
            MakeTxt(bar.transform, "Title", "AR PathFinder",
                TextAnchor.MiddleLeft, 38, C_TextHi, FontStyle.Bold,
                0.04f, 0.62f, 0f, 1f);
            MakeTxt(bar.transform, "Sub", "PIEAS Campus Navigation",
                TextAnchor.MiddleRight, 20, C_Blue, FontStyle.Normal,
                0.38f, 0.97f, 0f, 1f);

            // ─ AR Status Badge (between bar and sheet) ─
            var badge = MakePanel(screen.transform, "ARBadge",
                new Color(0f, 0f, 0f, 0.60f), 0f, 0.5f, 0.855f, 0.91f);
            _arStatusSearch = MakeTxt(badge.transform, "ARSt", "AR  Checking...",
                TextAnchor.MiddleCenter, 18, new Color(1f, 0.82f, 0.2f),
                FontStyle.Normal, 0f, 1f, 0f, 1f);

            // ─ Bottom Sheet ─
            var sheet = MakePanel(screen.transform, "BottomSheet", C_CardBg, 0, 1, 0f, 0.855f);
            AddOutline(sheet, new Color(1, 1, 1, 0.06f), new Vector2(0, 2));

            // ─ Search Row: top 12% of sheet ─────────────────────────────────────
            // We use a HorizontalLayoutGroup so children are sized and stacked
            // automatically — no fractional anchors on children, no Pad() needed.
            var searchRow = MakePanel(sheet.transform, "SearchRow", C_RowBg, 0f, 1f, 0.88f, 1f);
            var hlg = searchRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 8;
            hlg.padding               = new RectOffset(14, 14, 10, 10);
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            // Magnifier icon (fixed 50 px)
            var icoGo  = new GameObject("SearchIco");
            icoGo.transform.SetParent(searchRow.transform, false);
            icoGo.AddComponent<LayoutElement>().preferredWidth = 50;
            var icoTxt = icoGo.AddComponent<Text>();
            icoTxt.text = "\U0001F50D"; icoTxt.font = _font;
            icoTxt.fontSize           = 28;
            icoTxt.color              = C_TextLo;
            icoTxt.alignment          = TextAnchor.MiddleCenter;
            icoTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Input field (flexible — takes remaining space)
            var ifGo  = new GameObject("IF");
            ifGo.transform.SetParent(searchRow.transform, false);
            ifGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            ifGo.AddComponent<Image>().color = Color.clear;
            // Placeholder
            var phGo = new GameObject("Ph");
            phGo.transform.SetParent(ifGo.transform, false);
            var phRT = phGo.AddComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
            phRT.offsetMin = phRT.offsetMax = Vector2.zero;
            var ph = phGo.AddComponent<Text>();
            ph.text = "Search PIEAS campus..."; ph.color = C_TextLo;
            ph.fontSize = 26; ph.font = _font;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;
            // Input text
            var txGo = new GameObject("Txt");
            txGo.transform.SetParent(ifGo.transform, false);
            var txRT = txGo.AddComponent<RectTransform>();
            txRT.anchorMin = Vector2.zero; txRT.anchorMax = Vector2.one;
            txRT.offsetMin = txRT.offsetMax = Vector2.zero;
            var tx = txGo.AddComponent<Text>();
            tx.color = C_TextHi; tx.fontSize = 26; tx.font = _font;
            tx.alignment = TextAnchor.MiddleLeft;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            _searchInput = ifGo.AddComponent<InputField>();
            _searchInput.placeholder   = ph;
            _searchInput.textComponent = tx;
            _searchInput.caretColor    = C_Blue;
            _searchInput.caretBlinkRate = 0.85f;

            // SEARCH button (fixed 180 px)
            var sbGo  = new GameObject("SearchBtn");
            sbGo.transform.SetParent(searchRow.transform, false);
            sbGo.AddComponent<LayoutElement>().preferredWidth = 180;
            var sbImg = sbGo.AddComponent<Image>();
            sbImg.color = C_Blue;
            var sbBtn  = sbGo.AddComponent<Button>();
            var sbCols = sbBtn.colors;
            sbCols.normalColor      = C_Blue;
            sbCols.highlightedColor = new Color(0.35f, 0.62f, 1f, 1f);
            sbCols.pressedColor     = new Color(0.15f, 0.38f, 0.80f, 1f);
            sbBtn.colors = sbCols; sbBtn.targetGraphic = sbImg;
            sbBtn.onClick.AddListener(() => OnSearchRequested?.Invoke(_searchInput.text));
            var sbLbl    = new GameObject("Lbl");
            sbLbl.transform.SetParent(sbGo.transform, false);
            var sbLblRT  = sbLbl.AddComponent<RectTransform>();
            sbLblRT.anchorMin = Vector2.zero; sbLblRT.anchorMax = Vector2.one;
            sbLblRT.offsetMin = sbLblRT.offsetMax = Vector2.zero;
            var sbLblTxt = sbLbl.AddComponent<Text>();
            sbLblTxt.text      = "SEARCH"; sbLblTxt.font = _font;
            sbLblTxt.fontSize  = 24; sbLblTxt.fontStyle = FontStyle.Bold;
            sbLblTxt.color     = Color.white;
            sbLblTxt.alignment = TextAnchor.MiddleCenter;
            sbLblTxt.horizontalOverflow = HorizontalWrapMode.Wrap;

            // ─ Thin divider + Section label below search row ─────────────────────
            MakePanel(sheet.transform, "Div1", C_Divider, 0f, 1f, 0.875f, 0.879f);
            MakeTxt(sheet.transform, "SectionLbl", "  PIEAS Campus Locations",
                TextAnchor.MiddleLeft, 26, C_Blue, FontStyle.Bold,
                0.03f, 1f, 0.82f, 0.875f);

            // ─ Scrollable location list ───────────────────────────────────────────
            _listContent = MakeScrollView(sheet.transform, 0f, 1f, 0.06f, 0.82f);

            // ─ Status messages (share the thin strip at the very bottom) ─────────
            _errorText = MakeTxt(sheet.transform, "ErrTxt", "",
                TextAnchor.MiddleCenter, 22, C_Red, FontStyle.Normal,
                0.02f, 0.98f, 0.01f, 0.06f);
            _errorText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _errorText.gameObject.SetActive(false);

            _successText = MakeTxt(sheet.transform, "OkTxt", "",
                TextAnchor.MiddleCenter, 22, C_Green, FontStyle.Normal,
                0.02f, 0.98f, 0.01f, 0.06f);
            _successText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _successText.gameObject.SetActive(false);

            return screen;
        }

        // ── Navigation Screen ────────────────────────────────────────────────

        GameObject BuildNavScreen(Transform parent)
        {
            var screen = MakePanel(parent, "NavScreen", Color.clear, 0, 1, 0, 1);

            // ─ Instruction Banner (top 15%) ─
            var banner = MakePanel(screen.transform, "Banner", C_PanelBg, 0, 1, 0.85f, 1f);
            AddOutline(banner, new Color(0, 0, 0, 0.5f), new Vector2(0, -2));

            // Large turn arrow on the left
            _arrowText = MakeTxt(banner.transform, "Arrow", "↑",
                TextAnchor.MiddleCenter, 80, C_Blue, FontStyle.Bold,
                0f, 0.20f, 0f, 1f);

            // Vertical divider between arrow and text
            MakePanel(banner.transform, "VDiv", C_Divider, 0.20f, 0.204f, 0.08f, 0.92f);

            // Main instruction text (upper half of banner)
            _instrText = MakeTxt(banner.transform, "InstrTxt", "Follow the AR path",
                TextAnchor.MiddleLeft, 34, C_TextHi, FontStyle.Bold,
                0.22f, 0.97f, 0.42f, 1f);
            _instrText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Distance to next turn (lower half of banner)
            _instrDistText = MakeTxt(banner.transform, "InstrDist", "",
                TextAnchor.MiddleLeft, 28, C_Blue, FontStyle.Normal,
                0.22f, 0.97f, 0f, 0.42f);

            // ─ Bottom Navigation Card (bottom 30%) ─
            var card = MakePanel(screen.transform, "NavCard", C_CardBg, 0, 1, 0f, 0.30f);
            AddOutline(card, new Color(1, 1, 1, 0.06f), new Vector2(0, 2));

            // Blue accent strip at top of card
            MakePanel(card.transform, "BlueAccent", C_Blue, 0f, 1f, 0.985f, 1f);

            // Destination name
            _destText = MakeTxt(card.transform, "DestName", "Destination",
                TextAnchor.UpperLeft, 38, C_TextHi, FontStyle.Bold,
                0.04f, 0.96f, 0.64f, 0.97f);

            // Distance remaining
            _distText = MakeTxt(card.transform, "DistRemain", "-- m",
                TextAnchor.MiddleLeft, 30, C_Blue, FontStyle.Normal,
                0.04f, 0.52f, 0.38f, 0.63f);

            // ETA
            _etaText = MakeTxt(card.transform, "ETA", "-- min",
                TextAnchor.MiddleRight, 26, C_TextLo, FontStyle.Normal,
                0.52f, 0.96f, 0.38f, 0.63f);

            // Horizontal divider
            MakePanel(card.transform, "HDiv", C_Divider, 0.04f, 0.96f, 0.35f, 0.358f);

            // End Navigation button
            var endBtn = MakeBtn(card.transform, "EndNavBtn", "✕   End Navigation",
                C_Red, Color.white, 28, 0.04f, 0.96f, 0.04f, 0.32f);
            endBtn.onClick.AddListener(() => OnEndNavigation?.Invoke());

            // ─ Minimap PIP (bottom-left, overlaps AR view + card top) ─
            // Rendered AFTER card so it draws on top
            var mm = MakePanel(screen.transform, "Minimap",
                new Color(0.07f, 0.08f, 0.09f, 0.90f),
                0f, 0.40f, 0.18f, 0.52f);
            AddOutline(mm, new Color(C_Blue.r, C_Blue.g, C_Blue.b, 0.5f), new Vector2(1.5f, 1.5f));

            // "MAP" label top-left
            MakeTxt(mm.transform, "MapLbl", " MAP",
                TextAnchor.UpperLeft, 18, C_TextLo, FontStyle.Bold,
                0f, 0.5f, 0.84f, 1f);

            // Compass indicator top-right
            MakeTxt(mm.transform, "Compass", "N ↑ ",
                TextAnchor.UpperRight, 18, new Color(1f, 0.82f, 0.2f), FontStyle.Bold,
                0.5f, 1f, 0.84f, 1f);

            // RawImage for the Mapbox render texture
            var mmImg = new GameObject("MinimapImg");
            mmImg.transform.SetParent(mm.transform, false);
            var mmRT = mmImg.AddComponent<RectTransform>();
            mmRT.anchorMin = new Vector2(0.02f, 0.02f);
            mmRT.anchorMax = new Vector2(0.98f, 0.84f);
            mmRT.offsetMin = mmRT.offsetMax = Vector2.zero;
            _minimapImg = mmImg.AddComponent<RawImage>();
            _minimapImg.color = Color.white;

            return screen;
        }

        // ── List Management ──────────────────────────────────────────────────

        void RebuildList(List<(string name, string desc)> items, Action<int> onClickIdx)
        {
            if (!_listContent) return;
            foreach (Transform c in _listContent) Destroy(c.gameObject);
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                CreateListCard(_listContent, items[i].name, items[i].desc,
                    () => onClickIdx?.Invoke(idx));
            }
        }

        void CreateListCard(Transform parent, string name, string desc, Action onClick)
        {
            var card = new GameObject($"Card_{name}");
            card.transform.SetParent(parent, false);
            var rt = card.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 92);

            var bg = card.AddComponent<Image>();
            bg.color = C_RowBg;

            var btn = card.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor      = C_RowBg;
            cols.highlightedColor = new Color(0.22f, 0.24f, 0.27f, 1f);
            cols.pressedColor     = new Color(C_Blue.r, C_Blue.g, C_Blue.b, 0.25f);
            btn.colors = cols;
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick?.Invoke());

            // Blue left accent strip
            var strip = new GameObject("Strip");
            strip.transform.SetParent(card.transform, false);
            var sRT = strip.AddComponent<RectTransform>();
            sRT.anchorMin = Vector2.zero; sRT.anchorMax = new Vector2(0f, 1f);
            sRT.offsetMin = Vector2.zero; sRT.offsetMax = new Vector2(4f, 0f);
            strip.AddComponent<Image>().color = C_Blue;

            // Pin icon background circle
            var ic = new GameObject("Icon");
            ic.transform.SetParent(card.transform, false);
            var icRT = ic.AddComponent<RectTransform>();
            icRT.anchorMin = new Vector2(0f, 0.5f);
            icRT.anchorMax = new Vector2(0f, 0.5f);
            icRT.pivot     = new Vector2(0f, 0.5f);
            icRT.anchoredPosition = new Vector2(18f, 0f);
            icRT.sizeDelta        = new Vector2(42f, 42f);
            ic.AddComponent<Image>().color = new Color(C_Blue.r, C_Blue.g, C_Blue.b, 0.18f);

            // Pin emoji inside circle
            var icTxtGo = new GameObject("IcTxt");
            icTxtGo.transform.SetParent(ic.transform, false);
            var icTxtRT = icTxtGo.AddComponent<RectTransform>();
            icTxtRT.anchorMin = Vector2.zero; icTxtRT.anchorMax = Vector2.one;
            icTxtRT.offsetMin = icTxtRT.offsetMax = Vector2.zero;
            var icTxt = icTxtGo.AddComponent<Text>();
            icTxt.text = "📍"; icTxt.font = _font;
            icTxt.alignment = TextAnchor.MiddleCenter; icTxt.fontSize = 20;
            icTxt.color = C_Blue;

            // Location name
            MakeTxtOnRT(card.transform, "Name", name,
                TextAnchor.LowerLeft, 28, C_TextHi, FontStyle.Bold,
                0.13f, 0.88f, 0.44f, 0.92f);

            // Description
            MakeTxtOnRT(card.transform, "Desc", desc,
                TextAnchor.UpperLeft, 20, C_TextLo, FontStyle.Normal,
                0.13f, 0.88f, 0.08f, 0.44f);

            // Chevron arrow right
            MakeTxtOnRT(card.transform, "Chevron", "›",
                TextAnchor.MiddleCenter, 48, C_Blue, FontStyle.Bold,
                0.88f, 1f, 0f, 1f);

            // Bottom divider
            var div = new GameObject("Div");
            div.transform.SetParent(card.transform, false);
            var divRT = div.AddComponent<RectTransform>();
            divRT.anchorMin = new Vector2(0.13f, 0f);
            divRT.anchorMax = new Vector2(1f, 0f);
            divRT.offsetMin = Vector2.zero;
            divRT.offsetMax = new Vector2(0f, 1f);
            div.AddComponent<Image>().color = C_Divider;
        }

        // ── Factory Helpers ──────────────────────────────────────────────────

        /// <summary>Create a panel (Image) with anchor-based positioning.</summary>
        GameObject MakePanel(Transform parent, string n, Color col,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject(n);
            go.transform.SetParent(parent, false);
            RT(go, xMin, xMax, yMin, yMax);
            if (col.a > 0.001f) go.AddComponent<Image>().color = col;
            return go;
        }

        /// <summary>Create a Text component with anchor-based positioning.</summary>
        Text MakeTxt(Transform parent, string n, string content, TextAnchor anchor,
            int size, Color col, FontStyle style,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject(n);
            go.transform.SetParent(parent, false);
            RT(go, xMin, xMax, yMin, yMax);
            return ApplyText(go, content, anchor, size, col, style);
        }

        /// <summary>Same as MakeTxt but positioning is set by caller after return.</summary>
        void MakeTxtOnRT(Transform parent, string n, string content, TextAnchor anchor,
            int size, Color col, FontStyle style,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject(n);
            go.transform.SetParent(parent, false);
            RT(go, xMin, xMax, yMin, yMax);
            ApplyText(go, content, anchor, size, col, style);
        }

        Text ApplyText(GameObject go, string content, TextAnchor anchor,
            int size, Color col, FontStyle style)
        {
            var txt = go.AddComponent<Text>();
            txt.text      = content;
            txt.alignment = anchor;
            txt.fontSize  = size;
            txt.color     = col;
            txt.fontStyle = style;
            txt.font      = _font;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow   = VerticalWrapMode.Overflow;
            return txt;
        }

        /// <summary>Create a Button with label text.</summary>
        Button MakeBtn(Transform parent, string n, string label,
            Color bg, Color fg, int fontSize,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go = MakePanel(parent, n, bg, xMin, xMax, yMin, yMax);
            var img = go.GetComponent<Image>();
            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor      = bg;
            cols.highlightedColor = new Color(
                Mathf.Min(bg.r * 1.2f, 1f), Mathf.Min(bg.g * 1.2f, 1f),
                Mathf.Min(bg.b * 1.2f, 1f), bg.a);
            cols.pressedColor = new Color(bg.r * 0.78f, bg.g * 0.78f, bg.b * 0.78f, bg.a);
            btn.colors = cols;
            btn.targetGraphic = img;

            // Button label
            var lGo = new GameObject("Lbl");
            lGo.transform.SetParent(go.transform, false);
            var lRT = lGo.AddComponent<RectTransform>();
            lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
            lRT.offsetMin = lRT.offsetMax = Vector2.zero;
            var lTxt = lGo.AddComponent<Text>();
            lTxt.text      = label;
            lTxt.alignment = TextAnchor.MiddleCenter;
            lTxt.fontSize  = fontSize;
            lTxt.color     = fg;
            lTxt.fontStyle = FontStyle.Bold;
            lTxt.font      = _font;
            lTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            return btn;
        }

        /// <summary>Create an InputField with placeholder text.</summary>
        InputField MakeInputField(Transform parent,
            float xMin, float xMax, float yMin, float yMax, string placeholder)
        {
            var go = new GameObject("IF");
            go.transform.SetParent(parent, false);
            RT(go, xMin, xMax, yMin, yMax);
            go.AddComponent<Image>().color = Color.clear;

            // Placeholder
            var phGo = new GameObject("Ph");
            phGo.transform.SetParent(go.transform, false);
            RT(phGo, 0f, 1f, 0f, 1f);
            var ph = phGo.AddComponent<Text>();
            ph.text = placeholder; ph.color = C_TextLo;
            ph.fontSize = 26; ph.font = _font;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Input text
            var tGo = new GameObject("Txt");
            tGo.transform.SetParent(go.transform, false);
            RT(tGo, 0f, 1f, 0f, 1f);
            var txt = tGo.AddComponent<Text>();
            txt.color = C_TextHi; txt.fontSize = 26; txt.font = _font;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;

            var iF = go.AddComponent<InputField>();
            iF.placeholder    = ph;
            iF.textComponent  = txt;
            iF.caretColor     = C_Blue;
            iF.caretBlinkRate = 0.85f;
            return iF;
        }

        /// <summary>Create a vertical ScrollView and return the content Transform.</summary>
        Transform MakeScrollView(Transform parent,
            float xMin, float xMax, float yMin, float yMax)
        {
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(parent, false);
            RT(scrollGo, xMin, xMax, yMin, yMax);
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;

            var vp = new GameObject("Viewport");
            vp.transform.SetParent(scrollGo.transform, false);
            RT(vp, 0f, 1f, 0f, 1f);
            vp.AddComponent<Image>().color = Color.clear;
            var mask = vp.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            var cRT = content.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0f, 1f);
            cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot     = new Vector2(0.5f, 1f);
            cRT.offsetMin = cRT.offsetMax = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing            = 6;
            vlg.padding            = new RectOffset(0, 0, 4, 4);
            vlg.childControlWidth  = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            content.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            sr.content  = cRT;
            sr.viewport = vp.GetComponent<RectTransform>();
            return content.transform;
        }

        // ── Low-level helpers ────────────────────────────────────────────────

        static void RT(GameObject go, float xMin, float xMax, float yMin, float yMax)
        {
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Pad(GameObject go, float h, float v)
        {
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.offsetMin = new Vector2(h, v); rt.offsetMax = new Vector2(-h, -v);
        }

        static void AddOutline(GameObject go, Color color, Vector2 dist)
        {
            var o = go.AddComponent<Outline>();
            o.effectColor    = color;
            o.effectDistance = dist;
        }

        Font GetFont()
        {
            // Unity 2021+ uses "LegacyRuntime.ttf", older versions use "Arial.ttf"
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }
        }

        static string FmtDist(float meters)
        {
            if (meters < 0) return "--";
            return meters < 1000 ? $"{Mathf.RoundToInt(meters)} m" : $"{(meters / 1000f):F1} km";
        }

        IEnumerator ClearSuccessAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_successText) _successText.gameObject.SetActive(false);
        }
    }
}
