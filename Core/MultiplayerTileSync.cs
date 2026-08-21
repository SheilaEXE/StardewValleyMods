using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace TileMarker
{
    public partial class ModEntry
    {
        private const string TileSnapshotRequestMessage = "TileSnapshotRequest/v1";
        private const string TileSnapshotMessage = "TileSnapshot/v1";

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsWorldReady || !e.Peer.HasSmapi)
                return;

            if (Context.IsMainPlayer)
                SendTileSnapshot(e.Peer.PlayerID);
            else if (e.Peer.IsHost)
                RequestTileSnapshotFromHost();
        }

        private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (!string.Equals(e.FromModID, ModManifest.UniqueID, StringComparison.Ordinal))
                return;

            if (e.Type == TileSnapshotRequestMessage)
            {
                if (Context.IsMainPlayer && Context.IsWorldReady)
                    SendTileSnapshot(e.FromPlayerID);
                return;
            }

            if (e.Type != TileSnapshotMessage || Context.IsMainPlayer || !Context.IsWorldReady)
                return;

            IMultiplayerPeer senderPeer = Helper.Multiplayer.GetConnectedPlayer(e.FromPlayerID);
            if (senderPeer?.IsHost != true)
            {
                Monitor.Log("[Tile Marker] Ignored a tile snapshot that wasn't sent by the host.", LogLevel.Warn);
                return;
            }

            try
            {
                TileSnapshotPayload payload = e.ReadAs<TileSnapshotPayload>();
                store.ReplaceWithHostSnapshot(payload?.Data);
                Monitor.Log("[Tile Marker] Received the current tile selections from the multiplayer host.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[Tile Marker] Could not read the tile snapshot sent by the host: {ex}", LogLevel.Warn);
            }
        }

        private void OnTileMarksChangedForMultiplayer(object sender, Api.TileMarksChangedEventArgs e)
        {
            if (Context.IsMainPlayer && Context.IsWorldReady)
                SendTileSnapshot();
        }

        private void RequestTileSnapshotFromHost()
        {
            if (Context.IsMainPlayer || !Context.IsWorldReady)
                return;

            Helper.Multiplayer.SendMessage(
                new TileSnapshotRequestPayload(),
                TileSnapshotRequestMessage,
                new[] { ModManifest.UniqueID }
            );
        }

        private void SendTileSnapshot(long? playerId = null)
        {
            if (!Context.IsMainPlayer || !Context.IsWorldReady)
                return;

            long[] playerIds = playerId.HasValue ? new[] { playerId.Value } : null;
            Helper.Multiplayer.SendMessage(
                new TileSnapshotPayload { Data = store.CreateSnapshot() },
                TileSnapshotMessage,
                new[] { ModManifest.UniqueID },
                playerIds
            );
        }

        private sealed class TileSnapshotRequestPayload
        {
        }

        private sealed class TileSnapshotPayload
        {
            public Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> Data { get; set; } = new();
        }
    }
}
