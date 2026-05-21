// ============================================================================
// ThresholdUIBuilder.cs — Editor tool to build the complete THRESHOLD UI
// Creates the full UI hierarchy AND attaches + wires all runtime scripts.
// Both movement (left) and aim (right) use analogue sticks with outer/inner.
// ============================================================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using System.Reflection;

namespace Threshold.UI.Editor
{
    public class ThresholdUIBuilder : EditorWindow
    {
        // Joystick settings
        private float joystickBaseSize = 200f;
        private float joystickKnobRatio = 0.45f;
        private Color moveOuterColor = new(1f, 1f, 1f, 0.15f);
        private Color moveInnerColor = new(1f, 1f, 1f, 0.5f);
        private Color aimOuterColor = new(1f, 1f, 1f, 0.15f);
        private Color aimInnerColor = new(0.95f, 0.25f, 0.25f, 0.6f);

        // HUD settings
        private Color healthFullColor = new(0.2f, 0.9f, 0.4f, 1f);
        private Color ammoColor = new(1f, 0.85f, 0.3f, 1f);
        private Vector2 refResolution = new(1080, 1920);

        [MenuItem("Threshold/Build UI Hierarchy")]
        public static void ShowWindow()
        {
            GetWindow<ThresholdUIBuilder>("Threshold UI Builder");
        }

        private void OnGUI()
        {
            GUILayout.Label("THRESHOLD UI Builder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds the complete gameplay UI hierarchy with all runtime scripts\n" +
                "attached and wired. Both sticks have outer (bg) + inner (knob) images.",
                MessageType.Info);

            EditorGUILayout.Space(8);
            GUILayout.Label("Stick Settings", EditorStyles.boldLabel);
            joystickBaseSize = EditorGUILayout.FloatField("Base Size", joystickBaseSize);
            joystickKnobRatio = EditorGUILayout.Slider("Knob Ratio", joystickKnobRatio, 0.2f, 0.8f);

            EditorGUILayout.Space(4);
            GUILayout.Label("Move Stick (Left)", EditorStyles.miniBoldLabel);
            moveOuterColor = EditorGUILayout.ColorField("Outer", moveOuterColor);
            moveInnerColor = EditorGUILayout.ColorField("Inner", moveInnerColor);

            GUILayout.Label("Aim Stick (Right)", EditorStyles.miniBoldLabel);
            aimOuterColor = EditorGUILayout.ColorField("Outer", aimOuterColor);
            aimInnerColor = EditorGUILayout.ColorField("Inner", aimInnerColor);

            EditorGUILayout.Space(4);
            GUILayout.Label("HUD", EditorStyles.boldLabel);
            refResolution = EditorGUILayout.Vector2Field("Ref Resolution", refResolution);
            healthFullColor = EditorGUILayout.ColorField("Health Color", healthFullColor);
            ammoColor = EditorGUILayout.ColorField("Ammo Color", ammoColor);

            EditorGUILayout.Space(12);
            if (GUILayout.Button("Build UI Hierarchy", GUILayout.Height(36)))
                BuildAll();
        }

        // ====================================================================
        // Master Build
        // ====================================================================

        private void BuildAll()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Threshold UI");

            var canvasGO = CreateCanvas();
            var ct = canvasGO.transform;
            EnsureEventSystem();

            // --- ThresholdUIManager (root manager) ---
            var managerGO = new GameObject("ThresholdUIManager");
            Undo.RegisterCreatedObjectUndo(managerGO, "Create UIManager");
            var manager = managerGO.AddComponent<ThresholdUIManager>();

            // --- Move Stick (left) ---
            var moveStickGO = new GameObject("MoveStick");
            Undo.RegisterCreatedObjectUndo(moveStickGO, "Create MoveStick");
            moveStickGO.transform.SetParent(managerGO.transform);
            var moveStick = moveStickGO.AddComponent<VirtualJoystick>();
            moveStick.baseSize = joystickBaseSize;
            moveStick.knobRatio = joystickKnobRatio;
            moveStick.baseColor = moveOuterColor;
            moveStick.knobColor = moveInnerColor;
            BuildAndWireMoveStick(ct, moveStick);

