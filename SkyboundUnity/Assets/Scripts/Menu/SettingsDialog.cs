using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>Settings, shared by the start menu and the pause menu.</summary>
    public class SettingsDialog
    {
        static readonly string[] QUALITY_COPY =
        {
            "No shadows or effects, fewer trees and clouds, lower resolution. For laptops and integrated graphics.",
            "Shadows, full scenery near the field, standard resolution. A good default.",
            "Sharp shadows, the densest scenery, full resolution, plus bloom and vignette.",
        };

        readonly Scope scope = new Scope();
        readonly VisualElement overlay;

        /// <summary>The dialog, for tests.</summary>
        public VisualElement Root => overlay;

        public SettingsDialog(VisualElement parent)
        {
            var s = SettingsStore.Get();
            void Close() => UiStore.SetShowSettings(false);

            var qualityText = Ui.Txt("", "hint", "mt-2");
            scope.Text(qualityText, () => QUALITY_COPY[(int)SettingsStore.Get().graphics]);

            var dialog = Ui.Div("glass-strong", "rounded-2xl", "dialog");
            dialog.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            var body = Ui.Scroller("dialog__scroll");
            body.Add(Section("Graphics",
                Ui.Div("py").With(
                    new SegmentedEl<GraphicsQuality>(scope, () => SettingsStore.Get().graphics, SettingsStore.SetGraphics,
                        (GraphicsQuality.Low, "Low"), (GraphicsQuality.Medium, "Medium"), (GraphicsQuality.High, "High")),
                    qualityText),
                Row("Bloom and vignette", "Soft glow on lights and the sun, darker corners. High quality only.",
                    new SwitchEl(scope, () => SettingsStore.Get().postFx, SettingsStore.SetPostFx, () => SettingsStore.Get().graphics != GraphicsQuality.High),
                    () => SettingsStore.Get().graphics != GraphicsQuality.High),
                Row("Show frame rate", "A small FPS counter in the corner of the flight screen.",
                    new SwitchEl(scope, () => SettingsStore.Get().showFps, SettingsStore.SetShowFps))));
            body.Add(Section("Flying",
                Row("Easy flight", "Tilt sets bank and climb rate, speed is held, one key takes off, and the aircraft levels out when you let go.",
                    new SwitchEl(scope, () => SettingsStore.Get().easyFlight, SettingsStore.SetEasyFlight)),
                Row("Bank assist", "Roll input sets a bank angle instead of a roll rate. Always on in Easy flight.",
                    new SwitchEl(scope, () => SettingsStore.Get().easyFlight || SettingsStore.Get().bankAssist, SettingsStore.SetBankAssist, () => SettingsStore.Get().easyFlight),
                    () => SettingsStore.Get().easyFlight),
                Row("Auto-rudder", "Coordinates turns automatically. Always on in Easy flight.",
                    new SwitchEl(scope, () => SettingsStore.Get().easyFlight || SettingsStore.Get().autoRudder, SettingsStore.SetAutoRudder, () => SettingsStore.Get().easyFlight),
                    () => SettingsStore.Get().easyFlight)));
            body.Add(Section("Screen",
                Row("On-screen instruments", "The six-pack panel in the chase view (I).",
                    new SwitchEl(scope, () => UiStore.showInstruments, _ => UiStore.ToggleInstruments())),
                Row("Key card", "The controls reference in the corner (H).",
                    new SwitchEl(scope, () => UiStore.showHelp, _ => UiStore.ToggleHelp()))));
            body.Add(Section("Controller",
                Row("Motion controller", "Connection, calibration, sensitivity, dead zone and axis inversion.",
                    Ui.Btn("Open setup", () => UiStore.SetScreen(AppScreen.Setup), "outline", "sm"))));
            dialog.With(
                Ui.Div("dialog__header").With(Ui.Txt("SETTINGS", "display", "dialog__title"), Ui.Btn("Done", Close, "ghost", "sm")),
                body);

            overlay = Ui.Div("fill", "scrim", "centre").With(dialog).Appear("fade");
            overlay.RegisterCallback<ClickEvent>(_ => Close());
            parent.Add(overlay);
        }

        VisualElement Row(string title, string hint, VisualElement control, System.Func<bool> disabled = null)
        {
            var row = Ui.Div("settings-row").With(
                Ui.Div("settings-row__text").With(Ui.Txt(title, "settings-row__title"), Ui.Txt(hint, "hint", "mt-1")),
                control);
            if (disabled != null) scope.Toggle(row, "settings-row--disabled", disabled);
            return row;
        }

        static VisualElement Section(string title, params VisualElement[] rows)
        {
            var section = Ui.Div("settings-section").With(Ui.SectionLabel(title));
            if (title == "Graphics") section.AddToClassList("settings-section--first");
            rows[0].AddToClassList("settings-row--first");
            foreach (var row in rows) section.Add(row);
            return section;
        }

        public void Frame()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) UiStore.SetShowSettings(false);
            scope.Frame();
        }

        public void Close() => overlay.RemoveFromHierarchy();
    }
}
