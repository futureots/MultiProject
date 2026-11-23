using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Transporting;
using Lobby.Protocol;
using UnityEngine;

namespace Lobby
{
    public struct LobbyPlayer
    {
        public int ClientId;
        public string Nickname;
        public bool IsSpectator;
    }
    
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class LobbySession : MonoBehaviour
    {
        public IReadOnlyList<LobbyPlayer> Players { get; private set; } = Array.Empty<LobbyPlayer>();
        public event Action PlayersChanged;
        internal event Action<int> PlayerRegisteredOnServer;
        internal event Action<int> PlayerLeftOnServer;
        internal int RegisteredPlayerCount => serverPlayers.Count;

        private readonly Dictionary<int, LobbyPlayer> serverPlayers = new();
        private ClientManager _client;
        private ServerManager _server;

        private void Start()
        {
            var networkManager = GetComponent<NetworkManager>();
            _client = networkManager.ClientManager;
            _server = networkManager.ServerManager;
            _client.RegisterBroadcast<LobbyRosterSnapshot>(OnRosterReceived);
            _client.OnClientConnectionState += OnClientStateChanged;
            _server.RegisterBroadcast<RegisterLobbyNickname>(OnNicknameReceived);
            _server.OnRemoteConnectionState += OnRemoteStateChanged;
            _server.OnServerConnectionState += OnServerStateChanged;
        }

        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.UnregisterBroadcast<LobbyRosterSnapshot>(OnRosterReceived);
                _client.OnClientConnectionState -= OnClientStateChanged;
            }

            if (_server != null)
            {
                _server.UnregisterBroadcast<RegisterLobbyNickname>(OnNicknameReceived);
                _server.OnRemoteConnectionState -= OnRemoteStateChanged;
                _server.OnServerConnectionState -= OnServerStateChanged;
            }
        }


        public async Task<LobbyPlayer?> RegisterAsync(string nickname, CancellationToken cancellation)
        {
            if (_client == null || !_client.Connection.IsAuthenticated)
                return null;

            int clientId = _client.Connection.ClientId;
            var registered = new TaskCompletionSource<LobbyPlayer?>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnRosterChanged()
            {
                foreach (LobbyPlayer player in Players)
                {
                    if (player.ClientId == clientId && player.Nickname == nickname)
                    {
                        registered.TrySetResult(player);
                        break;
                    }
                }
            }

            void OnConnectionChanged(ClientConnectionStateArgs args)
            {
                if (args.ConnectionState == LocalConnectionState.Stopped)
                    registered.TrySetResult(null);
            }

            PlayersChanged += OnRosterChanged;
            _client.OnClientConnectionState += OnConnectionChanged;
            try
            {
                using var registration = cancellation.Register(() => registered.TrySetCanceled());
                cancellation.ThrowIfCancellationRequested();
                _client.Broadcast(new RegisterLobbyNickname { Nickname = nickname });
                return await registered.Task;
            }
            finally
            {
                PlayersChanged -= OnRosterChanged;
                if (_client != null)
                    _client.OnClientConnectionState -= OnConnectionChanged;
            }
        }

        internal bool TryGetServerPlayer(int clientId, out LobbyPlayer player)
        {
            return serverPlayers.TryGetValue(clientId, out player);
        }

        internal void SetSpectator(int clientId)
        {
            if (!serverPlayers.TryGetValue(clientId, out LobbyPlayer player))
                return;

            player.IsSpectator = true;
            serverPlayers[clientId] = player;
        }

        private void OnNicknameReceived(NetworkConnection connection, RegisterLobbyNickname message, Channel channel)
        {
            var nickname = message.Nickname ?? "Anonymous";

            if (!serverPlayers.ContainsKey(connection.ClientId))
            {
                serverPlayers.Add(connection.ClientId, new LobbyPlayer
                {
                    ClientId = connection.ClientId,
                    Nickname = nickname
                });
                PlayerRegisteredOnServer?.Invoke(connection.ClientId);
            }

            SendRoster();
        }

        private void OnRemoteStateChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            if (serverPlayers.Remove(connection.ClientId) && _server.Started)
            {
                PlayerLeftOnServer?.Invoke(connection.ClientId);
                SendRoster();
            }
        }

        private void OnServerStateChanged(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;

            serverPlayers.Clear();
        }

        private void SendRoster()
        {
            var players = serverPlayers.Values.OrderBy(player => player.ClientId).ToArray();
            _server.Broadcast(new LobbyRosterSnapshot { Players = players });
        }

        private void OnRosterReceived(LobbyRosterSnapshot message, Channel channel)
        {
            Players = message.Players ?? Array.Empty<LobbyPlayer>();
            PlayersChanged?.Invoke();
        }

        private void OnClientStateChanged(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;

            Players = Array.Empty<LobbyPlayer>();
            PlayersChanged?.Invoke();
        }
    }
}