            // --- Aim Stick (right) ---
            var aimStickGO = new GameObject("AimStick");
            Undo.RegisterCreatedObjectUndo(aimStickGO, "Create AimStick");
            aimStickGO.transform.SetParent(managerGO.transform);
            var aimStick = aimStickGO.AddComponent<AimJoystick>();
            aimStick.baseSize = joystickBaseSize;
            aimStick.knobRatio = joystickKnobRatio;
            aimStick.baseColor = aimOuterColor;
            aimStick.knobColor = aimInnerColor;
            BuildAndWireAimStick(ct, aimStick);

            // --- GameplayHUD ---
            var hudGO = new GameObject("GameplayHUD");
            Undo.RegisterCreatedObjectUndo(hudGO, "Create HUD");
            hudGO.transform.SetParent(managerGO.transform);
            var hud = hudGO.AddComponent<GameplayHUD>();
            hud.healthFullColor = healthFullColor;
            BuildAndWireHUD(ct, hud);

            // --- DefectionPopup ---
            var popupGO = new GameObject("DefectionPopup");
            Undo.RegisterCreatedObjectUndo(popupGO, "Create Popup");
            popupGO.transform.SetParent(managerGO.transform);
            var popup = popupGO.AddComponent<DefectionPopup>();
            BuildAndWireDefectionPopup(ct, popup);

            // --- RunSummaryScreen ---
            var summaryGO = new GameObject("RunSummaryScreen");
            Undo.RegisterCreatedObjectUndo(summaryGO, "Create Summary");
            summaryGO.transform.SetParent(managerGO.transform);
            var summary = summaryGO.AddComponent<RunSummaryScreen>();
            BuildAndWireRunSummary(ct, summary);

            // --- PauseScreen ---
            var pauseGO = new GameObject("PauseScreen");
            Undo.RegisterCreatedObjectUndo(pauseGO, "Create PauseScreen");
            pauseGO.transform.SetParent(managerGO.transform);
            var pause = pauseGO.AddComponent<PauseScreen>();
            BuildAndWirePauseScreen(ct, pause);

            // --- Pause Button (on HUD canvas) ---
            var pauseBtn = BuildPauseButton(ct, manager);

            // --- Wire manager references ---
            manager.joystick = moveStick;
            manager.aimJoystick = aimStick;
            manager.hud = hud;
            manager.defectionPopup = popup;
            manager.summaryScreen = summary;
            manager.pauseScreen = pause;
            manager.referenceResolution = refResolution;

            EditorUtility.SetDirty(manager);
            Selection.activeGameObject = canvasGO;
            Debug.Log("[ThresholdUIBuilder] Complete UI hierarchy built with all scripts wired.");
        }

        // ====================================================================
        // Move Stick (Left)
        // ====================================================================

