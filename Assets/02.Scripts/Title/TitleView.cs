using UnityEngine;
using UnityEngine.UI;

namespace Title
{
    public class TitleView : MonoBehaviour
    {
        [SerializeField] ServerJoinScreen _serverJoinScreen;
        
        [SerializeField] private InputField _serverIp;
        [SerializeField] private InputField _nicknameInput;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _hostButton;
        [SerializeField] private Text _status;
    
        
        private void Start()
        {
            _joinButton.onClick.RemoveAllListeners();
            _joinButton.onClick.AddListener(Connect);
            _hostButton.onClick.RemoveAllListeners();
            _hostButton.onClick.AddListener(StartHost);
            _serverJoinScreen.OnStatusChanged += StatusChanged;
        }

        void StatusChanged(string status) => _status.text = status;
        
        async void Connect()
        {
            SetInteractable(false);
            var result = await _serverJoinScreen.Connect(_serverIp.text,_nicknameInput.text);
            if(!result && this) SetInteractable(true);
        }

        async void StartHost()
        {
            SetInteractable(false);
            var result = await _serverJoinScreen.StartHost(_nicknameInput.text);
            if(!result && this) SetInteractable(true);
        }
        
        private void SetInteractable(bool ready)
        {
            _serverIp.interactable = ready;
            _nicknameInput.interactable = ready;
            _joinButton.interactable = ready;
            _hostButton.interactable = ready;
        }
    }
}
