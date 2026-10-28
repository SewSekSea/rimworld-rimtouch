using UnityEngine;
using Verse;
using System.Runtime.CompilerServices;
using RimTouch.TouchPlatform;

namespace RimTouch
{
    public sealed class RimTouchMod : Mod
    {
        internal const bool DefaultExtendedZoomEnabled = true;
        internal const float DefaultExtendedZoomInMin = 5f;
        internal const float DefaultExtendedZoomOutMax = 120f;

        public static RimTouchSettings Settings;
        private static readonly ConditionalWeakTable<CameraMapConfig, ZoomRangeLease> ZoomLeases = new ConditionalWeakTable<CameraMapConfig, ZoomRangeLease>();
        private Vector2 settingsScroll;
        private float settingsHeight = 1000f;
        public static bool ExtraUiScalesEnabled { get { return RimTouchBootstrap.UiScaleHookReady && Settings != null && Settings.enableTouchMode && Settings.allowExtraUiScales; } }

        public RimTouchMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimTouchSettings>();
        }

        public override string SettingsCategory()
        {
            return "RimTouch";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, Mathf.Max(inRect.height, settingsHeight));
            Widgets.BeginScrollView(inRect, ref settingsScroll, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("Enable touch mode", ref Settings.enableTouchMode, "Enables RimTouch direct touch gestures and UI tap repair.");
            listing.Label("Input: " + TouchSource.Status);
            listing.CheckboxLabeled("Two-finger globe twist", ref Settings.enableGlobeTwist, "Turn two fingers to roll the world view. A deliberate twist releases the world's north-up lock. Use North-up in the world view to straighten it again.");
            listing.CheckboxLabeled("Allow extra UI scales (advanced)", ref Settings.allowExtraUiScales, "Opt in to scales outside the game's normal resolution checks. Turn this off to restore the native scale choices.");
            if (!RimTouchBootstrap.UiScaleHookReady) listing.Label("Extra scales are unavailable on this game build; native scale limits apply.");
            if (listing.ButtonText("Reset UI scale to 1.0x"))
            {
                Settings.allowExtraUiScales = false;
                Prefs.UIScale = 1f;
            }
            listing.GapLine();

            listing.Label("Pan inertia strength: " + Settings.panInertiaStrength.ToString("0.00"));
            Settings.panInertiaStrength = listing.Slider(Settings.panInertiaStrength, 0f, 2f);

            listing.Label("Pan inertia damping: " + Settings.panInertiaDamping.ToString("0.00"));
            Settings.panInertiaDamping = listing.Slider(Settings.panInertiaDamping, 1f, 10f);

            listing.Label("Zoom inertia strength: " + Settings.zoomInertiaStrength.ToString("0.00"));
            Settings.zoomInertiaStrength = listing.Slider(Settings.zoomInertiaStrength, 0f, 2f);

            listing.Label("Zoom inertia damping: " + Settings.zoomInertiaDamping.ToString("0.00"));
            Settings.zoomInertiaDamping = listing.Slider(Settings.zoomInertiaDamping, 1f, 10f);

            listing.Gap();
            if (listing.ButtonText("Reset touch tuning"))
            {
                Settings.ResetTouchTuning();
            }

            listing.GapLine();
            listing.Label("Extended zoom range. Disabling restores prior bounds if this mod still owns them.");
            bool extendedZoomEnabledBefore = Settings.extendedZoomEnabled;
            listing.CheckboxLabeled("Enable extended zoom range", ref Settings.extendedZoomEnabled, "Lets you zoom in closer and out further than vanilla allows.");

            listing.Label("Zoom in (min size, lower = closer): " + Settings.extendedZoomInMin.ToString("0.0"));
            Settings.extendedZoomInMin = listing.Slider(Settings.extendedZoomInMin, 0.5f, 11f);

            listing.Label("Zoom out (max size, higher = further): " + Settings.extendedZoomOutMax.ToString("0"));
            Settings.extendedZoomOutMax = listing.Slider(Settings.extendedZoomOutMax, 60f, 300f);

            listing.Gap();
            if (listing.ButtonText("Apply zoom range now"))
            {
                ApplyExtendedZoomRange();
            }
            if (listing.ButtonText("Reset zoom range to default"))
            {
                Settings.ResetZoomRange();
                ApplyExtendedZoomRange();
            }

            if (Settings.extendedZoomEnabled != extendedZoomEnabledBefore || Settings.extendedZoomEnabled)
            {
                // Re-apply live so dragging the sliders gives immediate feedback in-game instead
                // of only taking effect after the next save/load.
                ApplyExtendedZoomRange();
            }

            settingsHeight = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();
            // Dialog_ModSettings.PreClose persists through Mod.WriteSettings.
            // Writing on every GUI pass causes needless I/O and file-sharing errors.
        }

