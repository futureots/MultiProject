using FishNet.Broadcast;

namespace Lobby.Protocol
{
    public struct RegisterLobbyNickname : IBroadcast
    {
        public string Nickname;
    }

    public struct LobbyRosterSnapshot : IBroadcast
    {
        public LobbyPlayer[] Players;
    }

    public struct LobbyClientReady : IBroadcast
    {
        public bool Ready;
    }

    public struct LobbyReadyRequest : IBroadcast
    {
        public bool Requested;
    }
}