        private void BuildAndWireMoveStick(Transform canvas, VirtualJoystick stick)
        {
            // Touch area — left half
            var touchArea = CreateUIElement("MoveStick_TouchArea", canvas);
            var touchRect = touchArea.GetComponent<RectTransform>();
            touchRect.anchorMin = new Vector2(0f, 0f);
            touchRect.anchorMax = new Vector2(0.5f, 0.5f);
            touchRect.offsetMin = Vector2.zero;
            touchRect.offsetMax = Vector2.zero;
            var touchImg = touchArea.AddComponent<Image>();
            touchImg.color = new Color(0, 0, 0, 0.01f);
            touchImg.raycastTarget = true;
            var proxy = touchArea.AddComponent<JoystickTouchProxy>();
            proxy.joystick = stick;

            // Outer (base)
            var outer = CreateUIElement("MoveStick_Outer", canvas);
            var outerRect = outer.GetComponent<RectTransform>();
            outerRect.anchorMin = outerRect.anchorMax = Vector2.zero;
            outerRect.pivot = new Vector2(0.5f, 0.5f);
            outerRect.anchoredPosition = new Vector2(140f, 140f);
            outerRect.sizeDelta = new Vector2(joystickBaseSize, joystickBaseSize);
            var outerImg = outer.AddComponent<Image>();
            outerImg.color = moveOuterColor;
            outerImg.raycastTarget = false;
            AssignCircleSprite(outerImg);

            // Inner (knob)
            float knobSize = joystickBaseSize * joystickKnobRatio;
            var inner = CreateUIElement("MoveStick_Inner", outer.transform);
            var innerRect = inner.GetComponent<RectTransform>();
            innerRect.anchoredPosition = Vector2.zero;
            innerRect.sizeDelta = new Vector2(knobSize, knobSize);
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = moveInnerColor;
            innerImg.raycastTarget = false;
            AssignCircleSprite(innerImg);

            // Wire serialized fields via reflection
            SetPrivateField(stick, "_baseRect", outerRect);
            SetPrivateField(stick, "_knobRect", innerRect);
            SetPrivateField(stick, "_baseImage", outerImg);
            SetPrivateField(stick, "_knobImage", innerImg);
            EditorUtility.SetDirty(stick);
        }

        // ====================================================================
        // Aim Stick (Right)
        // ====================================================================

        private void BuildAndWireAimStick(Transform canvas, AimJoystick stick)
        {
            // Touch area — right half
            var touchArea = CreateUIElement("AimStick_TouchArea", canvas);
            var touchRect = touchArea.GetComponent<RectTransform>();
            touchRect.anchorMin = new Vector2(0.5f, 0f);
            touchRect.anchorMax = new Vector2(1f, 0.5f);
            touchRect.offsetMin = Vector2.zero;
            touchRect.offsetMax = Vector2.zero;
            var touchImg = touchArea.AddComponent<Image>();
            touchImg.color = new Color(0, 0, 0, 0.01f);
            touchImg.raycastTarget = true;
            var proxy = touchArea.AddComponent<AimTouchProxy>();
            proxy.aimJoystick = stick;

            // Outer (base)
            var outer = CreateUIElement("AimStick_Outer", canvas);
            var outerRect = outer.GetComponent<RectTransform>();
            outerRect.anchorMin = outerRect.anchorMax = new Vector2(1f, 0f);
            outerRect.pivot = new Vector2(0.5f, 0.5f);
            outerRect.anchoredPosition = new Vector2(-140f, 140f);
            outerRect.sizeDelta = new Vector2(joystickBaseSize, joystickBaseSize);
            var outerImg = outer.AddComponent<Image>();
            outerImg.color = aimOuterColor;
            outerImg.raycastTarget = false;
            AssignCircleSprite(outerImg);

            // Inner (knob)
            float knobSize = joystickBaseSize * joystickKnobRatio;
            var inner = CreateUIElement("AimStick_Inner", outer.transform);
            var innerRect = inner.GetComponent<RectTransform>();
            innerRect.anchoredPosition = Vector2.zero;
            innerRect.sizeDelta = new Vector2(knobSize, knobSize);
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = aimInnerColor;
            innerImg.raycastTarget = false;
            AssignCircleSprite(innerImg);

            // Wire serialized fields
            SetPrivateField(stick, "_baseRect", outerRect);
            SetPrivateField(stick, "_knobRect", innerRect);
            SetPrivateField(stick, "_baseImage", outerImg);
            SetPrivateField(stick, "_knobImage", innerImg);
            EditorUtility.SetDirty(stick);
        }

        // ====================================================================
        // HUD
        // ====================================================================

