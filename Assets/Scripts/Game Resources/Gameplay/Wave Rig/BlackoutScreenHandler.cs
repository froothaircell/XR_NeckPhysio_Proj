using CoreResources.Singleton;
using DG.Tweening;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameResources.Gameplay.VRController
{
    public class BlackoutScreenHandler : DestroyableMonoSingleton<BlackoutScreenHandler>
    {
        [SerializeField]
        private Image _blackScreen;
        [SerializeField]
        private TextMeshProUGUI _blackScreenText;
        [SerializeField]
        private float _screenTransitionDelay = 0.15f;
        [SerializeField]
        private float _textTransitionDelay = 0.05f;

        private Color _defaultImageColor;
        private Color _transparentImageColor;
        private Color _defaultTextColor;
        private Color _transparentTextColor;
        private CanvasGroup _canvasGroup;

        private const string LOADING_TEXT = "Loading...";

        #region Overrides
        public override void OnInit()
        {
            _defaultImageColor = _transparentImageColor = Color.black;
            _defaultImageColor.a = 1;
            _transparentImageColor.a = 0;

            _defaultTextColor = _transparentTextColor = Color.white;
            _defaultTextColor.a = 1;
            _transparentTextColor.a = 0;

            _blackScreen.color = _defaultImageColor;
            _blackScreenText.color = _defaultTextColor;

            SetBlackoutScreen(true, "Please center the head position and press the grip button.");

            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

        }

        public override void OnDeInit()
        {
        }
        #endregion

        #region Public Methods
        public void SetBlackoutScreen(bool status, string screenText = "")
        {
            if (status)
            {
                _blackScreenText.text = screenText;
                _blackScreen.DOColor(_defaultImageColor, _screenTransitionDelay).OnComplete(() =>
                {
                    _blackScreenText.DOColor(_defaultTextColor, _textTransitionDelay).OnComplete(() =>
                    {
                        _canvasGroup.blocksRaycasts = false;
                        _canvasGroup.interactable = false;
                    });
                });
            }
            else
            {
                _blackScreen.DOColor(_transparentImageColor, _screenTransitionDelay).OnComplete(() => 
                {
                    _blackScreenText.DOColor(_transparentTextColor, _textTransitionDelay).OnComplete(() =>
                    {
                        _canvasGroup.blocksRaycasts = false;
                        _canvasGroup.interactable = false;
                        _blackScreenText.text = screenText;
                    });
                });
            }
        }

        public void SetScale(float scale)
        {
            transform.localPosition *= scale;
            _blackScreenText.rectTransform.localScale *= scale;
        }
        #endregion
    }
}