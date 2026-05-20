// ============================================================================
// ThresholdUIBuilder.cs — Editor tool to build THRESHOLD UI in scene
// Creates the exact same UI hierarchy that the runtime scripts build
// programmatically, so it's visible and editable in the Unity Editor.
//
// Usage: Menu > THRESHOLD > Build UI In Scene
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Threshold.UI.Editor
{
    public static class ThresholdUIBuilder
    {
        // ====================================================================
        // Menu Entry
        // ====================================================================

        [MenuItem("THRESHOLD/Build UI In Scene", false, 100)]
        public static void BuildAll()
        {
            // Clean existing
            var existing = GameObject.Find("THRESHOLD_UI");
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("THRESHOLD UI Builder",
                    "Existing THRESHOLD_UI found. Replace it?", "Replace", "Cancel"))
                    return;
                Undo.DestroyObjectImmediate(existing);
            }

            // Root
            var root = new GameObject("THRESHOLD_UI");
            Undo.RegisterCreatedObjectUndo(root, "Build THRESHOLD UI");

            // Canvas
            var canvas = BuildCanvas(root.transform);

            // EventSystem
            BuildEventSystem();

            // UI Manager
            var mgr = root.AddComponent<ThresholdUIManager>();

            // Joystick
            var joystickGo = new GameObject("VirtualJoystick");
            joystickGo.transform.SetParent(root.transform);
            var joystick = joystickGo.AddComponent<VirtualJoystick>();
            BuildJoystickVisuals(canvas.transform, joystick);
            mgr.joystick = joystick;

            // HUD
            var hudGo = new GameObject("GameplayHUD");
            hudGo.transform.SetParent(root.transform);
            var hud = hudGo.AddComponent<GameplayHUD>();
            BuildHUDVisuals(canvas.transform);
            mgr.hud = hud;

            // Defection Popup
            var popupGo = new GameObject("DefectionPopup");
            popupGo.transform.SetParent(root.transform);
            var popup = popupGo.AddComponent<DefectionPopup>();
            BuildDefectionPopupVisuals(canvas.transform);
            mgr.defectionPopup = popup;

            // Run Summary
            var summaryGo = new GameObject("RunSummaryScreen");
            summaryGo.transform.SetParent(root.transform);
            var summary = summaryGo.AddComponent<RunSummaryScreen>();
            BuildRunSummaryVisuals(canvas.transform);
            mgr.summaryScreen = summary;

            // TopDownCamera on main camera
            var cam = Camera.main;
            if (cam != null)
            {
                var tdc = cam.GetComponent<TopDownCamera>();
                if (tdc == null) tdc = cam.gameObject.AddComponent<TopDownCamera>();
                mgr.topDownCamera = tdc;
            }

            Selection.activeGameObject = root;
            Debug.Log("[ThresholdUIBuilder] UI hierarchy created successfully.");
        }

        // ====================================================================
        // Canvas & EventSystem
        // ====================================================================

        static Canvas BuildCanvas(Transform parent)
        {
            var obj = new GameObject("THRESHOLD_Canvas");
            obj.transform.SetParent(parent, false);

            var canvas = obj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = obj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            obj.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        static void BuildEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var obj = new GameObject("EventSystem");
            obj.AddComponent<EventSystem>();
            obj.AddComponent<StandaloneInputModule>();
        }

        // ====================================================================
        // Virtual Joystick
        // ====================================================================

        static void BuildJoystickVisuals(Transform canvasT, VirtualJoystick joystick)
        {
            // Touch capture area (left half of screen)
            var touchArea = CreateUI("Joystick_TouchArea", canvasT);
            var touchRect = Rect(touchArea);
            touchRect.anchorMin = Vector2.zero;
            touchRect.anchorMax = new Vector2(0.5f, 0.5f);
            touchRect.offsetMin = Vector2.zero;
            touchRect.offsetMax = Vector2.zero;
            var touchImg = touchArea.AddComponent<Image>();
            touchImg.color = Color.clear;
            touchImg.raycastTarget = true;
            touchArea.AddComponent<JoystickTouchProxy>();

            // Base circle
            var baseObj = CreateUI("Joystick_Base", canvasT);
            var baseRect = Rect(baseObj);
            baseRect.sizeDelta = new Vector2(200f, 200f);
            baseRect.anchorMin = Vector2.zero;
            baseRect.anchorMax = Vector2.zero;
            baseRect.pivot = new Vector2(0.5f, 0.5f);
            baseRect.anchoredPosition = new Vector2(140f, 140f);
            var baseImg = baseObj.AddComponent<Image>();
            baseImg.color = new Color(1f, 1f, 1f, 0.15f);
            baseImg.raycastTarget = false;
            ApplyCircleSprite(baseImg);

            // Knob
            var knobObj = CreateUI("Joystick_Knob", baseRect);
            var knobRect = Rect(knobObj);
            knobRect.sizeDelta = new Vector2(90f, 90f); // 200 * 0.45
            knobRect.anchoredPosition = Vector2.zero;
            var knobImg = knobObj.AddComponent<Image>();
            knobImg.color = new Color(1f, 1f, 1f, 0.5f);
            knobImg.raycastTarget = false;
            ApplyCircleSprite(knobImg);
        }

        // ====================================================================
        // Gameplay HUD
        // ====================================================================

        static void BuildHUDVisuals(Transform canvasT)
        {
            // --- Health Bar (top-left) ---
            var healthContainer = CreatePanel("HealthBar_Container", canvasT,
                new Color(0, 0, 0, 0));
            var hcRect = Rect(healthContainer);
            hcRect.anchorMin = new Vector2(0f, 1f);
            hcRect.anchorMax = new Vector2(0f, 1f);
            hcRect.pivot = new Vector2(0f, 1f);
            hcRect.anchoredPosition = new Vector2(24f, -24f);
            hcRect.sizeDelta = new Vector2(300f, 50f);

            // Health icon
            var icon = CreateText("Health_Icon", healthContainer.transform, "+", 28,
                new Color(0.9f, 0.3f, 0.3f, 1f), TextAnchor.MiddleCenter);
            var iconR = Rect(icon);
            iconR.anchorMin = new Vector2(0f, 0f);
            iconR.anchorMax = new Vector2(0f, 1f);
            iconR.pivot = new Vector2(0f, 0.5f);
            iconR.anchoredPosition = Vector2.zero;
            iconR.sizeDelta = new Vector2(30f, 0f);

            // Health bg
            var hBg = CreatePanel("HealthBar_Bg", healthContainer.transform,
                new Color(0, 0, 0, 0.6f));
            var hBgR = Rect(hBg);
            hBgR.anchorMin = new Vector2(0f, 0.15f);
            hBgR.anchorMax = new Vector2(1f, 0.85f);
            hBgR.offsetMin = new Vector2(36f, 0f);
            hBgR.offsetMax = new Vector2(-8f, 0f);

            // Health fill
            var hFill = CreatePanel("HealthBar_Fill", hBg.transform,
                new Color(0.2f, 0.9f, 0.4f, 1f));
            var hFillR = Rect(hFill);
            hFillR.anchorMin = Vector2.zero;
            hFillR.anchorMax = Vector2.one;
            hFillR.offsetMin = new Vector2(3f, 3f);
            hFillR.offsetMax = new Vector2(-3f, -3f);

            // Health text
            CreateText("Health_Text", hBg.transform, "100%", 18,
                Color.white, TextAnchor.MiddleCenter, true);

            // --- Ammo Counter (top-right) ---
            var ammoContainer = CreatePanel("Ammo_Container", canvasT,
                new Color(0, 0, 0, 0.4f));
            var acRect = Rect(ammoContainer);
            acRect.anchorMin = new Vector2(1f, 1f);
            acRect.anchorMax = new Vector2(1f, 1f);
            acRect.pivot = new Vector2(1f, 1f);
            acRect.anchoredPosition = new Vector2(-24f, -24f);
            acRect.sizeDelta = new Vector2(180f, 50f);

            var ammoTxt = CreateText("Ammo_Text", ammoContainer.transform,
                "30 / 30", 26, new Color(1f, 0.85f, 0.3f, 1f),
                TextAnchor.MiddleCenter, true);

            // --- Kill Counter (below health) ---
            var killTxt = CreateText("Kill_Text", canvasT, "KILLS: 0", 20,
                new Color(1f, 0.4f, 0.4f, 0.9f), TextAnchor.MiddleLeft);
            var killR = Rect(killTxt);
            killR.anchorMin = new Vector2(0f, 1f);
            killR.anchorMax = new Vector2(0f, 1f);
            killR.pivot = new Vector2(0f, 1f);
            killR.anchoredPosition = new Vector2(24f, -82f);
            killR.sizeDelta = new Vector2(200f, 30f);

            // --- Room Progress (top-center) ---
            var roomContainer = CreatePanel("Room_Container", canvasT,
                new Color(0, 0, 0, 0.4f));
            var rcRect = Rect(roomContainer);
            rcRect.anchorMin = new Vector2(0.5f, 1f);
            rcRect.anchorMax = new Vector2(0.5f, 1f);
            rcRect.pivot = new Vector2(0.5f, 1f);
            rcRect.anchoredPosition = new Vector2(0f, -24f);
            rcRect.sizeDelta = new Vector2(260f, 44f);

            var roomTxt = CreateText("Room_Text", roomContainer.transform,
                "ROOM 1 / 7", 18, Color.white, TextAnchor.MiddleCenter);
            var roomTxtR = Rect(roomTxt);
            roomTxtR.anchorMin = new Vector2(0f, 0.5f);
            roomTxtR.anchorMax = new Vector2(1f, 1f);
            roomTxtR.offsetMin = new Vector2(8f, 0f);
            roomTxtR.offsetMax = new Vector2(-8f, 0f);

            // Room progress bar bg
            var rpBg = CreatePanel("RoomProgress_Bg", roomContainer.transform,
                new Color(0.3f, 0.3f, 0.3f, 0.8f));
            var rpBgR = Rect(rpBg);
            rpBgR.anchorMin = new Vector2(0.05f, 0.1f);
            rpBgR.anchorMax = new Vector2(0.95f, 0.4f);
            rpBgR.offsetMin = Vector2.zero;
            rpBgR.offsetMax = Vector2.zero;

            // Room progress fill
            var rpFill = CreatePanel("RoomProgress_Fill", rpBg.transform,
                new Color(0.3f, 0.8f, 1f, 0.9f));
            var rpFillR = Rect(rpFill);
            rpFillR.anchorMin = Vector2.zero;
            rpFillR.anchorMax = new Vector2(0.5f, 1f);
            rpFillR.offsetMin = Vector2.zero;
            rpFillR.offsetMax = Vector2.zero;

            // --- Fire Button (bottom-right) ---
            var fireBtn = CreatePanel("Fire_Button", canvasT,
                new Color(0.95f, 0.25f, 0.25f, 0.6f));
            var fbRect = Rect(fireBtn);
            fbRect.anchorMin = new Vector2(1f, 0f);
            fbRect.anchorMax = new Vector2(1f, 0f);
            fbRect.pivot = new Vector2(1f, 0f);
            fbRect.anchoredPosition = new Vector2(-80f, 100f);
            fbRect.sizeDelta = new Vector2(160f, 160f);
            ApplyCircleSprite(fireBtn.GetComponent<Image>());

            var fireLabel = CreateText("Fire_Label", fireBtn.transform,
                "\u2295", 48, Color.white, TextAnchor.MiddleCenter, true);

            fireBtn.AddComponent<FireButtonHandler>();
        }

        // ====================================================================
        // Defection Popup
        // ====================================================================

        static void BuildDefectionPopupVisuals(Transform canvasT)
        {
            // Root
            var root = CreateUI("DefectionPopup_Root", canvasT);
            var rootR = Rect(root);
            rootR.anchorMin = new Vector2(0.5f, 0.65f);
            rootR.anchorMax = new Vector2(0.5f, 0.65f);
            rootR.pivot = new Vector2(0.5f, 0.5f);
            rootR.sizeDelta = new Vector2(500f, 120f);
            var cg = root.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.blocksRaycasts = false;

            // Background
            var bg = CreatePanel("Bg", root.transform,
                new Color(0.05f, 0.15f, 0.3f, 0.9f));
            StretchFull(Rect(bg));

            // Accent bar (left edge)
            var bar = CreatePanel("AccentBar", root.transform,
                new Color(0.3f, 0.9f, 0.5f, 1f));
            var barR = Rect(bar);
            barR.anchorMin = Vector2.zero;
            barR.anchorMax = new Vector2(0f, 1f);
            barR.pivot = new Vector2(0f, 0.5f);
            barR.anchoredPosition = Vector2.zero;
            barR.sizeDelta = new Vector2(6f, 0f);

            // Title
            var title = CreateText("Title", root.transform,
                "\u2691 DEFECTION", 16, new Color(0.3f, 0.9f, 0.5f, 1f),
                TextAnchor.MiddleLeft);
            var titleR = Rect(title);
            titleR.anchorMin = new Vector2(0f, 0.65f);
            titleR.anchorMax = new Vector2(1f, 1f);
            titleR.offsetMin = new Vector2(20f, 0f);
            titleR.offsetMax = new Vector2(-12f, -8f);

            // NPC Name
            var nameTxt = CreateText("NPCName", root.transform,
                "NPC-04 has joined your side", 22,
                new Color(0.9f, 0.95f, 1f, 1f), TextAnchor.MiddleLeft);
            nameTxt.GetComponent<Text>().fontStyle = FontStyle.Bold;
            var nameR = Rect(nameTxt);
            nameR.anchorMin = new Vector2(0f, 0.25f);
            nameR.anchorMax = new Vector2(1f, 0.7f);
            nameR.offsetMin = new Vector2(20f, 0f);
            nameR.offsetMax = new Vector2(-12f, 0f);

            // Subtitle
            var sub = CreateText("Subtitle", root.transform,
                "They will fight alongside you", 14,
                new Color(0.6f, 0.7f, 0.8f, 0.9f), TextAnchor.MiddleLeft);
            var subR = Rect(sub);
            subR.anchorMin = new Vector2(0f, 0f);
            subR.anchorMax = new Vector2(1f, 0.3f);
            subR.offsetMin = new Vector2(20f, 6f);
            subR.offsetMax = new Vector2(-12f, 0f);

            root.SetActive(false);
        }

        // ====================================================================
        // Run Summary Screen
        // ====================================================================

        static void BuildRunSummaryVisuals(Transform canvasT)
        {
            // Full-screen root
            var root = CreateUI("RunSummary_Root", canvasT);
            StretchFull(Rect(root));
            var bgImg = root.AddComponent<Image>();
            bgImg.color = new Color(0.02f, 0.04f, 0.08f, 0.95f);
            bgImg.raycastTarget = true;
            var cg = root.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            // Content panel
            var scroll = CreateUI("Scroll", root.transform);
            var scrollR = Rect(scroll);
            scrollR.anchorMin = new Vector2(0.08f, 0.05f);
            scrollR.anchorMax = new Vector2(0.92f, 0.95f);
            scrollR.offsetMin = Vector2.zero;
            scrollR.offsetMax = Vector2.zero;

            Color hdrCol = new(0.3f, 0.85f, 1f, 1f);
            Color statCol = new(0.85f, 0.9f, 0.95f, 1f);
            Color xpCol = new(1f, 0.85f, 0.2f, 1f);
            Color accentCol = new(0.3f, 0.95f, 0.5f, 1f);
            Color dimCol = new(0.5f, 0.55f, 0.65f, 0.9f);

            float y = 0f;
            float sp = 8f;

            AddSummaryText(scroll.transform, ref y, "RUN COMPLETE", 32, hdrCol, FontStyle.Bold);
            y += sp;
            AddDivider(scroll.transform, ref y);
            y += sp * 2;

            AddSummaryText(scroll.transform, ref y, "STATS", 16, dimCol, FontStyle.Bold);
            y += 4f;
            AddSummaryText(scroll.transform, ref y, "Rooms Cleared:  0 / 0", 20, statCol);
            AddSummaryText(scroll.transform, ref y, "Kills:  0", 20, statCol);
            AddSummaryText(scroll.transform, ref y, "Accuracy:  0%", 20, statCol);
            AddSummaryText(scroll.transform, ref y, "Time:  0:00", 20, statCol);
            y += sp * 2;

            AddDivider(scroll.transform, ref y);
            y += sp * 2;

            AddSummaryText(scroll.transform, ref y, "REWARDS", 16, dimCol, FontStyle.Bold);
            y += 4f;
            AddSummaryText(scroll.transform, ref y, "Base XP:   +0", 20, xpCol);
            AddSummaryText(scroll.transform, ref y, "Bonus XP:  +0", 20, xpCol);
            AddSummaryText(scroll.transform, ref y, "", 16, accentCol, FontStyle.Italic);
            y += 4f;
            AddSummaryText(scroll.transform, ref y, "TOTAL:  0 XP", 26, xpCol, FontStyle.Bold);
            y += sp * 2;

            AddDivider(scroll.transform, ref y);
            y += sp * 2;

            AddSummaryText(scroll.transform, ref y, "DIRECTOR NOTES", 16, dimCol, FontStyle.Bold);
            y += 4f;
            var dirTxt = AddSummaryText(scroll.transform, ref y, "No adjustments.", 17, statCol);
            Rect(dirTxt).sizeDelta = new Vector2(0f, 60f);
            y += 40f;

            AddSummaryText(scroll.transform, ref y, "", 18, accentCol, FontStyle.Italic);
            y += sp * 2;

            // Continue button
            var btnObj = CreatePanel("ContinueBtn", scroll.transform,
                new Color(0.2f, 0.6f, 1f, 0.9f));
            var btnR = Rect(btnObj);
            btnR.anchorMin = new Vector2(0.2f, 1f);
            btnR.anchorMax = new Vector2(0.8f, 1f);
            btnR.pivot = new Vector2(0.5f, 1f);
            btnR.anchoredPosition = new Vector2(0f, -y);
            btnR.sizeDelta = new Vector2(0f, 60f);
            btnObj.AddComponent<Button>();

            var btnLabel = CreateText("Label", btnObj.transform,
                "NEXT RUN \u25B6", 24, Color.white, TextAnchor.MiddleCenter, true);
            btnLabel.GetComponent<Text>().fontStyle = FontStyle.Bold;

            root.SetActive(false);
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject CreatePanel(string name, Transform parent, Color col)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = col;
            return go;
        }

        static GameObject CreateText(string name, Transform parent, string text,
            int fontSize, Color color, TextAnchor anchor, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (stretch) StretchFull(Rect(go));
            return go;
        }

        static GameObject AddSummaryText(Transform parent, ref float y,
            string content, int fontSize, Color color,
            FontStyle style = FontStyle.Normal)
        {
            float h = fontSize + 12f;
            var go = CreateText("Text", parent, content, fontSize, color,
                TextAnchor.MiddleLeft);
            var t = go.GetComponent<Text>();
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            var r = Rect(go);
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -y);
            r.sizeDelta = new Vector2(0f, h);
            y += h;
            return go;
        }

        static void AddDivider(Transform parent, ref float y)
        {
            var go = CreatePanel("Divider", parent,
                new Color(0.3f, 0.4f, 0.5f, 0.5f));
            var r = Rect(go);
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -y);
            r.sizeDelta = new Vector2(0f, 2f);
            y += 2f;
        }

        static RectTransform Rect(GameObject go) => go.GetComponent<RectTransform>();

        static void StretchFull(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        static void ApplyCircleSprite(Image img)
        {
            int sz = 128;
            var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
            float c = sz / 2f;
            float rad = c - 1f;
            for (int py = 0; py < sz; py++)
                for (int px = 0; px < sz; px++)
                {
                    float d = Vector2.Distance(new Vector2(px, py), new Vector2(c, c));
                    float a = Mathf.Clamp01((rad - d) * 2f);
                    tex.SetPixel(px, py, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            img.sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, sz, sz),
                new Vector2(0.5f, 0.5f));
        }
    }
}
#endif