        private void BuildAndWireHUD(Transform canvas, GameplayHUD hud)
        {
            // -- Health Bar --
            var hContainer = CreatePanel("HealthBar_Container", canvas);
            var hcr = hContainer.GetComponent<RectTransform>();
            hcr.anchorMin = hcr.anchorMax = new Vector2(0f, 1f);
            hcr.pivot = new Vector2(0f, 1f);
            hcr.anchoredPosition = new Vector2(24f, -24f);
            hcr.sizeDelta = new Vector2(300f, 50f);
            hContainer.GetComponent<Image>().color = Color.clear;

            var icon = CreateTextElement("Health_Icon", hContainer.transform, "+", 28,
                new Color(0.9f, 0.3f, 0.3f, 1f), TextAnchor.MiddleCenter);
            var ir = icon.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0f, 0f); ir.anchorMax = new Vector2(0f, 1f);
            ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = Vector2.zero; ir.sizeDelta = new Vector2(30f, 0f);

            var hBg = CreatePanel("HealthBar_Bg", hContainer.transform);
            var hbr = hBg.GetComponent<RectTransform>();
            hbr.anchorMin = new Vector2(0f, 0.15f); hbr.anchorMax = new Vector2(1f, 0.85f);
            hbr.offsetMin = new Vector2(36f, 0f); hbr.offsetMax = new Vector2(-8f, 0f);
            hBg.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            var hFill = CreatePanel("HealthBar_Fill", hBg.transform);
            var hfr = hFill.GetComponent<RectTransform>();
            hfr.anchorMin = Vector2.zero; hfr.anchorMax = Vector2.one;
            hfr.offsetMin = new Vector2(3f, 3f); hfr.offsetMax = new Vector2(-3f, -3f);
            var hFillImg = hFill.GetComponent<Image>();
            hFillImg.color = healthFullColor;

            var hTxt = CreateTextElement("Health_Text", hBg.transform, "100%", 18,
                Color.white, TextAnchor.MiddleCenter);
            var htr = hTxt.GetComponent<RectTransform>();
            htr.anchorMin = Vector2.zero; htr.anchorMax = Vector2.one;
            htr.offsetMin = htr.offsetMax = Vector2.zero;

            // -- Ammo --
            var aContainer = CreatePanel("Ammo_Container", canvas);
            var acr = aContainer.GetComponent<RectTransform>();
            acr.anchorMin = acr.anchorMax = new Vector2(1f, 1f);
            acr.pivot = new Vector2(1f, 1f);
            acr.anchoredPosition = new Vector2(-24f, -24f);
            acr.sizeDelta = new Vector2(180f, 50f);
            aContainer.GetComponent<Image>().color = new Color(0, 0, 0, 0.4f);

            var aTxt = CreateTextElement("Ammo_Text", aContainer.transform, "30 / 30", 26,
                ammoColor, TextAnchor.MiddleCenter);
            var atr = aTxt.GetComponent<RectTransform>();
            atr.anchorMin = Vector2.zero; atr.anchorMax = Vector2.one;
            atr.offsetMin = new Vector2(8f, 0f); atr.offsetMax = new Vector2(-8f, 0f);

            // -- Kill Counter --
            var kTxt = CreateTextElement("Kill_Text", canvas, "KILLS: 0", 20,
                new Color(1f, 0.4f, 0.4f, 0.9f), TextAnchor.MiddleLeft);
            var ktr = kTxt.GetComponent<RectTransform>();
            ktr.anchorMin = ktr.anchorMax = new Vector2(0f, 1f);
            ktr.pivot = new Vector2(0f, 1f);
            ktr.anchoredPosition = new Vector2(24f, -82f);
            ktr.sizeDelta = new Vector2(200f, 30f);

