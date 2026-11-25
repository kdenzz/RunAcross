using System;
using RunAcross.Audio;
using RunAcross.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RunAcross.UI
{
    /// <summary>Binds GameUI.uxml: main menu, settings, controls, pause, HUD and the finish screen.</summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameUI : MonoBehaviour
    {
        public GameFlow flow;
        public float toastSeconds = 2.4f;

        VisualElement _hud, _menu, _pause, _complete, _settings, _controls;
        VisualElement _subReturn; // which screen Settings/Controls go back to
        Label _objective, _objectiveCount, _timer, _toast, _best, _completeTime, _completeBest, _qualityNote;
        Button _qLow, _qMed, _qHigh;
        float _toastUntil;
        bool _bound;

        void OnEnable() => Bind();

        void Bind()
        {
            if (_bound) return;
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            _bound = true;

            _hud = root.Q("hud");
            _menu = root.Q("main-menu");
            _pause = root.Q("pause");
            _complete = root.Q("complete");
            _settings = root.Q("settings");
            _controls = root.Q("controls");
            _objective = root.Q<Label>("objective-text");
            _objectiveCount = root.Q<Label>("objective-count");
            _timer = root.Q<Label>("timer");
            _toast = root.Q<Label>("toast");
            _best = root.Q<Label>("best-time");
            _completeTime = root.Q<Label>("complete-time");
            _completeBest = root.Q<Label>("complete-best");
            _qualityNote = root.Q<Label>("quality-note");

            Wire(root, "btn-play", () => flow.StartRun());
            Wire(root, "btn-settings", () => OpenSub(_settings, _menu));
            Wire(root, "btn-controls", () => OpenSub(_controls, _menu));
            Wire(root, "btn-exit", () => flow.QuitGame());

            Wire(root, "btn-resume", () => flow.Resume());
            Wire(root, "btn-restart", () => flow.Restart());
            Wire(root, "btn-pause-settings", () => OpenSub(_settings, _pause));
            Wire(root, "btn-pause-controls", () => OpenSub(_controls, _pause));
            Wire(root, "btn-mainmenu", () => flow.QuitToMenu());
            Wire(root, "btn-pause-exit", () => flow.QuitGame());

            Wire(root, "btn-again", () => flow.Restart());
            Wire(root, "btn-complete-menu", () => flow.QuitToMenu());

            Wire(root, "btn-settings-back", () => CloseSubPanel());
            Wire(root, "btn-controls-back", () => CloseSubPanel());

            _qLow = Wire(root, "q-low", () => GameSettings.SetQuality(GraphicsQuality.Low));
            _qMed = Wire(root, "q-medium", () => GameSettings.SetQuality(GraphicsQuality.Medium));
            _qHigh = Wire(root, "q-high", () => GameSettings.SetQuality(GraphicsQuality.High));

            BindSlider(root, "vol-master", GameSettings.MasterVolume, GameSettings.SetMasterVolume);
            BindSlider(root, "vol-music", GameSettings.MusicVolume, GameSettings.SetMusicVolume);
            BindSlider(root, "vol-fx", GameSettings.FxVolume, v => GameSettings.SetFxVolume(v), previewFx: true);
            BindSlider(root, "mouse-sens", GameSettings.MouseSensitivity, GameSettings.SetMouseSensitivity);
            BindToggle(root, "invert-y", GameSettings.InvertY, GameSettings.SetInvertY);
            var fullscreen = BindToggle(root, "fullscreen", GameSettings.Fullscreen, GameSettings.SetFullscreen);
            if (Application.isEditor) fullscreen.SetEnabled(false);

            GameSettings.Changed += RefreshQuality;
            RefreshQuality();
        }

        void OnDestroy() => GameSettings.Changed -= RefreshQuality;

        void Update()
        {
            if (_toast != null && _toast.ClassListContains("toast--visible") && Time.unscaledTime > _toastUntil)
                _toast.RemoveFromClassList("toast--visible");
        }

        // ---------------------------------------------------------------- screens

        public void ShowMainMenu()
        {
            Bind();
            float best = GameSettings.BestTime;
            _best.text = best > 0f ? "BEST  " + FormatTime(best) : "";
            Show(_menu);
            Focus(_menu);
        }

        public void ShowHud() { Bind(); Show(_hud); }

        public void ShowPause() { Show(_pause); Focus(_pause); }

        public void ShowComplete(float time, float best, bool record)
        {
            _completeTime.text = FormatTime(time);
            _completeBest.text = record ? "NEW BEST" : "BEST  " + FormatTime(best);
            Show(_complete);
            Focus(_complete);
        }

        void OpenSub(VisualElement panel, VisualElement from)
        {
            _subReturn = from;
            Show(panel);
            Focus(panel);
        }

        /// <summary>Closes Settings/Controls if one is open. Returns false when there was nothing to close.</summary>
        public bool CloseSubPanel()
        {
            if (_subReturn == null) return false;
            var back = _subReturn;
            _subReturn = null;
            Show(back);
            Focus(back);
            return true;
        }

        void Show(VisualElement screen)
        {
            foreach (var s in new[] { _hud, _menu, _pause, _complete, _settings, _controls })
                s.style.display = s == screen ? DisplayStyle.Flex : DisplayStyle.None;
            if (screen != _settings && screen != _controls) _subReturn = null;
        }

        static void Focus(VisualElement screen) => screen.Q<Button>()?.Focus();

        // ---------------------------------------------------------------- HUD

        public void SetTimer(float seconds) { if (_timer != null) _timer.text = FormatTime(seconds); }

        public void SetObjective(string text, int hit, int total)
        {
            Bind();
            _objective.text = text;
            _objectiveCount.text = total > 0 ? $"TARGETS  {hit} / {total}" : "";
            _objectiveCount.style.display = total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Toast(string message)
        {
            if (_toast == null || string.IsNullOrEmpty(message)) return;
            _toast.text = message.ToUpperInvariant();
            _toast.AddToClassList("toast--visible");
            _toastUntil = Time.unscaledTime + toastSeconds;
        }

        public static string FormatTime(float s)
        {
            int m = (int)(s / 60f);
            return $"{m:00}:{s - m * 60f:00.00}";
        }

        // ---------------------------------------------------------------- settings

        void RefreshQuality()
        {
            var q = GameSettings.Quality;
            _qLow.EnableInClassList("segment--selected", q == GraphicsQuality.Low);
            _qMed.EnableInClassList("segment--selected", q == GraphicsQuality.Medium);
            _qHigh.EnableInClassList("segment--selected", q == GraphicsQuality.High);
            _qualityNote.text = q switch
            {
                GraphicsQuality.Low => "75% render scale · no shadows or MSAA · low-res reflections · no depth of field",
                GraphicsQuality.Medium => "Full resolution · 2x MSAA · soft shadows · half-res reflections · light blur",
                _ => "Full resolution · 4x MSAA · high-res shadows · full reflections · bokeh depth of field",
            };
        }

        static Button Wire(VisualElement root, string name, Action onClick)
        {
            var b = root.Q<Button>(name);
            b.clicked += () => { GameAudio.Play(Sfx.UiClick, 0.6f); onClick(); };
            b.RegisterCallback<PointerEnterEvent>(_ => GameAudio.Play(Sfx.UiHover, 0.35f));
            return b;
        }

        static void BindSlider(VisualElement root, string name, float value, Action<float> set, bool previewFx = false)
        {
            var s = root.Q<Slider>(name);
            s.SetValueWithoutNotify(value);
            s.RegisterValueChangedCallback(e => set(e.newValue));
            if (previewFx) s.RegisterCallback<PointerUpEvent>(_ => GameAudio.Play(Sfx.TargetHit, 0.6f), TrickleDown.TrickleDown);
        }

        static Toggle BindToggle(VisualElement root, string name, bool value, Action<bool> set)
        {
            var t = root.Q<Toggle>(name);
            t.SetValueWithoutNotify(value);
            t.RegisterValueChangedCallback(e => { GameAudio.Play(Sfx.UiClick, 0.6f); set(e.newValue); });
            return t;
        }
    }
}
