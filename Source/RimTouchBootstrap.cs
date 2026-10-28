using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimTouch
{
    [StaticConstructorOnStartup]
    public static class RimTouchBootstrap
    {
        internal static bool UiScaleHookReady;
        private static readonly float[] ExtraUiScales = { 0.75f, 0.85f, 1f, 1.1f, 1.25f, 1.35f, 1.5f, 1.75f, 2f, 2.25f, 2.5f, 3f };
        static RimTouchBootstrap()
        {
            Harmony harmony = new Harmony("tatsuki.rimtouch");
            harmony.PatchAll();
            int targets = 0;
            foreach (MethodBase target in harmony.GetPatchedMethods()) targets++;
            Log.Message("[RimTouch] Loaded touch input support; " + targets + " native hook targets; extra UI-scale hook ready=" + UiScaleHookReady + ".");
        }

        public static float[] GetUiScales(float[] nativeChoices)
        {
            // Avoid writing the game's static readonly field. Each use chooses
            // the native or opted-in array, leaving other mods' data intact.
            return RimTouchMod.ExtraUiScalesEnabled ? ExtraUiScales : nativeChoices;
        }

        public static void PostprocessButtonTap(Rect rect, ref bool result)
        {
            if (TouchInputDriver.ShouldSuppressUiClickInRect(rect))
            {
                result = false;
                Event current = Event.current;
                if (current != null && current.isMouse)
                {
                    current.Use();
                }
                return;
            }

            if (!result && TouchTapRepair.TryConsumeTap(rect))
            {
                result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Options), "DoUIOptions")]
    public static class DialogOptionsUiScalesPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = new List<CodeInstruction>(instructions);
            FieldInfo field = AccessTools.Field(typeof(Dialog_Options), "UIScales");
            MethodInfo choose = AccessTools.Method(typeof(RimTouchBootstrap), "GetUiScales");
            int reads = original.FindAll(i => i.opcode == OpCodes.Ldsfld && Equals(i.operand, field)).Count;
            if (reads != 3 || choose == null)
            {
                RimTouchBootstrap.UiScaleHookReady = false;
                Log.Warning("[RimTouch] Extra UI scales unavailable: expected three native choice-list reads. Native scale safety remains enabled.");
                return original;
            }
            var result = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in original)
            {
                result.Add(instruction);
                if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, field)) result.Add(new CodeInstruction(OpCodes.Call, choose));
            }
            RimTouchBootstrap.UiScaleHookReady = true;
            return result;
        }
    }

    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class GameInitNewGamePatch
    {
        public static void Postfix()
        {
            RimTouchMod.ApplyExtendedZoomRange();
        }
    }

    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class GameLoadGamePatch
    {
        public static void Postfix()
        {
            RimTouchMod.ApplyExtendedZoomRange();
        }
    }

    [HarmonyPatch(typeof(Root_Play), "Update")]
    public static class RootPlayUpdatePatch
    {
        public static void Prefix()
        {
            RimTouch.TouchPlatform.TouchInputProof.MarkUpdate(true);
            TouchInputDriver.Update();
        }
    }

    [HarmonyPatch(typeof(Root_Entry), "Update")]
    public static class RootEntryUpdatePatch
    {
        public static void Postfix()
        {
            RimTouch.TouchPlatform.TouchInputProof.MarkUpdate(false);
            TouchInputDriver.Update();
        }
    }

    [HarmonyPatch(typeof(Root), "OnGUI")]
    public static class RootOnGUIPatch
    {
        public static void Postfix()
        {
            RimTouch.TouchPlatform.TouchInputProof.OnGuiHeartbeat();
            TouchInputDriver.OnGUI();
        }
    }

    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    public static class SelectorHandleMapClicksPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressVanillaMapInput;
        }
    }

    [HarmonyPatch(typeof(DesignatorManager), "ProcessInputEvents")]
    public static class DesignatorManagerProcessInputEventsPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressVanillaMapToolInput;
        }
    }

    [HarmonyPatch(typeof(Targeter), "ProcessInputEvents")]
    public static class TargeterProcessInputEventsPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressVanillaMapToolInput;
        }
    }

    [HarmonyPatch(typeof(DragBox), "DragBoxOnGUI")]
    public static class DragBoxOnGUIPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressVanillaDragBoxDrawing;
        }
    }

    [HarmonyPatch(typeof(WorldSelector), "HandleWorldClicks")]
    public static class WorldSelectorHandleWorldClicksPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressVanillaWorldInput;
        }
    }

    [HarmonyPatch(typeof(WorldDragBox), "DragBoxOnGUI")]
    public static class WorldDragBoxOnGUIPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressVanillaWorldDragBoxDrawing;
        }
    }

    [HarmonyPatch(typeof(WorldDrawLayer_MouseTile), "get_Tile")]
    public static class WorldDrawLayerMouseTilePatch
    {
        public static bool Prefix(ref PlanetTile __result)
        {
            if (!TouchInputDriver.ShouldSuppressWorldHover)
            {
                return true;
            }

            __result = PlanetTile.Invalid;
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraDriver), "CalculateCurInputDollyVect")]
    public static class CameraDriverCalculateCurInputDollyVectPatch
    {
        public static void Postfix(CameraDriver __instance, ref Vector2 __result)
        {
            if (TouchInputDriver.ShouldSuppressEdgeScroll)
            {
                __result = Vector2.zero;
                TouchInputDriver.SuppressCameraEdgeScrollState(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(WorldCameraDriver), "CalculateCurInputDollyVect")]
    public static class WorldCameraDriverCalculateCurInputDollyVectPatch
    {
        public static void Postfix(WorldCameraDriver __instance, ref Vector2 __result)
        {
            __result = TouchInputDriver.ModifyWorldCameraInput(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(ResolutionUtility), "UIScaleSafeWithResolution")]
    public static class ResolutionUtilityUIScaleSafeWithResolutionPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!RimTouchMod.ExtraUiScalesEnabled) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Widgets), "BeginScrollView")]
    public static class WidgetsBeginScrollViewTouchPatch
    {
        public static void Prefix([HarmonyArgument(0)] Rect outRect, [HarmonyArgument(1)] ref Vector2 scrollPosition, [HarmonyArgument(2)] Rect viewRect)
        {
            TouchInputDriver.ProcessScrollViewTouch(outRect, viewRect, ref scrollPosition);
        }
    }

    [HarmonyPatch(typeof(WindowStack), "CloseWindowsBecauseClicked")]
    public static class WindowStackCloseWindowsBecauseClickedPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!TouchTapRepair.ShouldSuppressWindowClose && !TouchInputDriver.ShouldSuppressWindowCloseFromGesture)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(WindowStack), "NotifyOutsideClicks")]
    public static class WindowStackNotifyOutsideClicksPatch
    {
        public static bool Prefix()
        {
            return !TouchTapRepair.ShouldSuppressWindowClose && !TouchInputDriver.ShouldSuppressWindowCloseFromGesture;
        }
    }

    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class WindowStackAddPatch
    {
        public static void Prefix(Window window)
        {
            if (!TouchInputDriver.RecentTouchInput)
            {
                return;
            }

            FloatMenu floatMenu = window as FloatMenu;
            if (floatMenu != null)
            {
                floatMenu.vanishIfMouseDistant = false;
            }
        }
    }

    [HarmonyPatch(typeof(MainTabsRoot), "ToggleTab")]
    public static class MainTabsRootToggleTabPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressMainTabsFromGesture;
        }
    }

    [HarmonyPatch(typeof(MainTabsRoot), "SetCurrentTab")]
    public static class MainTabsRootSetCurrentTabPatch
    {
        public static bool Prefix()
        {
            return !TouchInputDriver.ShouldSuppressMainTabsFromGesture;
        }
    }

    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "HandleClicks")]
    public static class ColonistBarColonistDrawerHandleClicksPatch
    {
        public static void Postfix(Rect rect, Pawn colonist)
        {
            if (colonist == null || !TouchTapRepair.TryConsumeDoubleTap(rect))
            {
                return;
            }

            CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(colonist), CameraJumper.MovementMode.Pan);
        }
    }

    [HarmonyPatch(typeof(Widgets), "ButtonInvisible", new[] { typeof(Rect), typeof(bool) })]
    public static class WidgetsButtonInvisiblePatch
    {
        public static void Postfix(Rect butRect, ref bool __result)
        {
            RimTouchBootstrap.PostprocessButtonTap(butRect, ref __result);
        }
    }

    [HarmonyPatch(typeof(Widgets), "ButtonText", new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(System.Nullable<TextAnchor>) })]
    public static class WidgetsButtonTextPatch
    {
        public static void Postfix(Rect rect, ref bool __result)
        {
            RimTouchBootstrap.PostprocessButtonTap(rect, ref __result);
        }
    }

    [HarmonyPatch(typeof(Widgets), "ButtonText", new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(System.Nullable<TextAnchor>) })]
    public static class WidgetsButtonTextColorPatch
    {
        public static void Postfix(Rect rect, ref bool __result)
        {
            RimTouchBootstrap.PostprocessButtonTap(rect, ref __result);
        }
    }

    [HarmonyPatch(typeof(Widgets), "ButtonImage", new[] { typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string) })]
    public static class WidgetsButtonImagePatch
    {
        public static void Postfix(Rect butRect, ref bool __result)
        {
            RimTouchBootstrap.PostprocessButtonTap(butRect, ref __result);
        }
    }

    [HarmonyPatch(typeof(Widgets), "ButtonImage", new[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(bool), typeof(string) })]
    public static class WidgetsButtonImageColorPatch
    {
        public static void Postfix(Rect butRect, ref bool __result)
        {
            RimTouchBootstrap.PostprocessButtonTap(butRect, ref __result);
        }
    }

    [HarmonyPatch(typeof(Widgets), "ButtonImage", new[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color), typeof(bool), typeof(string) })]
    public static class WidgetsButtonImageTwoColorPatch
    {
        public static void Postfix(Rect butRect, ref bool __result)
        {
            RimTouchBootstrap.PostprocessButtonTap(butRect, ref __result);
        }
    }
}
