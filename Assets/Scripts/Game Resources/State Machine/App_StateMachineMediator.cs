using CoreResources.Singleton;
using CoreResources.Managers.InputManagement;
using GameResources.Pooling;
using UnityEngine;
using GameResources.UI;
using GameResources.Gameplay;
using GameResources.Gameplay.VRController;
using CoreResources.Managers;
using GameResources.PerceptionManagement;

namespace GameResources.StateMachine
{
    public class App_StateMachineMediator : MonoSingleton<App_StateMachineMediator>
    {
        #region Private Properties
        private App_StateMachine _fsm;
        #endregion

        #region Subsystems
        [SerializeField]
        private InputManager _inputManager;
        [SerializeField]
        private ScenePerceptionHandler _scenePerceptionHandler;
        [SerializeField]
        private ObjectPool _objPool;
        [SerializeField]
        private UIMediator _UIManager;
        [SerializeField]
        private GameplayHandler _gameManager;

        private TaskUtilitiesManager _taskUtilitiesManager = new TaskUtilitiesManager();
        #endregion

        #region Overrides
        public override void InitSingleton()
        {
            base.InitSingleton();

            InitializeManagers();

            _fsm = new App_StateMachine();

            _fsm.GoToStateNonHistorically<AppState_AppCalibration>();
        }

        public override void CleanSingleton()
        {
            DeInitializeManagers();

            _fsm = null;

            base.CleanSingleton();
        }
        #endregion

        #region Public Methods
        public void StartAppWarmup()
        {
            if (!IsInstantiated)
                return;

            if (_fsm.CurrentStateType == typeof(AppState_InitMenu))
                _fsm.GoToState<AppState_Warmup>();
        }

        public void StartAppPhase1()
        {
            if (!IsInstantiated)
                return;

            if (_fsm.CurrentStateType == typeof(AppState_Warmup))
                _fsm.GoToState<AppState_AppPhase1>();
        }

        public void StartAppPhase2()
        {
            if (!IsInstantiated)
                return;

            if (_fsm.CurrentStateType == typeof(AppState_AppPhase1))
                _fsm.GoToState<AppState_AppPhase2>();
        }

        public void StartAppPhase3()
        {
            if (!IsInstantiated)
                return;

            if (_fsm.CurrentStateType == typeof(AppState_AppPhase2))
                _fsm.GoToState<AppState_AppPhase3>();
        }

        public void StartAppPhase4()
        {
            if (!IsInstantiated)
                return;

            if (_fsm.CurrentStateType == typeof(AppState_AppPhase3))
                _fsm.GoToState<AppState_AppPhase4>();
        }

        /// <summary>
        /// Function to be called before 
        /// transitioning to another 
        /// application variant or to 
        /// return to main menu
        /// </summary>
        public void GoToMenu()
        {
            if (!IsInstantiated)
                return;

            if (_fsm.CurrentStateType != typeof(AppState_InitMenu))
                _fsm.GoToState<AppState_InitMenu>();
        }

        public void QuitApp()
        {
            if (!IsInstantiated)
                return;

            _fsm.GoToStateNonHistorically<AppState_ApplicationQuit>();
        }
        #endregion

        #region Private Methods
        private void InitializeManagers()
        {
            _taskUtilitiesManager.InitSingleton();

            _inputManager.gameObject.SetActive(true);
            _inputManager.InitSingleton();
            _scenePerceptionHandler.gameObject.SetActive(true);
            _scenePerceptionHandler.InitSingleton();
            _objPool.gameObject.SetActive(true);
            _objPool.InitSingleton();
            _UIManager.gameObject.SetActive(true);
            _UIManager.InitSingleton();
            _gameManager.gameObject.SetActive(true);
            _gameManager.InitSingleton();
        }

        private void DeInitializeManagers()
        {
            _inputManager.CleanSingleton();
            _scenePerceptionHandler.CleanSingleton();
            _objPool.CleanSingleton();
            _UIManager.CleanSingleton();
            _gameManager.CleanSingleton();

            _taskUtilitiesManager.CleanSingleton();
        }
        #endregion
    }
}