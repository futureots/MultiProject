using System;
using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UI;

namespace Lobby
{
    [DisallowMultipleComponent]
    public sealed class LobbyRosterView : MonoBehaviour
    {
        [SerializeField] private RectTransform rosterContent;
        [SerializeField] private Text playerRowPrefab;
        [SerializeField] private Text playerCount;
        [SerializeField] private Text emptyMessage;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Text startStatus;
        [SerializeField] private LobbyMatchController matchController;

        private readonly List<Text> rows = new();
        private LobbySession session;

        private void OnEnable()
        {
            if (NetworkManager.Instances.Count > 0)
                session = NetworkManager.Instances[0].GetComponent<LobbySession>();

            if (session != null)
                session.PlayersChanged += Refresh;
            matchController.StartAvailabilityChanged += RefreshStartControls;

            Refresh();
        }

        private void OnDisable()
        {
            if (session != null)
                session.PlayersChanged -= Refresh;
            matchController.StartAvailabilityChanged -= RefreshStartControls;
            session = null;
        }

        private void Refresh()
        {
            IReadOnlyList<LobbyPlayer> players = session != null ? session.Players : Array.Empty<LobbyPlayer>();
            playerCount.text = $"접속 중 {players.Count}명";
            emptyMessage.gameObject.SetActive(players.Count == 0);
            RefreshStartControls();

            for (int i = 0; i < players.Count; i++)
            {
                if (i == rows.Count)
                    rows.Add(Instantiate(playerRowPrefab, rosterContent));

                LobbyPlayer player = players[i];
                rows[i].text = $"ID {player.ClientId}     {player.Nickname}";
                rows[i].gameObject.SetActive(true);
            }

            for (int i = players.Count; i < rows.Count; i++)
                rows[i].gameObject.SetActive(false);
        }

        public void RequestGameStart()
        {
            if (matchController.CanStartGame && !matchController.TryStartGame())
                startStatus.text = "게임 화면을 불러올 수 없습니다.";
        }

        private void RefreshStartControls()
        {
            bool isHost = matchController.IsLocalHost;
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = isHost && matchController.CanStartGame;

            if (isHost)
            {
                if (matchController.GameStarted)
                    startStatus.text = "게임 화면으로 이동하는 중…";
                else if (session.Players.Count < LobbyMatchController.MinimumPlayers)
                    startStatus.text = $"게임 시작에는 {LobbyMatchController.MinimumPlayers}명 이상이 필요합니다.";
                else
                    startStatus.text = matchController.CanStartGame ? string.Empty : "플레이어가 로비에 들어오는 중…";
            }
            else
            {
                startStatus.text = session != null && session.Players.Count > 0
                    ? "호스트가 게임을 시작할 때까지 기다려 주세요."
                    : string.Empty;
            }
        }

    }
}