            // -- Room Progress --
            var rContainer = CreatePanel("Room_Container", canvas);
            var rcr = rContainer.GetComponent<RectTransform>();
            rcr.anchorMin = rcr.anchorMax = new Vector2(0.5f, 1f);
            rcr.pivot = new Vector2(0.5f, 1f);
            rcr.anchoredPosition = new Vector2(0f, -24f);
            rcr.sizeDelta = new Vector2(260f, 44f);
            rContainer.GetComponent<Image>().color = new Color(0, 0, 0, 0.4f);

            var rTxt = CreateTextElement("Room_Text", rContainer.transform, "ROOM 1 / 7", 18,
                Color.white, TextAnchor.MiddleCenter);
            var rtr = rTxt.GetComponent<RectTransform>();
            rtr.anchorMin = new Vector2(0f, 0.5f); rtr.anchorMax = new Vector2(1f, 1f);
            rtr.offsetMin = new Vector2(8f, 0f); rtr.offsetMax = new Vector2(-8f, 0f);

            var rBg = CreatePanel("RoomProgress_Bg", rContainer.transform);
            var rbr = rBg.GetComponent<RectTransform>();
            rbr.anchorMin = new Vector2(0.05f, 0.1f); rbr.anchorMax = new Vector2(0.95f, 0.4f);
            rbr.offsetMin = rbr.offsetMax = Vector2.zero;
            rBg.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 0.8f);

            var rFill = CreatePanel("RoomProgress_Fill", rBg.transform);
            var rfr = rFill.GetComponent<RectTransform>();
            rfr.anchorMin = Vector2.zero; rfr.anchorMax = new Vector2(0.14f, 1f);
            rfr.offsetMin = rfr.offsetMax = Vector2.zero;
            var rFillImg = rFill.GetComponent<Image>();
            rFillImg.color = new Color(0.3f, 0.8f, 1f, 0.9f);

            // Wire to HUD
            SetPrivateField(hud, "_healthBarBg", hbr);
            SetPrivateField(hud, "_healthBarFill", hfr);
            SetPrivateField(hud, "_healthFillImage", hFillImg);
            SetPrivateField(hud, "_healthText", hTxt.GetComponent<Text>());
            SetPrivateField(hud, "_ammoText", aTxt.GetComponent<Text>());
            SetPrivateField(hud, "_killText", kTxt.GetComponent<Text>());
            SetPrivateField(hud, "_roomText", rTxt.GetComponent<Text>());
            SetPrivateField(hud, "_roomProgressBg", rbr);
            SetPrivateField(hud, "_roomProgressFill", rfr);
            SetPrivateField(hud, "_roomFillImage", rFillImg);
            EditorUtility.SetDirty(hud);
        }

        // ====================================================================
        // Defection Popup
        // ====================================================================

        private void BuildAndWireDefectionPopup(Transform canvas, DefectionPopup popup)
        {
            var root = CreateUIElement("DefectionPopup_Root", canvas);
            var rr = root.GetComponent<RectTransform>();
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.65f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(500f, 120f);
            var cg = root.AddComponent<CanvasGroup>();
            cg.alpha = 0f; cg.blocksRaycasts = false;

            var bg = CreatePanel("Bg", root.transform);
            StretchFull(bg.GetComponent<RectTransform>());
            bg.GetComponent<Image>().color = new Color(0.05f, 0.15f, 0.3f, 0.9f);

            var bar = CreatePanel("AccentBar", root.transform);
            var barR = bar.GetComponent<RectTransform>();
            barR.anchorMin = new Vector2(0f, 0f); barR.anchorMax = new Vector2(0f, 1f);
            barR.pivot = new Vector2(0f, 0.5f);
            barR.anchoredPosition = Vector2.zero; barR.sizeDelta = new Vector2(6f, 0f);
            var accentCol = new Color(0.3f, 0.9f, 0.5f, 1f);
            bar.GetComponent<Image>().color = accentCol;

            var title = CreateTextElement("Title", root.transform, "⚑ DEFECTION", 16,
                accentCol, TextAnchor.MiddleLeft);
            var tr = title.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0f, 0.65f); tr.anchorMax = new Vector2(1f, 1f);
            tr.offsetMin = new Vector2(20f, 0f); tr.offsetMax = new Vector2(-12f, -8f);

            var nameTxt = CreateTextElement("NPCName", root.transform,
                "NPC-04 has joined your side", 22, new Color(0.9f, 0.95f, 1f, 1f), TextAnchor.MiddleLeft);
            var nr = nameTxt.GetComponent<RectTransform>();
            nr.anchorMin = new Vector2(0f, 0.25f); nr.anchorMax = new Vector2(1f, 0.7f);
            nr.offsetMin = new Vector2(20f, 0f); nr.offsetMax = new Vector2(-12f, 0f);
            nameTxt.GetComponent<Text>().fontStyle = FontStyle.Bold;

            var sub = CreateTextElement("Subtitle", root.transform,
                "They will fight alongside you", 14,
                new Color(0.6f, 0.7f, 0.8f, 0.9f), TextAnchor.MiddleLeft);
            var sr = sub.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0.3f);
            sr.offsetMin = new Vector2(20f, 6f); sr.offsetMax = new Vector2(-12f, 0f);

            root.SetActive(false);

            SetPrivateField(popup, "_popupRoot", root);
            SetPrivateField(popup, "_popupRect", rr);
            SetPrivateField(popup, "_canvasGroup", cg);
            SetPrivateField(popup, "_titleText", title.GetComponent<Text>());
            SetPrivateField(popup, "_nameText", nameTxt.GetComponent<Text>());
            SetPrivateField(popup, "_subtitleText", sub.GetComponent<Text>());
            SetPrivateField(popup, "_accentBar", bar.GetComponent<Image>());
            EditorUtility.SetDirty(popup);
        }

        // ====================================================================
        // Run Summary (simplified — hidden root with continue button)
        // ====================================================================

        private void BuildAndWireRunSummary(Transform canvas, RunSummaryScreen summary)
        {
            var root = CreatePanel("RunSummary_Root", canvas);
            StretchFull(root.GetComponent<RectTransform>());
            root.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.08f, 0.95f);
            var cg = root.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            // The RunSummaryScreen script builds its own rich content layout.
            // We wire just the root so it knows to skip re-creating it.
            // The script's BuildUI() will populate content inside _root.
            root.SetActive(false);

            SetPrivateField(summary, "_root", root);
            SetPrivateField(summary, "_canvasGroup", cg);
            EditorUtility.SetDirty(summary);
        }

        // ====================================================================
        // Pause Screen
        // ====================================================================

        private void BuildAndWirePauseScreen(Transform canvas, PauseScreen pause)
        {
            // ── Root overlay (full-screen dark) ──
            var root = CreatePanel("PauseScreen_Root", canvas);
            StretchFull(root.GetComponent<RectTransform>());
            root.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.06f, 0.85f);
            var cg = root.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            // ── Title ──
            var title = CreateTextElement("Pause_Title", root.transform, "PAUSED", 48,
                new Color(0.95f, 0.95f, 1f, 1f), TextAnchor.MiddleCenter);
            var titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.1f, 0.62f);
            titleRect.anchorMax = new Vector2(0.9f, 0.78f);
            titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
            title.GetComponent<Text>().fontStyle = FontStyle.Bold;

            // ── Subtitle ──
            var sub = CreateTextElement("Pause_Sub", root.transform,
                "\u2014 THRESHOLD \u2014", 18,
                new Color(0.5f, 0.6f, 0.8f, 0.7f), TextAnchor.MiddleCenter);
            var subRect = sub.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.15f, 0.57f);
            subRect.anchorMax = new Vector2(0.85f, 0.63f);
            subRect.offsetMin = subRect.offsetMax = Vector2.zero;

            // ── Buttons ──
            float btnW = 320f, btnH = 70f;

            var resumeBtn = CreateMenuButton("Btn_Resume", root.transform,
                "\u25b6  RESUME", new Color(0.2f, 0.75f, 0.4f, 1f), btnW, btnH,
                0.5f, 0.50f);

            var quitBtn = CreateMenuButton("Btn_Quit", root.transform,
                "\u2715  QUIT", new Color(0.85f, 0.2f, 0.2f, 1f), btnW, btnH,
                0.5f, 0.42f);

            root.SetActive(false);

            // Wire to PauseScreen
            SetPrivateField(pause, "_root", root);
            SetPrivateField(pause, "_canvasGroup", cg);
            SetPrivateField(pause, "_resumeButton", resumeBtn);
            SetPrivateField(pause, "_quitButton", quitBtn);
            EditorUtility.SetDirty(pause);
        }

        /// <summary>Creates a styled menu button and returns its Button component.</summary>
        private Button CreateMenuButton(string name, Transform parent, string label,
            Color bgColor, float width, float height, float anchorX, float anchorY)
        {
            var btnObj = CreatePanel(name, parent);
            var btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = btnRect.anchorMax = new Vector2(anchorX, anchorY);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.sizeDelta = new Vector2(width, height);
            btnRect.anchoredPosition = Vector2.zero;

            var btnImage = btnObj.GetComponent<Image>();
            btnImage.color = bgColor;

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnImage;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            var labelObj = CreateTextElement(name + "_Label", btnObj.transform, label,
                24, Color.white, TextAnchor.MiddleCenter);
            StretchFull(labelObj.GetComponent<RectTransform>());
            labelObj.GetComponent<Text>().fontStyle = FontStyle.Bold;

            return btn;
        }

        // ====================================================================
        // Pause Button (HUD element)
        // ====================================================================

        private GameObject BuildPauseButton(Transform canvas, ThresholdUIManager manager)
        {
            var btnObj = CreatePanel("PauseButton", canvas);
            var btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = btnRect.anchorMax = new Vector2(1f, 1f);
            btnRect.pivot = new Vector2(1f, 1f);
            btnRect.anchoredPosition = new Vector2(-24f, -82f);
            btnRect.sizeDelta = new Vector2(60f, 60f);

            var btnImage = btnObj.GetComponent<Image>();
            btnImage.color = new Color(0.12f, 0.12f, 0.18f, 0.65f);
            btnImage.raycastTarget = true;

            // Pause icon
            var iconObj = CreateTextElement("PauseIcon", btnObj.transform, "\u275a\u275a", 28,
                new Color(0.9f, 0.9f, 0.95f, 0.9f), TextAnchor.MiddleCenter);
            StretchFull(iconObj.GetComponent<RectTransform>());

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnImage;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.8f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = colors;

            // Note: Button onClick is wired at runtime by ThresholdUIManager.BuildPauseButton
            // or by the user in the Inspector. We can't wire UnityEvent to instance methods
            // from editor scripts easily, so runtime self-wiring handles this.

            return btnObj;
        }

        // ====================================================================
        // Utility
        // ====================================================================

        private GameObject CreateCanvas()
        {
            var go = new GameObject("THRESHOLD_Canvas");
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 100;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = refResolution;
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private GameObject CreateUIElement(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, name);
            return go;
        }

        private GameObject CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, name);
            return go;
        }

        private GameObject CreateTextElement(string name, Transform parent, string text,
            int fontSize, Color color, TextAnchor anchor)
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
            Undo.RegisterCreatedObjectUndo(go, name);
            return go;
        }

        private void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void AssignCircleSprite(Image img)
        {
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01((radius - dist) * 2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            tex.Apply();
            img.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Sets a private [SerializeField] via SerializedObject for proper Undo + persistence.</summary>
        private void SetPrivateField(Component target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning($"[UIBuilder] Could not find field '{fieldName}' on {target.GetType().Name}");
            }
        }
    }
}
#endif
