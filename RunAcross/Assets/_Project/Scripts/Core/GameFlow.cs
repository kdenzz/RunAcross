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

        public static GameFlow Instance { get; private set; }
        public GameState State { get; private set; }
        public float RunTime { get; private set; }

        static bool _skipMenuOnLoad;
        Player.PlayerLook _look;
        CinemachineOrbitalFollow _orbit;
        float _startDelay;
        PathOpener _timed;

        void OnCheckpoint(Checkpoint _) => ui.Toast("Checkpoint");

        void Awake()
        {
            Instance = this;
            GameSettings.Load();
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

        void Start()
        {
            if (_skipMenuOnLoad) { _skipMenuOnLoad = false; ShowMenu(); StartRun(instant: true); }
            else ShowMenu();
        }

        void Update()
        {
            var kb = Keyboard.current;
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) ||
                        (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

            switch (State)
            {
                case GameState.Starting:
                    _startDelay -= Time.unscaledDeltaTime;
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
            _startDelay = instant ? 0f : blendSeconds * 0.85f;
            if (instant && brain) brain.ResetState();
        }

        void BeginControl()
        {
            State = GameState.Playing;
            SetControl(true);
            ui.Toast(Levels.Label(levelIndex));
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
