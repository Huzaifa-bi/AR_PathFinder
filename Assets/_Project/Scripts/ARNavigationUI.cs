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
    public enum RouteDeviationLevel
    {
        None = 0,
        Warning = 1,
        Recalculating = 2
    }

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
        /// <summary>Screen-locked AR “stat” card (replaces unreliable world-space sign board).</summary>

        /// <summary>Fraction of safe-area height for the top instruction strip (compact chrome).</summary>
        const float NavTopBand = 0.125f;
        /// <summary>Fraction of safe-area height for the bottom destination sheet.</summary>
        const float NavBottomBand = 0.22f;

        // ── Public Events (MenuController subscribes to these) ───────────────
        public event Action<string> OnSearchRequested;
        public event Action<int>    OnLocationSelected;
        public event Action<int>    OnSearchResultSelected;
        public event Action         OnEndNavigation;
        public event Action<string> OnSearchTextChanged;  // live filtering as user types
        /// <summary>User confirmed route on 2D map — start AR guidance, chevrons, live nav.</summary>
        public event Action         OnStartARNavigation;
        /// <summary>Leave route preview without starting AR.</summary>
        public event Action         OnCancelRoutePreview;
        /// <summary>User requests a new walking route from current GPS to the same destination.</summary>
        public event Action         OnRerouteRequested;

        // ── Internal UI References ───────────────────────────────────────────
        Canvas      _canvas;
        GameObject  _searchScreen;
        GameObject  _navScreen;
        GameObject  _routePreviewScreen;

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
        Text        _navGuideFooter;
        Button      _rerouteBtn;
        RawImage    _minimapImg;
        RectTransform _minimapPanelRt;
        RectTransform _minimapImgRt;
        AspectRatioFitter _minimapAspectFitter;
        RectTransform _navBannerRt;
        RectTransform _navCardRt;
        GameObject _offRouteBanner;
        Text _offRouteTitle;
        Text _offRouteDetail;
        RouteDeviationLevel _deviationLevel = RouteDeviationLevel.None;

        RawImage    _previewMapImg;
        AspectRatioFitter _previewMapAspectFitter;
        Text        _previewDestText;
        Text        _previewDistText;

        Font  _font;
        bool  _built = false;
        Vector2 _lastScreenPx;

        // Cache to restore after search
        List<(string name, string desc, float dist)> _cachedLocationsExt = new List<(string, string, float)>();
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
            if (minimapTexture != null)
            {
                if (_minimapImg != null) _minimapImg.texture = minimapTexture;
                if (_previewMapImg != null) _previewMapImg.texture = minimapTexture;
            }
            RefreshNavChromeLayout();
            RefreshAllMapAspects();

            // Pre-populate with major locations so the list isn't blank on start.
            // MenuController will refresh this with real distances shortly.
            SetLocationsList(new List<(string, string)>
            {
                ("C-Block",         "PIEAS C Block"),
                ("D-Block",         "PIEAS D Block"),
                ("Central Library", "PIEAS Library"),
                ("Auditorium",      "Inaam-ur-Rehman Auditorium"),
                ("DNE",             "Dept. of Nuclear Engineering"),
            });
            Debug.Log($"[ARNavigationUI] Initialized with {_cachedLocations.Count} locations");
        }

        public void ShowSearchScreen()
        {
            if (!_built) return;
            if (_searchScreen) _searchScreen.SetActive(true);
            if (_navScreen)    _navScreen.SetActive(false);
            if (_routePreviewScreen) _routePreviewScreen.SetActive(false);
            RefreshNavChromeLayout();
        }

        public void ShowNavScreen()
        {
            if (!_built) return;
            if (_searchScreen) _searchScreen.SetActive(false);
            if (_routePreviewScreen) _routePreviewScreen.SetActive(false);
            if (_navScreen)    _navScreen.SetActive(true);
            ClearRouteDeviation();
            RefreshNavChromeLayout();
        }

        /// <summary>Show full-route 2D map; user must tap Start before AR overlays run.</summary>
        public void ShowRoutePreview(string destName, float distMeters)
        {
            if (!_built) return;
            if (_searchScreen) _searchScreen.SetActive(false);
            if (_navScreen) _navScreen.SetActive(false);
            if (_routePreviewScreen) _routePreviewScreen.SetActive(true);
            if (_previewDestText) _previewDestText.text = destName ?? "Destination";
            if (_previewDistText) _previewDistText.text = FmtDist(distMeters);
            RefreshAllMapAspects();
            RefreshNavChromeLayout();
        }

        public void SetMinimapTexture(Texture tex)
        {
            if (_minimapImg) _minimapImg.texture = tex;
            if (_previewMapImg) _previewMapImg.texture = tex;
            RefreshMinimapLayout();
            RefreshAllMapAspects();
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
            if (_cachedLocationsExt.Count > 0)
                RebuildListWithDistance(_cachedLocationsExt, i => OnLocationSelected?.Invoke(i));
            else
                RebuildList(_cachedLocations, i => OnLocationSelected?.Invoke(i));
        }

        /// <summary>Populate the list with distance data (replaces SetLocationsList).</summary>
        public void SetLocationsListWithDistance(List<(string name, string desc, float distMeters)> locations)
        {
            _cachedLocationsExt = locations ?? new List<(string, string, float)>();
            // Also keep the simple cache in sync
            _cachedLocations.Clear();
            foreach (var l in _cachedLocationsExt)
                _cachedLocations.Add((l.name, l.desc));
            RebuildListWithDistance(_cachedLocationsExt, i => OnLocationSelected?.Invoke(i));
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
            string distStr = distToTurnMeters >= 0 ? FmtDist(distToTurnMeters) : "";
            if (_arrowText) _arrowText.text = arrow;
            if (_instrText) _instrText.text = instruction;
            if (_instrDistText) _instrDistText.text = distStr;
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

        /// <summary>Status line above End / Re-route (off-route hint, re-route progress).</summary>
        public void SetGuidanceFooter(string message)
        {
            if (!_built || _navGuideFooter == null) return;
            bool show = !string.IsNullOrEmpty(message);
            _navGuideFooter.gameObject.SetActive(show);
            if (show) _navGuideFooter.text = message;
        }

        public void ClearGuidanceFooter() => SetGuidanceFooter(null);

        /// <summary>Prominent banner when the user leaves the campus route.</summary>
        public void SetRouteDeviation(RouteDeviationLevel level, float metersOffRoute, bool recalculating)
        {
            if (!_built) return;
            _deviationLevel = level;

            if (_offRouteBanner == null) return;

            if (level == RouteDeviationLevel.None)
            {
                _offRouteBanner.SetActive(false);
                ClearGuidanceFooter();
                return;
            }

            _offRouteBanner.SetActive(true);

            if (level == RouteDeviationLevel.Warning)
            {
                if (_offRouteTitle) _offRouteTitle.text = "Off route";
                if (_offRouteDetail)
                {
                    string dist = metersOffRoute >= 0 ? FmtDist(metersOffRoute) : "";
                    _offRouteDetail.text = string.IsNullOrEmpty(dist)
                        ? "Return to the white arrows on the road, or tap Re-route."
                        : $"You are {dist} from the path. Head back to the white arrows or tap Re-route.";
                }
                SetGuidanceFooter("Off route — follow the arrows back to the road");
            }
            else
            {
                if (_offRouteTitle) _offRouteTitle.text = recalculating ? "Recalculating…" : "Off route";
                if (_offRouteDetail)
                {
                    _offRouteDetail.text = recalculating
                        ? "Updating your route for PIEAS campus roads…"
                        : "You are far from the path. Tap Re-route if this continues.";
                }
                SetGuidanceFooter(recalculating ? "Recalculating route…" : "Far off route — tap Re-route");
            }
        }

        public void ClearRouteDeviation() => SetRouteDeviation(RouteDeviationLevel.None, -1f, false);

        public void SetRerouteButtonInteractable(bool interactable)
        {
            if (_built && _rerouteBtn != null)
                _rerouteBtn.interactable = interactable;
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
            _canvas.sortingOrder = 400;  // above AR + world UI so HUD never hides behind the scene
            _canvas.pixelPerfect = false;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            _searchScreen = BuildSearchScreen(root.transform);
            _navScreen    = BuildNavScreen(root.transform);
            _routePreviewScreen = BuildRoutePreviewScreen(root.transform);
            _navScreen.SetActive(false);
            _routePreviewScreen.SetActive(false);
        }

        GameObject BuildRoutePreviewScreen(Transform parent)
        {
            var screen = MakePanel(parent, "RoutePreview",
                new Color(0.07f, 0.075f, 0.082f, 0.98f), 0f, 1f, 0f, 1f);

            MakeTxt(screen.transform, "PrevTitle", "Route overview",
                TextAnchor.MiddleLeft, 32, C_TextHi, FontStyle.Bold,
                0.04f, 0.7f, 0.91f, 0.99f);
            MakeTxt(screen.transform, "PrevSub", "Check the path, then start AR navigation",
                TextAnchor.MiddleLeft, 22, C_TextLo, FontStyle.Normal,
                0.04f, 0.95f, 0.86f, 0.905f);

            var mapFrame = MakePanel(screen.transform, "PreviewMapFrame",
                C_CardBg, 0.04f, 0.96f, 0.30f, 0.84f);
            AddOutline(mapFrame, new Color(C_Blue.r, C_Blue.g, C_Blue.b, 0.45f), new Vector2(1.5f, 1.5f));

            var imgGo = new GameObject("PreviewMapImg");
            imgGo.transform.SetParent(mapFrame.transform, false);
            var imgRt = imgGo.AddComponent<RectTransform>();
            imgRt.anchorMin = new Vector2(0.02f, 0.02f);
            imgRt.anchorMax = new Vector2(0.98f, 0.98f);
            imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;
            _previewMapImg = imgGo.AddComponent<RawImage>();
            _previewMapImg.color = Color.white;
            _previewMapImg.raycastTarget = false;
            _previewMapAspectFitter = imgGo.AddComponent<AspectRatioFitter>();
            _previewMapAspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _previewMapAspectFitter.aspectRatio = 1f;

            _previewDestText = MakeTxt(screen.transform, "PrevDest", "Destination",
                TextAnchor.MiddleLeft, 34, C_TextHi, FontStyle.Bold,
                0.06f, 0.94f, 0.20f, 0.28f);
            _previewDistText = MakeTxt(screen.transform, "PrevDist", "--",
                TextAnchor.MiddleLeft, 26, C_Blue, FontStyle.Normal,
                0.06f, 0.94f, 0.125f, 0.195f);

            var startBtn = MakeBtn(screen.transform, "StartARBtn", "▶  Start AR navigation",
                C_Green, Color.white, 30, 0.06f, 0.94f, 0.045f, 0.115f);
            startBtn.onClick.AddListener(() => OnStartARNavigation?.Invoke());

            var backBtn = MakeBtn(screen.transform, "PreviewBackBtn", "←  Back to search",
                new Color(0.28f, 0.30f, 0.34f, 1f), C_TextHi, 24, 0.06f, 0.94f, 0.005f, 0.038f);
            backBtn.onClick.AddListener(() => OnCancelRoutePreview?.Invoke());

            return screen;
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
            _searchInput.onValueChanged.AddListener(OnSearchInputChanged);

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

            // ─ Instruction Banner (compact top strip — final anchors from ApplySafeAreaToNavChrome) ─
            var banner = MakePanel(screen.transform, "Banner", C_PanelBg, 0, 1, 0.88f, 1f);
            _navBannerRt = banner.GetComponent<RectTransform>();

            _arrowText = MakeTxt(banner.transform, "Arrow", "↑",
                TextAnchor.MiddleCenter, 54, C_Blue, FontStyle.Bold,
                0f, 0.14f, 0f, 1f);

            MakePanel(banner.transform, "VDiv", C_Divider, 0.14f, 0.145f, 0.10f, 0.90f);

            var instrClip = new GameObject("InstrClip", typeof(RectTransform));
            instrClip.transform.SetParent(banner.transform, false);
            var clipRt = instrClip.GetComponent<RectTransform>();
            clipRt.anchorMin = new Vector2(0.16f, 0.40f);
            clipRt.anchorMax = new Vector2(0.98f, 1f);
            clipRt.offsetMin = clipRt.offsetMax = Vector2.zero;
            instrClip.AddComponent<RectMask2D>();

            _instrText = MakeTxt(instrClip.transform, "InstrTxt", "Follow the AR path",
                TextAnchor.UpperLeft, 26, C_TextHi, FontStyle.Bold,
                0f, 1f, 0f, 1f);
            _instrText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _instrText.verticalOverflow = VerticalWrapMode.Truncate;

            _instrDistText = MakeTxt(banner.transform, "InstrDist", "",
                TextAnchor.MiddleLeft, 20, C_Blue, FontStyle.Normal,
                0.16f, 0.98f, 0f, 0.38f);

            // Off-route alert (center of screen, above minimap)
            _offRouteBanner = MakePanel(screen.transform, "OffRouteBanner",
                new Color(0.55f, 0.18f, 0.12f, 0.94f), 0.05f, 0.95f, 0.52f, 0.62f);
            AddOutline(_offRouteBanner, C_Red, new Vector2(2f, 2f));
            _offRouteTitle = MakeTxt(_offRouteBanner.transform, "OffTitle", "Off route",
                TextAnchor.UpperLeft, 28, Color.white, FontStyle.Bold,
                0.05f, 0.95f, 0.48f, 0.95f);
            _offRouteDetail = MakeTxt(_offRouteBanner.transform, "OffDetail", "",
                TextAnchor.UpperLeft, 20, new Color(1f, 0.9f, 0.85f), FontStyle.Normal,
                0.05f, 0.95f, 0.08f, 0.46f);
            _offRouteDetail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _offRouteBanner.SetActive(false);

            // ─ Bottom Navigation Card (compact) ─
            var card = MakePanel(screen.transform, "NavCard", C_CardBg, 0, 1, 0f, 0.22f);
            _navCardRt = card.GetComponent<RectTransform>();
            AddOutline(card, new Color(1, 1, 1, 0.06f), new Vector2(0, 2));

            // Blue accent strip at top of card
            MakePanel(card.transform, "BlueAccent", C_Blue, 0f, 1f, 0.985f, 1f);

            _destText = MakeTxt(card.transform, "DestName", "Destination",
                TextAnchor.UpperLeft, 30, C_TextHi, FontStyle.Bold,
                0.05f, 0.95f, 0.56f, 0.90f);

            _distText = MakeTxt(card.transform, "DistRemain", "-- m",
                TextAnchor.MiddleLeft, 24, C_Blue, FontStyle.Normal,
                0.05f, 0.48f, 0.34f, 0.52f);

            _etaText = MakeTxt(card.transform, "ETA", "-- min",
                TextAnchor.MiddleRight, 22, C_TextLo, FontStyle.Normal,
                0.48f, 0.95f, 0.34f, 0.52f);

            _navGuideFooter = MakeTxt(card.transform, "GuideFooter", "",
                TextAnchor.MiddleLeft, 16, new Color(1f, 0.72f, 0.35f, 1f), FontStyle.Normal,
                0.05f, 0.95f, 0.20f, 0.32f);
            _navGuideFooter.horizontalOverflow = HorizontalWrapMode.Wrap;
            _navGuideFooter.verticalOverflow = VerticalWrapMode.Truncate;
            _navGuideFooter.gameObject.SetActive(false);

            MakePanel(card.transform, "HDiv", C_Divider, 0.05f, 0.95f, 0.188f, 0.195f);

            _rerouteBtn = MakeBtn(card.transform, "RerouteBtn", "↻  Re-route",
                C_Green, Color.white, 18, 0.05f, 0.47f, 0.04f, 0.175f);
            _rerouteBtn.onClick.AddListener(() => OnRerouteRequested?.Invoke());

            var endBtn = MakeBtn(card.transform, "EndNavBtn", "✕  End navigation",
                C_Red, Color.white, 20, 0.53f, 0.95f, 0.04f, 0.175f);
            endBtn.onClick.AddListener(() => OnEndNavigation?.Invoke());

            // ─ Minimap PIP: square frame + aspect-correct map (no stretch) ─
            var mm = MakePanel(screen.transform, "Minimap",
                new Color(0.05f, 0.06f, 0.07f, 0.95f),
                0f, 0f, 0f, 0f);
            _minimapPanelRt = mm.GetComponent<RectTransform>();
            _minimapPanelRt.anchorMin = Vector2.zero;
            _minimapPanelRt.anchorMax = Vector2.zero;
            _minimapPanelRt.pivot = new Vector2(0f, 0f);
            _minimapPanelRt.sizeDelta = new Vector2(240f, 240f);
            _minimapPanelRt.anchoredPosition = new Vector2(14f, 14f);
            AddOutline(mm, new Color(C_Blue.r, C_Blue.g, C_Blue.b, 0.7f), new Vector2(2f, 2f));

            MakeTxt(mm.transform, "MapLbl", " MAP",
                TextAnchor.UpperLeft, 18, C_TextLo, FontStyle.Bold,
                0f, 0.45f, 0.88f, 1f);

            MakeTxt(mm.transform, "Compass", "N ↑ ",
                TextAnchor.UpperRight, 18, new Color(1f, 0.82f, 0.2f), FontStyle.Bold,
                0.55f, 1f, 0.88f, 1f);

            var mmImg = new GameObject("MinimapImg");
            mmImg.transform.SetParent(mm.transform, false);
            _minimapImgRt = mmImg.AddComponent<RectTransform>();
            _minimapImgRt.anchorMin = new Vector2(0.02f, 0.02f);
            _minimapImgRt.anchorMax = new Vector2(0.98f, 0.86f);
            _minimapImgRt.offsetMin = _minimapImgRt.offsetMax = Vector2.zero;
            _minimapImg = mmImg.AddComponent<RawImage>();
            _minimapImg.color = Color.white;
            _minimapImg.raycastTarget = false;
            _minimapAspectFitter = mmImg.AddComponent<AspectRatioFitter>();
            _minimapAspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _minimapAspectFitter.aspectRatio = 1f;

            return screen;
        }

        /// <summary>Re-apply safe-area insets + minimap size/aspect after rotation or texture change.</summary>
        void RefreshNavChromeLayout()
        {
            if (!_built || _canvas == null) return;
            ApplySafeAreaToNavChrome();
            RefreshMinimapLayout();
            _lastScreenPx = new Vector2(Screen.width, Screen.height);
        }

        void ApplySafeAreaToNavChrome()
        {
            if (_canvas == null) return;

            float sw = Mathf.Max(1f, Screen.width);
            float sh = Mathf.Max(1f, Screen.height);
            Rect safe = Screen.safeArea;

            float nx0 = Mathf.Clamp01(safe.xMin / sw);
            float nx1 = Mathf.Clamp01(safe.xMax / sw);
            float ny0 = Mathf.Clamp01(safe.yMin / sh);
            float ny1 = Mathf.Clamp01(safe.yMax / sh);
            if (nx1 <= nx0 + 0.01f || ny1 <= ny0 + 0.01f)
            {
                nx0 = 0f; nx1 = 1f; ny0 = 0f; ny1 = 1f;
            }

            const float pad = 8f;

            if (_navBannerRt != null)
            {
                _navBannerRt.anchorMin = new Vector2(nx0, Mathf.Lerp(ny0, ny1, 1f - NavTopBand));
                _navBannerRt.anchorMax = new Vector2(nx1, ny1);
                _navBannerRt.offsetMin = new Vector2(pad, 0f);
                _navBannerRt.offsetMax = new Vector2(-pad, -4f);
            }

            if (_navCardRt != null)
            {
                _navCardRt.anchorMin = new Vector2(nx0, ny0);
                _navCardRt.anchorMax = new Vector2(nx1, Mathf.Lerp(ny0, ny1, NavBottomBand));
                _navCardRt.offsetMin = new Vector2(pad, Mathf.Max(6f, pad));
                _navCardRt.offsetMax = new Vector2(-pad, -Mathf.Max(4f, pad * 0.5f));
            }
        }

        void RefreshMinimapLayout()
        {
            if (_minimapPanelRt == null || _canvas == null) return;
            var rootRt = _canvas.transform as RectTransform;
            if (rootRt == null) return;

            float h = rootRt.rect.height;
            float w = rootRt.rect.width;
            float side = Mathf.Clamp(Mathf.Min(w, h) * 0.34f, 188f, 360f);
            _minimapPanelRt.sizeDelta = new Vector2(side, side);

            float sw = Mathf.Max(1f, Screen.width);
            float sh = Mathf.Max(1f, Screen.height);
            float nx0 = Screen.safeArea.xMin / sw;
            float ny0 = Screen.safeArea.yMin / sh;
            float padX = w * nx0 + 14f;
            float bottomLift = h * ny0 + h * (NavBottomBand + 0.02f) + 10f;
            _minimapPanelRt.anchoredPosition = new Vector2(Mathf.Max(12f, padX), bottomLift);

            RefreshAllMapAspects();
        }

        void RefreshAllMapAspects()
        {
            void SetAspect(AspectRatioFitter fit, Texture t)
            {
                if (fit == null || t == null) return;
                fit.aspectRatio = (float)t.width / Mathf.Max(1, t.height);
            }
            SetAspect(_minimapAspectFitter, _minimapImg != null ? _minimapImg.texture : null);
            SetAspect(_previewMapAspectFitter, _previewMapImg != null ? _previewMapImg.texture : null);
        }

        void LateUpdate()
        {
            if (!_built) return;
            bool chrome = (_navScreen != null && _navScreen.activeSelf)
                          || (_routePreviewScreen != null && _routePreviewScreen.activeSelf);
            if (!chrome) return;
            var px = new Vector2(Screen.width, Screen.height);
            if ((px - _lastScreenPx).sqrMagnitude > 0.5f)
                RefreshNavChromeLayout();
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

        /// <summary>Live filter: called on every keystroke in the search box.</summary>
        void OnSearchInputChanged(string text)
        {
            OnSearchTextChanged?.Invoke(text);

            string q = (text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q))
            {
                // Empty query — show all locations (with distances if available)
                if (_cachedLocationsExt.Count > 0)
                    RebuildListWithDistance(_cachedLocationsExt, i => OnLocationSelected?.Invoke(i));
                else
                    RebuildList(_cachedLocations, i => OnLocationSelected?.Invoke(i));
                return;
            }

            // Filter cached locations by name or description
            var filteredIndices = new List<int>();
            for (int i = 0; i < _cachedLocations.Count; i++)
            {
                if (_cachedLocations[i].name.ToLowerInvariant().Contains(q)
                 || _cachedLocations[i].desc.ToLowerInvariant().Contains(q))
                {
                    filteredIndices.Add(i);
                }
            }

            if (filteredIndices.Count > 0)
            {
                // Rebuild list with only matching items, mapping clicks to original indices
                if (!_listContent) return;
                foreach (Transform c in _listContent) Destroy(c.gameObject);
                for (int i = 0; i < filteredIndices.Count; i++)
                {
                    int origIdx = filteredIndices[i];
                    string n = _cachedLocations[origIdx].name;
                    string d = _cachedLocations[origIdx].desc;

                    if (_cachedLocationsExt.Count > origIdx)
                    {
                        string icon = GetBuildingIcon(n);
                        string distLabel = _cachedLocationsExt[origIdx].dist >= 0
                            ? FmtDist(_cachedLocationsExt[origIdx].dist) : "";
                        CreateListCardWithDistance(_listContent, n, d, icon, distLabel,
                            () => OnLocationSelected?.Invoke(origIdx));
                    }
                    else
                    {
                        CreateListCard(_listContent, n, d,
                            () => OnLocationSelected?.Invoke(origIdx));
                    }
                }
            }
            else
            {
                // No matches — show "No matching locations" message
                if (!_listContent) return;
                foreach (Transform c in _listContent) Destroy(c.gameObject);
                var noResult = new GameObject("NoResult", typeof(RectTransform));
                noResult.transform.SetParent(_listContent, false);
                noResult.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 80);
                var txt = noResult.AddComponent<Text>();
                txt.text = $"No locations matching \"{text}\"";
                txt.font = _font; txt.fontSize = 24;
                txt.color = C_TextLo; txt.alignment = TextAnchor.MiddleCenter;
            }
        }

        /// <summary>Rebuild the list with distance-aware cards.</summary>
        void RebuildListWithDistance(List<(string name, string desc, float dist)> items, System.Action<int> onClickIdx)
        {
            if (!_listContent) return;
            foreach (Transform c in _listContent) Destroy(c.gameObject);
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                string icon = GetBuildingIcon(items[i].name);
                string distLabel = items[i].dist >= 0 ? FmtDist(items[i].dist) : "";
                CreateListCardWithDistance(_listContent, items[i].name, items[i].desc,
                    icon, distLabel, () => onClickIdx?.Invoke(idx));
            }
            Debug.Log($"[ARNavigationUI] List rebuilt with {items.Count} items");
        }

        /// <summary>Pick an emoji icon based on location name.</summary>
        static string GetBuildingIcon(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("library"))       return "📚";
            if (n.Contains("auditorium"))    return "🎭";
            if (n.Contains("mosque") || n.Contains("masjid")) return "🕌";
            if (n.Contains("hostel"))        return "🏨";
            if (n.Contains("cafeteria") || n.Contains("canteen")) return "🍽️";
            if (n.Contains("lab"))           return "🔬";
            if (n.Contains("gate") || n.Contains("entrance")) return "🚪";
            if (n.Contains("ground") || n.Contains("field") || n.Contains("court")) return "⚽";
            return "🏢"; // default building icon
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

        /// <summary>Create a list card with building icon and distance label.</summary>
        void CreateListCardWithDistance(Transform parent, string name, string desc,
            string icon, string distLabel, System.Action onClick)
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

            // Building icon background circle
            var ic = new GameObject("Icon");
            ic.transform.SetParent(card.transform, false);
            var icRT = ic.AddComponent<RectTransform>();
            icRT.anchorMin = new Vector2(0f, 0.5f);
            icRT.anchorMax = new Vector2(0f, 0.5f);
            icRT.pivot     = new Vector2(0f, 0.5f);
            icRT.anchoredPosition = new Vector2(18f, 0f);
            icRT.sizeDelta        = new Vector2(42f, 42f);
            ic.AddComponent<Image>().color = new Color(C_Blue.r, C_Blue.g, C_Blue.b, 0.18f);

            // Building type icon inside circle
            var icTxtGo = new GameObject("IcTxt");
            icTxtGo.transform.SetParent(ic.transform, false);
            var icTxtRT = icTxtGo.AddComponent<RectTransform>();
            icTxtRT.anchorMin = Vector2.zero; icTxtRT.anchorMax = Vector2.one;
            icTxtRT.offsetMin = icTxtRT.offsetMax = Vector2.zero;
            var icTxt = icTxtGo.AddComponent<Text>();
            icTxt.text = icon; icTxt.font = _font;
            icTxt.alignment = TextAnchor.MiddleCenter; icTxt.fontSize = 20;
            icTxt.color = C_Blue;

            // Location name
            MakeTxtOnRT(card.transform, "Name", name,
                TextAnchor.LowerLeft, 28, C_TextHi, FontStyle.Bold,
                0.13f, 0.72f, 0.44f, 0.92f);

            // Distance badge (top-right)
            if (!string.IsNullOrEmpty(distLabel))
            {
                MakeTxtOnRT(card.transform, "Dist", distLabel,
                    TextAnchor.MiddleRight, 20, C_Blue, FontStyle.Bold,
                    0.72f, 0.88f, 0.50f, 0.92f);
            }

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
            var go = new GameObject(n, typeof(RectTransform));
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
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RT(go, xMin, xMax, yMin, yMax);
            return ApplyText(go, content, anchor, size, col, style);
        }

        /// <summary>Same as MakeTxt but positioning is set by caller after return.</summary>
        void MakeTxtOnRT(Transform parent, string n, string content, TextAnchor anchor,
            int size, Color col, FontStyle style,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject(n, typeof(RectTransform));
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
            var go = new GameObject("IF", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RT(go, xMin, xMax, yMin, yMax);
            go.AddComponent<Image>().color = Color.clear;

            // Placeholder
            var phGo = new GameObject("Ph", typeof(RectTransform));
            phGo.transform.SetParent(go.transform, false);
            RT(phGo, 0f, 1f, 0f, 1f);
            var ph = phGo.AddComponent<Text>();
            ph.text = placeholder; ph.color = C_TextLo;
            ph.fontSize = 26; ph.font = _font;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Input text
            var tGo = new GameObject("Txt", typeof(RectTransform));
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
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            RT(scrollGo, xMin, xMax, yMin, yMax);
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;

            var vp = new GameObject("Viewport", typeof(RectTransform));
            vp.transform.SetParent(scrollGo.transform, false);
            RT(vp, 0f, 1f, 0f, 1f);
            // Use a tiny alpha instead of 0 to ensure the Mask component functions correctly
            vp.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f); 
            var mask = vp.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp.transform, false);
            var cRT = content.GetComponent<RectTransform>();
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
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
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
            // Try common built-in names
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            
            // Last resort: Search all loaded fonts and pick the first one
            if (f == null)
            {
                var allFonts = Resources.FindObjectsOfTypeAll<Font>();
                if (allFonts.Length > 0) f = allFonts[0];
            }

            if (f == null) Debug.LogError("[ARNavigationUI] CRITICAL: No Font found in project. UI text will be invisible!");
            return f;
        }

        static void EnsureEventSystem()
        {
            var existing = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing != null && existing.Length > 0)
                return;
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
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
