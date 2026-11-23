using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using Lobby.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lobby
{
    [DisallowMultipleComponent]
    public sealed class LobbyMatchController : MonoBehaviour
    {
        public const int MinimumPlayers = 1;
        public const string GameScenePath = "Assets/01.Scenes/Game.unity";
        internal const string GameSceneName = "Game";

        public event Action StartAvailabilityChanged;

        public bool IsLocalHost => manager != null && manager.IsHostStarted &&
                                  manager.ClientManager.Connection.IsAuthenticated;
        public bool GameStarted { get; private set; }
        public bool CanStartGame => hostListening && IsLocalHost && !GameStarted &&
                                    roster.RegisteredPlayerCount >= MinimumPlayers &&
                                    readyPlayers.Count == roster.RegisteredPlayerCount;

        private readonly HashSet<int> readyPlayers = new();
        private NetworkManager manager;
        private LobbySession roster;
        private bool clientListening;
        private bool hostListening;

        private void Awake()
        {
            if (NetworkManager.Instances.Count == 0)
                return;

            manager = NetworkManager.Instances[0];
            roster = manager.GetComponent<LobbySession>();
        }

        private void Start()
        {
            if (roster == null || !manager.ClientManager.Connection.IsAuthenticated)
                return;

            manager.ClientManager.RegisterBroadcast<LobbyReadyRequest>(OnReadyRequested);
            clientListening = true;

            if (IsLocalHost)
            {
                manager.ServerManager.RegisterBroadcast<LobbyClientReady>(OnClientReady);
                manager.ServerManager.OnServerConnectionState += OnServerStateChanged;
                roster.PlayerRegisteredOnServer += OnPlayerRegistered;
                roster.PlayerLeftOnServer += OnPlayerLeft;
                hostListening = true;

                // A client can reach the lobby before the host starts listening.
                manager.ServerManager.Broadcast(new LobbyReadyRequest { Requested = true });
            }

            SendReady();
            StartAvailabilityChanged?.Invoke();
        }

        private void OnDestroy()
        {
            if (manager == null)
                return;

            if (clientListening)
                manager.ClientManager.UnregisterBroadcast<LobbyReadyRequest>(OnReadyRequested);

            if (hostListening)
            {
                manager.ServerManager.UnregisterBroadcast<LobbyClientReady>(OnClientReady);
                manager.ServerManager.OnServerConnectionState -= OnServerStateChanged;
                roster.PlayerRegisteredOnServer -= OnPlayerRegistered;
                roster.PlayerLeftOnServer -= OnPlayerLeft;
            }
        }

        public bool TryStartGame()
        {
            if (!CanStartGame || SceneUtility.GetBuildIndexByScenePath(GameScenePath) < 0)
                return false;

            var connections = new List<NetworkConnection>(roster.RegisteredPlayerCount);
            foreach (int id in readyPlayers)
            {
                if (!manager.ServerManager.Clients.TryGetValue(id, out NetworkConnection connection) ||
                    !connection.IsActive || !connection.IsAuthenticated)
                    return false;

                connections.Add(connection);
            }

            GameStarted = true;
            DontDestroyOnLoad(gameObject);
            StartAvailabilityChanged?.Invoke();
            manager.SceneManager.LoadConnectionScenes(connections.ToArray(), CreateGameLoad());
            return true;
        }

        private void OnReadyRequested(LobbyReadyRequest message, Channel channel)
        {
            if (message.Requested && !GameStarted)
                SendReady();
        }

        private void SendReady()
        {
            if (manager.ClientManager.Connection.IsAuthenticated)
                manager.ClientManager.Broadcast(new LobbyClientReady { Ready = true });
        }

        private void OnClientReady(NetworkConnection connection, LobbyClientReady message, Channel channel)
        {
            if (!message.Ready || !roster.TryGetServerPlayer(connection.ClientId, out LobbyPlayer player) ||
                !readyPlayers.Add(connection.ClientId))
                return;

            if (player.IsSpectator)
                manager.SceneManager.LoadConnectionScenes(connection, CreateGameLoad());
            else
                StartAvailabilityChanged?.Invoke();
        }

        private void OnPlayerRegistered(int clientId)
        {
            if (GameStarted)
                roster.SetSpectator(clientId);
            else
                StartAvailabilityChanged?.Invoke();
        }

        private void OnPlayerLeft(int clientId)
        {
            readyPlayers.Remove(clientId);
            StartAvailabilityChanged?.Invoke();
        }

        private void OnServerStateChanged(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;

            readyPlayers.Clear();
            StartAvailabilityChanged?.Invoke();

            if (GameStarted)
                Destroy(gameObject);
        }

        private static SceneLoadData CreateGameLoad()
        {
            var load = new SceneLoadData(GameSceneName)
            {
                ReplaceScenes = ReplaceOption.All,
                PreferredActiveScene = new PreferredScene(new SceneLookupData(GameSceneName))
            };
            load.Options.AutomaticallyUnload = false;
            return load;
        }
    }
}
