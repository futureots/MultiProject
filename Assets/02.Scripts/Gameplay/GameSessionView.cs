using FishNet.Managing;
using Lobby;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameSessionView : MonoBehaviour
    {
        [SerializeField] private Text roleText;

        private NetworkManager networkManager;
        private LobbySession session;

        private void OnEnable()
        {
            if (NetworkManager.Instances.Count > 0)
                networkManager = NetworkManager.Instances[0];

            if (networkManager != null)
                session = networkManager.GetComponent<LobbySession>();

            if (session != null)
                session.PlayersChanged += Refresh;

            Refresh();
        }

        private void OnDisable()
        {
            if (session != null)
                session.PlayersChanged -= Refresh;

            session = null;
            networkManager = null;
        }

        private void Refresh()
        {
            if (session != null && networkManager.ClientManager.Connection.IsAuthenticated)
            {
                int clientId = networkManager.ClientManager.Connection.ClientId;
                foreach (LobbyPlayer player in session.Players)
                {
                    if (player.ClientId != clientId)
                        continue;

                    roleText.text = player.IsSpectator
                        ? "진행 중인 게임을 관전하고 있습니다."
                        : "플레이어로 참가 중입니다.";
                    return;
                }
            }

            roleText.text = "서버 연결을 확인해 주세요.";
        }
    }
}
