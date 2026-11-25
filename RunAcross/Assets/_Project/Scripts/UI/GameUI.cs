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

        VisualElement _hud, _menu, _pause, _complete, _settings, _controls, _levels, _levelList;
        Button _next;
        Label _completeLevel;
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
            _levels = root.Q("levels");
            _levelList = root.Q("level-list");
            _completeLevel = root.Q<Label>("complete-level");
            _objective = root.Q<Label>("objective-text");
            _objectiveCount = root.Q<Label>("objective-count");
            _timer = root.Q<Label>("timer");
            _toast = root.Q<Label>("toast");
            _best = root.Q<Label>("best-time");
            _completeTime = root.Q<Label>("complete-time");
            _completeBest = root.Q<Label>("complete-best");
            _qualityNote = root.Q<Label>("quality-note");

            Wire(root, "btn-play", () => { BuildLevelList(); OpenSub(_levels, _menu); });
            Wire(root, "btn-levels-back", () => CloseSubPanel());
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
            _next = Wire(root, "btn-next", () => flow.NextLevel());
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
            int cleared = Levels.Cleared();
            _best.text = cleared > 0 ? $"LEVELS CLEARED  {cleared} / {Levels.Count}" : "";
            Show(_menu);
            Focus(_menu);
        }

        public void ShowHud() { Bind(); Show(_hud); }

        public void ShowPause() { Show(_pause); Focus(_pause); }

        public void ShowComplete(string level, float time, float best, bool record, bool hasNext)
        {
            _completeLevel.text = level;
            _next.style.display = hasNext ? DisplayStyle.Flex : DisplayStyle.None;
            _next.text = hasNext ? "NEXT  ·  " + Levels.All[flow.levelIndex + 1].title.ToUpperInvariant() : "";
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
            foreach (var s in new[] { _hud, _menu, _pause, _complete, _settings, _controls, _levels })
                s.style.display = s == screen ? DisplayStyle.Flex : DisplayStyle.None;
            if (screen != _settings && screen != _controls && screen != _levels) _subReturn = null;
        }

        static void Focus(VisualElement screen) => screen.Q<Button>()?.Focus();

        // ---------------------------------------------------------------- HUD

        public void SetTimer(float seconds) { if (_timer != null) _timer.text = FormatTime(seconds); }

        public void SetObjective(string text, int hit, int total, float timeLeft = -1f)
        {
            Bind();
            _objective.text = text;
            _objectiveCount.text = total > 0 ? $"TARGETS  {hit} / {total}" : "";
            if (timeLeft >= 0f) _objectiveCount.text += $"   ·   {timeLeft:0.0}s LEFT";
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

        void BuildLevelList()
        {
            _levelList.Clear();
            for (int i = 0; i < Levels.Count; i++)
            {
                int index = i;
                bool unlocked = Levels.IsUnlocked(i);
                var b = new Button(() => { GameAudio.Play(Sfx.UiClick, 0.6f); flow.PlayLevel(index); });
                b.AddToClassList("menu-button");
                b.AddToClassList("level-button");
                if (i == 0) b.AddToClassList("menu-button--primary");
                b.Add(new Label(Levels.Label(i)));
                float best = Levels.BestTime(i);
                var right = new Label(!unlocked ? "LOCKED" : best > 0f ? "BEST  " + FormatTime(best) : "NEW");
                right.AddToClassList("level-best");
                b.Add(right);
                b.text = "";
                b.SetEnabled(unlocked);
                b.RegisterCallback<PointerEnterEvent>(_ => GameAudio.Play(Sfx.UiHover, 0.35f));
                _levelList.Add(b);
            }
            // focus the newest unlocked level
            for (int i = Levels.Count - 1; i >= 0; i--)
                if (Levels.IsUnlocked(i)) { ((Button)_levelList[i]).Focus(); break; }
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
