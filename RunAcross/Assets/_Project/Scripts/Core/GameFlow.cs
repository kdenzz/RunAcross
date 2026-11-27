using RunAcross.Gameplay;
using RunAcross.Player;
using RunAcross.UI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RunAcross.Core
{
    public enum GameState { Menu, Starting, Playing, Paused, Complete }

    /// <summary>
    /// Menu → play → pause → complete. The main menu sits on the fixed hero shot (the portfolio render);
    /// Play blends Cinemachine over to the third-person camera and hands control to the player.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public CinemachineCamera menuCamera;
        public CinemachineCamera playerCamera;
        public CinemachineBrain brain;
        public PlayerMotor player;
        public PlayerShooter shooter;
        public Transform playerStart;
        public GameUI ui;
        public GraphicsApplier graphics;
        [Tooltip("Index into Levels.All.")]
        public int levelIndex;
        [Tooltip("In course order; the first one still closed sets the HUD objective.")]
        public PathOpener[] path;
        public float blendSeconds = 1.6f;
        [Tooltip("3-2-1-GO before control after a restart or a direct level load.")]
        public int countdownFrom = 3;
        [Tooltip("Total length of the 3-2-1, in seconds; GO shows as it ends.")]
        public float countdownSeconds = 1.5f;

        public static GameFlow Instance { get; private set; }
        public GameState State { get; private set; }
        public float RunTime { get; private set; }

        static bool _skipMenuOnLoad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _skipMenuOnLoad = false;
        Player.PlayerLook _look;
        CinemachineOrbitalFollow _orbit;
        float _startDelay;
        PathOpener _timed;
        bool _countingDown;
        int _countShown;
        InputAction _restart;

        void OnCheckpoint(Checkpoint _) => ui.Toast("Checkpoint");

        void Awake()
        {
            Instance = this;
            GameSettings.Load();
            KeyBindings.Init(player.actions);
            _restart = player.actions.FindAction("Game/Restart", true);
            _look = playerCamera.GetComponent<Player.PlayerLook>();
            _orbit = playerCamera.GetComponent<CinemachineOrbitalFollow>();
            if (brain) brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendSeconds);
            foreach (var p in path)
            {
                p.Progressed += _ => RefreshObjective();
                p.Opened += o => { ui.Toast(o.openedMessage); RefreshObjective(); };
                p.TimedOut += _ => ui.Toast("Too slow. Targets reset");
            }
            Checkpoint.Reached += OnCheckpoint;
            GoalZone.Reached += Complete;
            player.Respawned += () => ui.Toast("Respawned at checkpoint");
        }

        void OnDestroy()
        {
            Checkpoint.Reached -= OnCheckpoint; // static events outlive the scene
            GoalZone.Reached -= Complete;
            if (Instance == this) Instance = null;
        }

        void OnEnable() => _restart?.Enable();
        void OnDisable() { KeyBindings.Cancel(); _restart?.Disable(); }

        void Start()
        {
            _ = Online.OnlineBoards.EnsureReady(); // sign in while the menu is up; fails soft when offline
            if (_skipMenuOnLoad) { _skipMenuOnLoad = false; ShowMenu(); StartRun(instant: true); }
            else ShowMenu();
        }

        void Update()
        {
            if (KeyBindings.Busy) return; // the key that ends a rebind (Esc to cancel) mustn't also close the panel
            var kb = Keyboard.current;
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) ||
                        (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            bool restart = _restart.WasPressedThisFrame();
            // Restart (R / Select by default, rebindable) works while playing or on the finish screen, not while typing a callsign
            if (restart && (State == GameState.Playing || State == GameState.Complete) && !ui.IsTyping)
            {
                Restart();
                return;
            }

            switch (State)
            {
                case GameState.Starting:
                    _startDelay -= Mathf.Min(Time.unscaledDeltaTime, 0.1f); // a scene-load hitch mustn't eat the countdown
                    if (_countingDown)
                    {
                        int n = Mathf.CeilToInt(_startDelay / countdownSeconds * countdownFrom);
                        if (n > 0 && n != _countShown)
                        {
                            _countShown = n;
                            ui.ShowCountdown(n.ToString());
                            Audio.GameAudio.Play(Audio.Sfx.UiClick);
                        }
                    }
                    if (_startDelay <= 0f) BeginControl();
                    break;
                case GameState.Playing:
                    RunTime += Time.deltaTime;
                    ui.SetTimer(RunTime);
                    if (_timed != null) RefreshObjective(); // keep the challenge countdown ticking
                    if (back) Pause();
                    break;
                case GameState.Paused:
                    if (back && !ui.CloseSubPanel()) Resume();
                    break;
                case GameState.Menu:
                    if (back) ui.CloseSubPanel();
                    break;
            }
        }

        // ---------------------------------------------------------------- states

        void ShowMenu()
        {
            State = GameState.Menu;
            Time.timeScale = 1f;
            menuCamera.Priority = 20;
            playerCamera.Priority = 0;
            SetControl(false);
            graphics.SetGameplayFocus(false);
            ui.ShowMainMenu();
        }

        /// <summary>Main-menu Play: blend to the player camera, then hand over control.</summary>
        public void StartRun() => StartRun(false);

        void StartRun(bool instant)
        {
            // Face down the course (through the arch) with the orbit camera behind the courier.
            if (instant) player.Teleport(playerStart.position, playerStart.rotation);
            else player.FaceTowards(playerStart.forward, blendSeconds); // turns round during the camera blend
            player.SetCheckpoint(playerStart.position, playerStart.rotation);
            if (_orbit)
            {
                _orbit.HorizontalAxis.Value = playerStart.eulerAngles.y;
                _orbit.VerticalAxis.Value = 14f;
            }
            menuCamera.Priority = 0;
            playerCamera.Priority = 20;
            graphics.SetGameplayFocus(true);
            ui.ShowHud();
            RunTime = 0f;
            ui.SetTimer(0f);
            RefreshObjective();
            State = GameState.Starting;
            // a restart (or a direct level load) counts down 3-2-1-GO with the courier frozen in place
            _countingDown = instant && countdownFrom > 0 && countdownSeconds > 0f;
            _countShown = -1;
            _startDelay = _countingDown ? countdownSeconds : instant ? 0f : blendSeconds * 0.85f;
            if (instant && brain) brain.ResetState();
            if (_countingDown) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void BeginControl()
        {
            State = GameState.Playing;
            SetControl(true);
            ui.Toast(Levels.Label(levelIndex));
            if (_countingDown)
            {
                _countingDown = false;
                ui.ShowCountdown("GO");
                Audio.GameAudio.Play(Audio.Sfx.Checkpoint);
            }
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            State = GameState.Paused;
            Time.timeScale = 0f;
            SetControl(false);
            ui.ShowPause();
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            State = GameState.Playing;
            Time.timeScale = 1f;
            ui.ShowHud();
            SetControl(true);
        }

        void Complete()
        {
            if (State != GameState.Playing) return;
            State = GameState.Complete;
            SetControl(false);
            // swing round to face the courier standing in the ring
            if (_orbit)
            {
                _orbit.HorizontalAxis.Value = Mathf.Repeat(player.transform.eulerAngles.y + 180f + 180f, 360f) - 180f;
                _orbit.VerticalAxis.Value = 8f;
            }
            float best = Levels.BestTime(levelIndex);
            bool record = best <= 0f || RunTime < best;
            if (record) Levels.SetBestTime(levelIndex, RunTime);
            bool hasNext = levelIndex + 1 < Levels.Count;
            if (hasNext) Levels.Unlock(levelIndex + 1);
            Audio.GameAudio.Play(Audio.Sfx.Complete);
            ui.ShowComplete(Levels.Label(levelIndex), RunTime, record ? RunTime : best, record, hasNext);
            ui.ShowOnlineResult(levelIndex, RunTime);
        }

        public void Restart() => Reload();

        /// <summary>The main menu lives in Level 01's scene.</summary>
        public void QuitToMenu() => Load(Levels.All[0].scene, skipMenu: false);

        void Reload() => Load(SceneManager.GetActiveScene().name, skipMenu: true);

        /// <summary>From the level select or the finish screen.</summary>
        public void PlayLevel(int index)
        {
            if (!Levels.IsUnlocked(index)) return;
            if (index == levelIndex && State == GameState.Menu) StartRun();
            else Load(Levels.All[index].scene, skipMenu: true);
        }

        public void NextLevel() => PlayLevel(levelIndex + 1);

        void Load(string scene, bool skipMenu)
        {
            _skipMenuOnLoad = skipMenu;
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- helpers

        void SetControl(bool on)
        {
            player.SetInputEnabled(on);
            if (_look) _look.enabled = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        void RefreshObjective()
        {
            _timed = null;
            foreach (var p in path)
            {
                if (p.IsOpen) continue;
                float left = p.TimeRemaining;
                if (left >= 0f) _timed = p;
                ui.SetObjective(p.objective, p.HitCount, p.TargetCount, left);
                return;
            }
            ui.SetObjective("Reach the beacon at the end of the course", 0, 0);
        }
    }
}
