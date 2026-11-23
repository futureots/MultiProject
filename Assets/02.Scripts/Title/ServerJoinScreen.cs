using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Transporting;
using Lobby;
using Lobby.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneLoadStartEventArgs = FishNet.Managing.Scened.SceneLoadStartEventArgs;

namespace Title
{
    [DisallowMultipleComponent]
    public sealed class ServerJoinScreen : MonoBehaviour
    {
        public const int MaxNicknameLength = 24;
        [SerializeField] private int lobbySceneIndex = 1;
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);

        private CancellationTokenSource _activeAttempt;
        
        internal event Action<string> OnStatusChanged;

        private static bool TryNormalizeNickname(string input, out string nickname)
        {
            nickname = input?.Trim() ?? string.Empty;
            return nickname.Length > 0 && nickname.Length <= MaxNicknameLength && !nickname.Any(char.IsControl);
        }
        
        public async Task<bool> Connect(string serverIp, string nicknameInput)
        {
            
            if (!isActiveAndEnabled || _activeAttempt != null)
                return false;

            string input = serverIp.Trim();
            if (!IPAddress.TryParse(input, out IPAddress address) ||
                (address.AddressFamily == AddressFamily.InterNetwork && address.ToString() != input))
            {
                OnStatusChanged?.Invoke("서버 IP 주소를 올바르게 입력해 주세요.");
                return false;
            }

            return await EnterLobbyAsync(nicknameInput,address.ToString(), asHost: false);
        }

        public async Task<bool> StartHost(string nicknameInput)
        {
            if (!isActiveAndEnabled || _activeAttempt != null)
                return false;

            return await EnterLobbyAsync(nicknameInput,IPAddress.Loopback.ToString(), asHost: true);
        }

        private void OnDisable()
        {
            _activeAttempt?.Cancel();
        }

        private async Task<bool> EnterLobbyAsync(string nicknameInput,string address, bool asHost)
        {
            if(!TryNormalizeNickname(nicknameInput,out var nickname))
            {
                OnStatusChanged?.Invoke("이름을 제대로 입력해 주세요.");
                return false;
            }
            
            
            var manager = InstanceFinder.NetworkManager;
            var lobbySession = manager != null ? manager.GetComponent<LobbySession>() : null;
            if (lobbySession == null)
            {
                OnStatusChanged?.Invoke("네트워크 설정을 확인해 주세요.");
                return false;
            }

            Transport transport = manager.TransportManager.Transport;
            if (transport.GetConnectionState(false) != LocalConnectionState.Stopped ||
                (asHost && transport.GetConnectionState(true) != LocalConnectionState.Stopped))
            {
                OnStatusChanged?.Invoke("기존 연결이 종료된 뒤 다시 시도해 주세요.");
                return false;
            }

            using var attempt = new CancellationTokenSource();
            using var timeout = new CancellationTokenSource(ConnectionTimeout);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token, timeout.Token);
            _activeAttempt = attempt;
            bool startingServer = false;
            bool startingClient = false;
            bool keepConnection = false;

            try
            {
                if (asHost)
                {
                    OnStatusChanged?.Invoke("호스트 서버를 시작하는 중…");
                    startingServer = true;
                    if (!await StartServerAsync(manager.ServerManager, cancellation.Token))
                    {
                        OnStatusChanged?.Invoke("호스트 서버를 시작할 수 없습니다. 서버 포트를 확인해 주세요.");
                        return false;
                    }
                }

                cancellation.Token.ThrowIfCancellationRequested();
                OnStatusChanged?.Invoke("서버에 접속하는 중…");
                startingClient = true;
                if (!await AuthenticateAsync(manager.ClientManager, address, cancellation.Token) ||
                    !manager.ClientManager.Connection.IsAuthenticated ||
                    (asHost && !manager.ServerManager.Started))
                {
                    OnStatusChanged?.Invoke(asHost ? "호스트 접속을 완료할 수 없습니다. 다시 시도해 주세요." : "서버에 연결할 수 없습니다. IP와 서버 상태를 확인해 주세요.");
                    return false;
                }

                cancellation.Token.ThrowIfCancellationRequested();
                OnStatusChanged?.Invoke("닉네임을 등록하는 중…");
                LobbyPlayer? player = await lobbySession.RegisterAsync(nickname, cancellation.Token);
                if (!player.HasValue)
                {
                    OnStatusChanged?.Invoke("닉네임을 등록할 수 없습니다. 다시 시도해 주세요.");
                    return false;
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (!manager.ClientManager.Connection.IsAuthenticated)
                {
                    OnStatusChanged?.Invoke("서버 연결이 종료되었습니다. 다시 시도해 주세요.");
                    return false;
                }

                if (player.Value.IsSpectator)
                {
                    OnStatusChanged?.Invoke("게임이 진행 중입니다. 관전자로 입장합니다…");
                    if (!await EnterAsSpectatorAsync(manager, () => keepConnection = true, cancellation.Token))
                    {
                        OnStatusChanged?.Invoke("관전 화면에 연결할 수 없습니다. 다시 시도해 주세요.");
                        return false;
                    }

                    return true;
                }

                OnStatusChanged?.Invoke("로비를 불러오는 중…");
                AsyncOperation load = SceneManager.LoadSceneAsync(lobbySceneIndex, LoadSceneMode.Single);
                if (load == null)
                {
                    OnStatusChanged?.Invoke("Lobby 씬을 불러올 수 없습니다.");
                    return false;
                }

                keepConnection = true;
                
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (this != null)
                    OnStatusChanged?.Invoke(attempt.IsCancellationRequested ? string.Empty : "서버 응답 시간이 초과되었습니다. 다시 시도해 주세요.");
            }
            catch (Exception exception)
            {
                if (this != null)
                    OnStatusChanged?.Invoke("접속을 완료할 수 없습니다. 다시 시도해 주세요.");
                Debug.LogException(exception);
            }
            finally
            {
                _activeAttempt = null;
                if (!keepConnection)
                {
                    if (manager != null)
                    {
                        if (startingClient)
                            manager.ClientManager.StopConnection();
                        if (startingServer)
                            manager.ServerManager.StopConnection(sendDisconnectMessage: true);
                    }
                }
            }
            return keepConnection;
        }

