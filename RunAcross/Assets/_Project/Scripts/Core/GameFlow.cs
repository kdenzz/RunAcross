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
        [Tooltip("In course order; the first one still closed sets the HUD objective.")]
        public PathOpener[] path;
        public float blendSeconds = 1.6f;

        public static GameFlow Instance { get; private set; }
        public GameState State { get; private set; }
        public float RunTime { get; private set; }

        static bool _skipMenuOnLoad;
        CinemachineInputAxisController _look;
        CinemachineOrbitalFollow _orbit;
        float _startDelay;
        float[] _baseGains;

        void Awake()
        {
            Instance = this;
            GameSettings.Load();
            _look = playerCamera.GetComponent<CinemachineInputAxisController>();
            _orbit = playerCamera.GetComponent<CinemachineOrbitalFollow>();
            if (brain) brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendSeconds);
            foreach (var p in path)
            {
                p.Progressed += _ => RefreshObjective();
                p.Opened += o => { ui.Toast(o.openedMessage); RefreshObjective(); };
            }
            Checkpoint.Reached += _ => ui.Toast("Checkpoint");
            GoalZone.Reached += Complete;
            player.Respawned += () => ui.Toast("Respawned at checkpoint");
            GameSettings.Changed += ApplyLookSettings;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyLookSettings;
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            ApplyLookSettings();
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
            ui.Toast(path.Length > 0 ? path[0].objective : "Reach the beacon");
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
            float best = GameSettings.BestTime;
            bool record = best <= 0f || RunTime < best;
            if (record) GameSettings.BestTime = RunTime;
            Audio.GameAudio.Play(Audio.Sfx.Complete);
            ui.ShowComplete(RunTime, record ? RunTime : best, record);
        }

        public void Restart()
        {
            _skipMenuOnLoad = true;
            Reload();
        }

        public void QuitToMenu() => Reload();

        void Reload()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
            foreach (var p in path)
            {
                if (p.IsOpen) continue;
                ui.SetObjective(p.objective, p.HitCount, p.TargetCount);
                return;
            }
            ui.SetObjective("Reach the beacon at the end of the course", 0, 0);
        }

        void ApplyLookSettings()
        {
            if (_look == null) return;
            var controllers = _look.Controllers;
            if (_baseGains == null)
            {
                _baseGains = new float[controllers.Count];
                for (int i = 0; i < controllers.Count; i++) _baseGains[i] = controllers[i].Input.Gain;
            }
            for (int i = 0; i < controllers.Count && i < _baseGains.Length; i++)
            {
                var c = controllers[i];
                float gain = _baseGains[i];
                if (c.Name.Contains("Orbit X")) gain *= GameSettings.MouseSensitivity;
                else if (c.Name.Contains("Orbit Y")) gain *= GameSettings.MouseSensitivity * (GameSettings.InvertY ? -1f : 1f);
                c.Input.Gain = gain;
            }
        }
    }
}
