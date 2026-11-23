using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using Lobby;
using UnityEngine;

namespace Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GrayBoxSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject playerPrefab;

        private NetworkManager manager;
        private LobbySession roster;
        private int nextSpawnSlot;

        private void OnEnable()
        {
            if (NetworkManager.Instances.Count == 0)
                return;

            var networkManager = NetworkManager.Instances[0];
            if (!networkManager.IsServerStarted)
                return;

            roster = networkManager.GetComponent<LobbySession>();
            if (roster == null)
                return;

            manager = networkManager;
            manager.SceneManager.OnClientPresenceChangeEnd += OnPresenceChanged;

            if (manager.SceneManager.SceneConnections.TryGetValue(gameObject.scene, out var presentConnections))
            {
                foreach (NetworkConnection connection in presentConnections)
                    SpawnPlayer(connection);
            }
        }

        private void OnDisable()
        {
            if (manager != null)
                manager.SceneManager.OnClientPresenceChangeEnd -= OnPresenceChanged;
            manager = null;
        }

        private void OnPresenceChanged(ClientPresenceChangeEventArgs args)
        {
            if (args.Added && args.Scene == gameObject.scene)
                SpawnPlayer(args.Connection);
        }

        private void SpawnPlayer(NetworkConnection connection)
        {
            if (!connection.IsActive || !connection.IsAuthenticated ||
                !roster.TryGetServerPlayer(connection.ClientId, out LobbyPlayer player) || player.IsSpectator)
                return;

            foreach (NetworkObject owned in connection.Objects)
            {
                if (owned != null && owned.gameObject.scene == gameObject.scene &&
                    owned.TryGetComponent<GrayBoxPlayer>(out _))
                    return;
            }

            int slot = nextSpawnSlot++ % 45;
            int column = slot % 9;
            int row = slot / 9;
            float x = ((column + 1) / 2) * (column % 2 == 0 ? -1.3f : 1.3f);
            float y = ((row + 1) / 2) * (row % 2 == 0 ? -1.3f : 1.3f);
            var box = Instantiate(playerPrefab, new Vector3(x, y, 0f), Quaternion.identity);
            manager.ServerManager.Spawn(box, connection, gameObject.scene);
        }
    }
}