        private static async Task<bool> EnterAsSpectatorAsync(NetworkManager manager, Action onLoadStarting,
            CancellationToken cancellation)
        {
            ClientManager client = manager.ClientManager;
            var sceneManager = manager.SceneManager;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnLoadStart(SceneLoadStartEventArgs args)
            {
                if (args.QueueData.AsServer || args.QueueData.SceneLoadData.SceneLookupDatas
                    .All(scene => scene.FullNameOnly != LobbyMatchController.GameSceneName))
                    return;

                // MainTitle can be disabled before the await resumes.
                if (started.TrySetResult(true))
                    onLoadStarting();
            }

            void OnConnectionChanged(ClientConnectionStateArgs args)
            {
                if (args.ConnectionState == LocalConnectionState.Stopped)
                    started.TrySetResult(false);
            }

            sceneManager.OnLoadStart += OnLoadStart;
            client.OnClientConnectionState += OnConnectionChanged;
            try
            {
                using var registration = cancellation.Register(() => started.TrySetCanceled());
                cancellation.ThrowIfCancellationRequested();
                client.Broadcast(new LobbyClientReady { Ready = true });
                return await started.Task;
            }
            finally
            {
                if (sceneManager != null)
                    sceneManager.OnLoadStart -= OnLoadStart;
                if (client != null)
                    client.OnClientConnectionState -= OnConnectionChanged;
            }
        }

        private static async Task<bool> StartServerAsync(ServerManager server, CancellationToken cancellation)
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            // callback 설정, 서버 열기(성공여부를 callback으로 받기)
            server.OnServerConnectionState += OnStateChanged;
            try
            {
                using var registration = cancellation.Register(() => started.TrySetCanceled());
                cancellation.ThrowIfCancellationRequested();
                return server.StartConnection() && await started.Task;
            }
            finally
            {
                server.OnServerConnectionState -= OnStateChanged;
            }
            
            void OnStateChanged(ServerConnectionStateArgs args)
            {
                if (args.ConnectionState == LocalConnectionState.Started)
                    started.TrySetResult(true);
                else if (args.ConnectionState == LocalConnectionState.Stopped)
                    started.TrySetResult(false);
            }
        }

        private static async Task<bool> AuthenticateAsync(ClientManager client, string address, CancellationToken cancellation)
        {
            var authenticated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnAuthenticated() => authenticated.TrySetResult(true);
            void OnStateChanged(ClientConnectionStateArgs args)
            {
                if (args.ConnectionState == LocalConnectionState.Stopped)
                    authenticated.TrySetResult(false);
            }

            client.OnAuthenticated += OnAuthenticated;
            client.OnClientConnectionState += OnStateChanged;
            try
            {
                using var registration = cancellation.Register(() => authenticated.TrySetCanceled());
                cancellation.ThrowIfCancellationRequested();
                return client.StartConnection(address) && await authenticated.Task;
            }
            finally
            {
                client.OnAuthenticated -= OnAuthenticated;
                client.OnClientConnectionState -= OnStateChanged;
            }
        }
    }
}
