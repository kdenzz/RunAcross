using System;
using RunAcross.Audio;
using RunAcross.Core;
using RunAcross.Online;
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
        VisualElement _boards, _boardTabs, _boardRows, _namePrompt, _completeRows;
        Label _boardStatus, _boardMe, _completeRank, _nameError, _settingsNameNote;
        TextField _nameField, _settingsName;
        Button _nameSubmit;
        int _boardLevel, _boardToken, _resultToken;
        (int level, float time)? _awaitingName; // finished run waiting on the callsign prompt
        Button _next, _again;
        VisualElement _bindList;
        Label _bindStatus, _hudHint, _countdown;
        float _countdownUntil;
        Button[,] _bindSlots;
        Label _completeLevel;
        VisualElement _subReturn; // which screen Settings/Controls go back to
        Label _objective, _objectiveCount, _timer, _toast, _best, _completeTime, _completeBest, _qualityNote;
        Button _qLow, _qMed, _qHigh;
        float _toastUntil;
        bool _bound;

        void OnEnable() => Bind();

        /// <summary>True while a text field has keyboard focus, so hotkeys should stand down.</summary>
        public bool IsTyping =>
            GetComponent<UIDocument>().rootVisualElement?.focusController?.focusedElement is VisualElement f &&
            (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);

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
            _boards = root.Q("boards");
            _boardTabs = root.Q("board-tabs");
            _boardRows = root.Q("board-rows");
            _boardStatus = root.Q<Label>("board-status");
            _boardMe = root.Q<Label>("board-me");
            _namePrompt = root.Q("name-prompt");
            _nameField = root.Q<TextField>("name-field");
            _nameError = root.Q<Label>("name-error");
            _completeRank = root.Q<Label>("complete-rank");
            _completeRows = root.Q("complete-rows");
            _settingsName = root.Q<TextField>("settings-name");
            _settingsNameNote = root.Q<Label>("settings-name-note");
            _bindList = root.Q("bind-list");
            _bindStatus = root.Q<Label>("bind-status");
            _hudHint = root.Q<Label>("hud-hint");
            _countdown = root.Q<Label>("countdown");

            Wire(root, "btn-play", () => { BuildLevelList(); OpenSub(_levels, _menu); });
            Wire(root, "btn-levels-back", () => CloseSubPanel());
            Wire(root, "btn-boards", () => { BuildBoardTabs(); OpenSub(_boards, _menu); SelectBoard(_boardLevel); });
            Wire(root, "btn-boards-back", () => CloseSubPanel());
            Wire(root, "btn-settings", () => OpenSettings(_menu));
            Wire(root, "btn-controls", () => OpenControls(_menu));
            Wire(root, "btn-exit", () => flow.QuitGame());

            Wire(root, "btn-resume", () => flow.Resume());
            Wire(root, "btn-restart", () => flow.Restart());
            Wire(root, "btn-pause-settings", () => OpenSettings(_pause));
            Wire(root, "btn-pause-controls", () => OpenControls(_pause));
            Wire(root, "btn-mainmenu", () => flow.QuitToMenu());
            Wire(root, "btn-pause-exit", () => flow.QuitGame());

            _again = Wire(root, "btn-again", () => flow.Restart());
            _next = Wire(root, "btn-next", () => flow.NextLevel());
            Wire(root, "btn-complete-menu", () => flow.QuitToMenu());

            Wire(root, "btn-settings-back", () => CloseSubPanel());
            Wire(root, "btn-settings-name", SaveSettingsName);

            _nameSubmit = Wire(root, "btn-name-submit", SubmitName);
            Wire(root, "btn-name-skip", SkipName);
            _nameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) SubmitName();
            });
            Wire(root, "btn-controls-back", () => CloseSubPanel());
            Wire(root, "btn-binds-reset", () => { KeyBindings.ResetAll(); _bindStatus.text = "CONTROLS RESET TO DEFAULTS"; });
            BuildBindRows();
            KeyBindings.Changed += RefreshBinds;
            RefreshBinds();

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

        void OnDestroy()
        {
            GameSettings.Changed -= RefreshQuality;
            KeyBindings.Changed -= RefreshBinds;
        }

        void Update()
        {
            if (_toast != null && _toast.ClassListContains("toast--visible") && Time.unscaledTime > _toastUntil)
                _toast.RemoveFromClassList("toast--visible");
            if (_countdown != null && _countdown.ClassListContains("countdown--visible") && Time.unscaledTime > _countdownUntil)
                _countdown.RemoveFromClassList("countdown--visible");
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
            if (screen != _controls) KeyBindings.Cancel();
            foreach (var s in new[] { _hud, _menu, _pause, _complete, _settings, _controls, _levels, _boards, _namePrompt })
                s.style.display = s == screen ? DisplayStyle.Flex : DisplayStyle.None;
            if (screen != _settings && screen != _controls && screen != _levels && screen != _boards) _subReturn = null;
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

        /// <summary>Big centred 3 / 2 / 1 / GO. Each call pops the label in; it fades shortly after.</summary>
        public void ShowCountdown(string text)
        {
            Bind();
            _countdown.text = text;
            _countdown.EnableInClassList("countdown--go", text == "GO");
            _countdown.RemoveFromClassList("countdown--settled"); // jump back to the big scale...
            _countdown.AddToClassList("countdown--visible");
            _countdown.schedule.Execute(() => _countdown.AddToClassList("countdown--settled")).StartingIn(16); // ...then ease down
            _countdownUntil = Time.unscaledTime + (text == "GO" ? 0.6f : 0.85f);
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

        // ---------------------------------------------------------------- online

        /// <summary>Finish screen: asks for a callsign the first time, then submits the run and shows the world rank.</summary>
        public async void ShowOnlineResult(int level, float time)
        {
            int token = ++_resultToken;
            _completeRows.Clear();
            _completeRank.text = "CONNECTING…";
            bool online = await OnlineBoards.EnsureReady();
            if (!this || token != _resultToken) return;
            if (!online)
            {
                OnlineBoards.SavePending(level, time);
                _completeRank.text = "OFFLINE  ·  TIME WILL BE SUBMITTED LATER";
                return;
            }
            if (!OnlineBoards.HasName)
            {
                _awaitingName = (level, time);
                _completeRank.text = "";
                _nameField.SetValueWithoutNotify("");
                _nameError.text = "";
                Show(_namePrompt);
                _nameField.Focus();
                return;
            }
            await SubmitAndShow(level, time, token);
        }

        async System.Threading.Tasks.Task SubmitAndShow(int level, float time, int token)
        {
            _completeRank.text = "SUBMITTING…";
            bool sent = await OnlineBoards.Submit(level, time);
            if (!this || token != _resultToken) return;
            var view = await OnlineBoards.Fetch(level, 5);
            if (!this || token != _resultToken) return;
            if (!sent || view.error != null)
            {
                _completeRank.text = sent ? view.error : "OFFLINE  ·  TIME WILL BE SUBMITTED LATER";
                return;
            }
            _completeRank.text = view.me is { } me ? $"WORLD  #{me.rank}  OF  {view.total}" : $"{view.total} RUNNERS";
            FillRows(_completeRows, view);
        }

        async void SubmitName()
        {
            if (!_nameSubmit.enabledSelf) return;
            string error = OnlineBoards.ValidateName(_nameField.value);
            if (error != null) { _nameError.text = error; return; }
            _nameError.text = "SAVING…";
            _nameSubmit.SetEnabled(false);
            error = await OnlineBoards.SetName(_nameField.value);
            if (!this) return;
            _nameSubmit.SetEnabled(true);
            if (error != null) { _nameError.text = error; return; }
            Show(_complete);
            Focus(_complete);
            if (_awaitingName is { } run)
            {
                _awaitingName = null;
                await SubmitAndShow(run.level, run.time, ++_resultToken);
            }
        }

        void SkipName()
        {
            if (_awaitingName is { } run) OnlineBoards.SavePending(run.level, run.time);
            _awaitingName = null;
            _completeRank.text = "NOT SUBMITTED  ·  SET A CALLSIGN TO JOIN THE BOARD";
            Show(_complete);
            Focus(_complete);
        }

        void BuildBoardTabs()
        {
            _boardTabs.Clear();
            for (int i = 0; i < Levels.Count; i++)
            {
                int index = i;
                var b = new Button(() => { GameAudio.Play(Sfx.UiClick, 0.6f); SelectBoard(index); }) { text = $"{i + 1:00}  {Levels.All[i].title.ToUpperInvariant()}" };
                b.AddToClassList("segment");
                b.RegisterCallback<PointerEnterEvent>(_ => GameAudio.Play(Sfx.UiHover, 0.35f));
                _boardTabs.Add(b);
            }
        }

        async void SelectBoard(int level)
        {
            _boardLevel = level;
            for (int i = 0; i < _boardTabs.childCount; i++)
                _boardTabs[i].EnableInClassList("segment--selected", i == level);
            int token = ++_boardToken;
            _boardRows.Clear();
            _boardStatus.text = "LOADING…";
            _boardMe.text = "";
            var view = await OnlineBoards.Fetch(level, 10);
            if (!this || token != _boardToken) return;
            if (view.error != null) { _boardStatus.text = view.error; return; }
            _boardStatus.text = view.total == 0 ? "NO TIMES YET  ·  BE THE FIRST" : $"{view.total} RUNNER{(view.total == 1 ? "" : "S")}";
            FillRows(_boardRows, view);
            float local = Levels.BestTime(level);
            _boardMe.text = view.me is { } me ? $"YOU  #{me.rank}  ·  {FormatTime(me.time)}"
                : local > 0f ? "YOUR TIME ISN'T ON THE BOARD YET" : "";
        }

        /// <summary>Top rows, then the player's own row after a gap when they're outside the top.</summary>
        static void FillRows(VisualElement list, BoardView view)
        {
            list.Clear();
            foreach (var r in view.top) list.Add(RowElement(r));
            if (view.me is { } me && !view.top.Exists(r => r.isMe))
            {
                var gap = new VisualElement();
                gap.AddToClassList("board-row");
                gap.AddToClassList("board-row--gap");
                gap.Add(new Label("···") { pickingMode = PickingMode.Ignore });
                list.Add(gap);
                list.Add(RowElement(me));
            }
        }

        static VisualElement RowElement(BoardRow r)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("board-row");
            if (r.isMe) row.AddToClassList("board-row--me");
            void Add(string text, string cls)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                l.AddToClassList(cls);
                row.Add(l);
            }
            Add("#" + r.rank, "board-rank");
            Add(r.name, "board-name");
            Add(FormatTime(r.time), "board-time");
            return row;
        }

        // ---------------------------------------------------------------- controls (rebinding)

        void OpenControls(VisualElement from)
        {
            _bindStatus.text = "";
            RefreshBinds();
            OpenSub(_controls, from);
        }

        void BuildBindRows()
        {
            _bindList.Clear();
            _bindSlots = new Button[KeyBindings.All.Length, KeyBindings.Slots];
            for (int i = 0; i < KeyBindings.All.Length; i++)
            {
                var entry = KeyBindings.All[i];
                var row = new VisualElement();
                row.AddToClassList("bind-row");
                var label = new Label(entry.label);
                label.AddToClassList("bind-label");
                row.Add(label);
                for (int slot = 0; slot < KeyBindings.Slots; slot++)
                {
                    int s = slot;
                    Button b = null;
                    b = new Button(() => StartRebind(entry, s, b));
                    b.AddToClassList("bind-slot");
                    b.RegisterCallback<PointerEnterEvent>(_ => GameAudio.Play(Sfx.UiHover, 0.35f));
                    _bindSlots[i, slot] = b;
                    row.Add(b);
                }
                _bindList.Add(row);
            }
        }

        void StartRebind(KeyBindings.Entry entry, int slot, Button b)
        {
            bool started = KeyBindings.Rebind(entry, slot, message =>
            {
                GameAudio.Play(Sfx.UiClick, 0.6f);
                _bindStatus.text = message ?? "";
                RefreshBinds(); // also restores this button when cancelled
            });
            if (!started) return;
            GameAudio.Play(Sfx.UiClick, 0.6f);
            b.text = "PRESS A KEY";
            b.RemoveFromClassList("bind-slot--empty");
            b.AddToClassList("bind-slot--listening");
            _bindStatus.text = "";
        }

        void RefreshBinds()
        {
            if (_bindSlots == null || !KeyBindings.Ready) return;
            for (int i = 0; i < KeyBindings.All.Length; i++)
                for (int slot = 0; slot < KeyBindings.Slots; slot++)
                {
                    var b = _bindSlots[i, slot];
                    b.text = KeyBindings.Display(KeyBindings.All[i], slot);
                    b.RemoveFromClassList("bind-slot--listening");
                    b.EnableInClassList("bind-slot--empty", b.text == "—");
                }
            string restart = KeyBindings.Hint("Game/Restart");
            _hudHint.text = restart == "" ? "ESC  PAUSE" : $"ESC  PAUSE   ·   {restart}  RESTART";
            _again.text = restart == "" ? "RUN AGAIN" : $"RUN AGAIN  ·  {restart}";
        }

        // ---------------------------------------------------------------- settings

        void OpenSettings(VisualElement from)
        {
            _settingsName.SetValueWithoutNotify(OnlineBoards.DisplayName);
            _settingsNameNote.text = OnlineBoards.IsReady ? "" : "OFFLINE · NAME CHANGES NEED A CONNECTION";
            OpenSub(_settings, from);
        }

        async void SaveSettingsName()
        {
            _settingsNameNote.text = "SAVING…";
            string error = await OnlineBoards.SetName(_settingsName.value);
            if (!this) return;
            _settingsNameNote.text = error ?? "CALLSIGN SAVED";
            if (error == null) _settingsName.SetValueWithoutNotify(OnlineBoards.DisplayName);
        }


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