        public static void ApplyExtendedZoomRange()
        {
            if (Settings == null)
            {
                return;
            }

            CameraDriver cameraDriver = Find.CameraDriver;
            if (cameraDriver == null || cameraDriver.config == null)
            {
                return;
            }

            float min = Mathf.Clamp(Settings.extendedZoomInMin, 0.5f, 11f);
            float max = Mathf.Max(min + 1f, Settings.extendedZoomOutMax);
            float nextMin, nextMax;
            ZoomRangeLease lease = ZoomLeases.GetOrCreateValue(cameraDriver.config);
            if (lease.Update(cameraDriver.config.sizeRange.min, cameraDriver.config.sizeRange.max, Settings.extendedZoomEnabled, min, max, out nextMin, out nextMax))
            {
                cameraDriver.config.sizeRange.min = nextMin;
                cameraDriver.config.sizeRange.max = nextMax;
            }
        }
    }

    public sealed class RimTouchSettings : ModSettings
    {
        public bool enableTouchMode = true;
        public bool enableGlobeTwist = true;
        public bool allowExtraUiScales;
        public float panInertiaStrength = 1.05f;
        public float panInertiaDamping = 4.8f;
        public float zoomInertiaStrength = 1.05f;
        public float zoomInertiaDamping = 4.8f;
        public bool extendedZoomEnabled = RimTouchMod.DefaultExtendedZoomEnabled;
        public float extendedZoomInMin = RimTouchMod.DefaultExtendedZoomInMin;
        public float extendedZoomOutMax = RimTouchMod.DefaultExtendedZoomOutMax;
        public int settingsVersion = 2;

        public void ResetTouchTuning()
        {
            panInertiaStrength = 1.05f;
            panInertiaDamping = 4.8f;
            zoomInertiaStrength = 1.05f;
            zoomInertiaDamping = 4.8f;
        }

        public void ResetZoomRange()
        {
            extendedZoomEnabled = RimTouchMod.DefaultExtendedZoomEnabled;
            extendedZoomInMin = RimTouchMod.DefaultExtendedZoomInMin;
            extendedZoomOutMax = RimTouchMod.DefaultExtendedZoomOutMax;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableTouchMode, "enableTouchMode", true);
            Scribe_Values.Look(ref enableGlobeTwist, "enableGlobeTwist", true);
            Scribe_Values.Look(ref allowExtraUiScales, "allowExtraUiScales", false);
            Scribe_Values.Look(ref panInertiaStrength, "panInertiaStrength", 1.05f);
            Scribe_Values.Look(ref panInertiaDamping, "panInertiaDamping", 4.8f);
            Scribe_Values.Look(ref zoomInertiaStrength, "zoomInertiaStrength", 1.05f);
            Scribe_Values.Look(ref zoomInertiaDamping, "zoomInertiaDamping", 4.8f);
            Scribe_Values.Look(ref extendedZoomEnabled, "extendedZoomEnabled", RimTouchMod.DefaultExtendedZoomEnabled);
            Scribe_Values.Look(ref extendedZoomInMin, "extendedZoomInMin", RimTouchMod.DefaultExtendedZoomInMin);
            Scribe_Values.Look(ref extendedZoomOutMax, "extendedZoomOutMax", RimTouchMod.DefaultExtendedZoomOutMax);
            Scribe_Values.Look(ref settingsVersion, "settingsVersion", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int loadedVersion = settingsVersion;
                if (loadedVersion < 1)
                {
                    if (Mathf.Abs(zoomInertiaStrength - 0.22f) < 0.001f)
                    {
                        zoomInertiaStrength = 1.05f;
                    }
                    if (Mathf.Abs(zoomInertiaDamping - 7.5f) < 0.001f)
                    {
                        zoomInertiaDamping = 4.8f;
                    }
                }
                if (loadedVersion < 2)
                {
                    extendedZoomEnabled = RimTouchMod.DefaultExtendedZoomEnabled;
                    if (Mathf.Abs(extendedZoomOutMax - 90f) < 0.001f)
                    {
                        extendedZoomOutMax = RimTouchMod.DefaultExtendedZoomOutMax;
                    }
                }
                settingsVersion = 2;
            }
        }
    }
}
