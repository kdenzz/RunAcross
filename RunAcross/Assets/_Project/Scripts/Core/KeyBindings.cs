using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunAcross.Core
{
    /// <summary>
    /// Keyboard &amp; mouse rebinding with two slots per action. The slots are the first two
    /// "Keyboard&amp;Mouse" bindings of each action (or composite part) in InputSystem_Actions;
    /// an empty path is an unbound slot. Overrides persist in PlayerPrefs as JSON. Gamepad
    /// bindings are fixed.
    /// </summary>
    public static class KeyBindings
    {
        public const int Slots = 2;
        const string Group = "Keyboard&Mouse";
        const string PrefsKey = "ra.binds";

        public readonly struct Entry
        {
            public readonly string label, action, part;
            public Entry(string label, string action, string part = null) { this.label = label; this.action = action; this.part = part; }
        }

        public static readonly Entry[] All =
        {
            new("Move forward", "Player/Move", "up"),
            new("Move back", "Player/Move", "down"),
            new("Move left", "Player/Move", "left"),
            new("Move right", "Player/Move", "right"),
            new("Jump", "Player/Jump"),
            new("Sprint", "Player/Sprint"),
            new("Shoot", "Player/Attack"),
            new("Restart level", "Game/Restart"),
        };

        public static event Action Changed;
        public static bool Ready => _asset != null;
        public static bool IsRebinding => _op != null;
        /// <summary>True during a rebind and briefly after, so the key that finished it doesn't also act (Esc closing the panel, Space re-pressing a button).</summary>
        public static bool Busy => _op != null || Time.unscaledTime - _endedAt < 0.25f;

        static InputActionAsset _asset;
        static InputActionRebindingExtensions.RebindingOperation _op;
        static float _endedAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _asset = null; _op = null; _endedAt = -1f; Changed = null; }

        public static void Init(InputActionAsset asset)
        {
            if (_asset == asset) return;
            _asset = asset;
            asset.RemoveAllBindingOverrides(); // the asset object outlives play sessions in the Editor
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { asset.LoadBindingOverridesFromJson(json); }
                catch (Exception e) { Debug.LogWarning("[Binds] Ignoring bad saved bindings: " + e.Message); }
            }
            Changed?.Invoke(); // UI that bound before Init refreshes now
        }

        // ---------------------------------------------------------------- queries

        static InputAction Action(Entry e) => _asset.FindAction(e.action, true);

        /// <summary>Binding index of a slot, or -1 if the asset has no such slot.</summary>
        static int BindingIndex(Entry e, int slot)
        {
            var bindings = Action(e).bindings;
            int n = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.groups == null || !b.groups.Contains(Group)) continue;
                if (e.part != null && !(b.isPartOfComposite && string.Equals(b.name, e.part, StringComparison.OrdinalIgnoreCase))) continue;
                if (e.part == null && b.isPartOfComposite) continue;
                if (n++ == slot) return i;
            }
            return -1;
        }

        static string Path(Entry e, int slot)
        {
            int i = BindingIndex(e, slot);
            return i < 0 ? "" : Action(e).bindings[i].effectivePath ?? "";
        }

        /// <summary>Button text for a slot, e.g. "SPACE", "LMB", or "—" when unbound.</summary>
        public static string Display(Entry e, int slot) => Pretty(Path(e, slot));

        /// <summary>The bound keys joined for a hint, e.g. "R" or "R / F5". Empty when nothing is bound.</summary>
        public static string Hint(string action)
        {
            foreach (var e in All)
            {
                if (e.action != action) continue;
                string a = Path(e, 0), b = Path(e, 1);
                if (a == "") return b == "" ? "" : Pretty(b);
                return b == "" ? Pretty(a) : Pretty(a) + " / " + Pretty(b);
            }
            return "";
        }

        static string Pretty(string path)
        {
            if (string.IsNullOrEmpty(path)) return "—";
            switch (path)
            {
                case "<Mouse>/leftButton": return "LMB";
                case "<Mouse>/rightButton": return "RMB";
                case "<Mouse>/middleButton": return "MMB";
                case "<Mouse>/backButton": return "MOUSE 4";
                case "<Mouse>/forwardButton": return "MOUSE 5";
            }
            string s = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return s.Replace("Arrow", "").ToUpperInvariant().Trim();
        }

        // ---------------------------------------------------------------- changes

        /// <summary>
        /// Waits for the next key or mouse button and puts it in the slot. Esc cancels, Backspace clears
        /// the slot. A key already used elsewhere is taken from that slot. <paramref name="done"/> gets a
        /// status line for the UI (null when there's nothing to report). Returns false if it didn't start.
        /// </summary>
        public static bool Rebind(Entry e, int slot, Action<string> done)
        {
            if (Busy) return false;
            var action = Action(e);
            int index = BindingIndex(e, slot);
            if (index < 0) return false;

            bool wasEnabled = action.enabled;
            action.Disable(); // an action must be disabled while it is rebound

            void Finish(string message)
            {
                _op?.Dispose();
                _op = null;
                _endedAt = Time.unscaledTime;
                if (wasEnabled) action.Enable();
                Save();
                done?.Invoke(message);
            }

            _op = action.PerformInteractiveRebinding(index)
                .WithExpectedControlType("Button")
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithMatchingEventsBeingSuppressed()
                .OnMatchWaitForAnother(0.05f)
                .WithTimeout(8f)
                .OnCancel(_ => Finish(null))
                .OnComplete(op =>
                {
                    string path = action.bindings[index].effectivePath;
                    if (path == "<Keyboard>/backspace")
                    {
                        action.ApplyBindingOverride(index, "");
                        Finish(e.label.ToUpperInvariant() + " SLOT CLEARED");
                        return;
                    }
                    Finish(TakeFromOthers(e, slot, path));
                });
            _op.Start();
            return true;
        }

        /// <summary>Unbinds <paramref name="path"/> from every other slot so one key does one thing.</summary>
        static string TakeFromOthers(Entry owner, int ownerSlot, string path)
        {
            string moved = null;
            foreach (var e in All)
            {
                for (int s = 0; s < Slots; s++)
                {
                    if (e.action == owner.action && e.part == owner.part && s == ownerSlot) continue;
                    if (!string.Equals(Path(e, s), path, StringComparison.OrdinalIgnoreCase)) continue;
                    Action(e).ApplyBindingOverride(BindingIndex(e, s), "");
                    if (e.action != owner.action || e.part != owner.part) moved = e.label;
                }
            }
            return moved == null ? null : $"{Pretty(path)} MOVED FROM {moved.ToUpperInvariant()}";
        }

        public static void ResetAll()
        {
            if (_op != null) return;
            _asset.RemoveAllBindingOverrides();
            Save();
        }

        static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, _asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Stops a rebind in progress, e.g. when the panel closes.</summary>
        public static void Cancel() => _op?.Cancel();
    }
}
