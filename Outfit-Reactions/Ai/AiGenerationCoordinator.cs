using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Owns the in-flight AI request queues. It deliberately knows nothing about Stardew objects
    /// or dialogue: callers supply the work and decide how a completed result affects the game.
    /// </summary>
    internal sealed class AiGenerationCoordinator
    {
        private readonly Dictionary<string, PendingAiGeneration> outfitGenerations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingAiPlayerReplyGeneration> replyGenerations = new(StringComparer.OrdinalIgnoreCase);

        public bool HasOutfitGenerations => outfitGenerations.Count > 0;
        public bool HasReplyGenerations => replyGenerations.Count > 0;

        public bool TryGetOutfit(string npcName, out PendingAiGeneration pending)
        {
            return outfitGenerations.TryGetValue(npcName, out pending);
        }

        public bool TryGetReply(string npcName, out PendingAiPlayerReplyGeneration pending)
        {
            return replyGenerations.TryGetValue(npcName, out pending);
        }

        public IReadOnlyList<string> GetOutfitNpcNames() => outfitGenerations.Keys.ToList();
        public IReadOnlyList<string> GetReplyNpcNames() => replyGenerations.Keys.ToList();
        public IReadOnlyList<PendingAiGeneration> GetOutfitSnapshot() => outfitGenerations.Values.ToList();
        public IReadOnlyList<PendingAiPlayerReplyGeneration> GetReplySnapshot() => replyGenerations.Values.ToList();

        public void StartOutfit(PendingAiGeneration pending, Func<CancellationToken, string> generate)
        {
            Start(pending, generate);
            outfitGenerations[pending.NpcName] = pending;
        }

        public void StartReply(PendingAiPlayerReplyGeneration pending, Func<CancellationToken, string> generate)
        {
            Start(pending, generate);
            replyGenerations[pending.NpcName] = pending;
        }

        public void RemoveOutfit(string npcName) => outfitGenerations.Remove(npcName);
        public void RemoveReply(string npcName) => replyGenerations.Remove(npcName);

        /// <summary>Cancel and remove all active requests, returning reply requests for UI cleanup.</summary>
        public IReadOnlyList<PendingAiPlayerReplyGeneration> CancelAll()
        {
            List<PendingAiPlayerReplyGeneration> replies = replyGenerations.Values.ToList();

            foreach (PendingAiGeneration pending in outfitGenerations.Values)
                AiRequestLifecycle.Cancel(pending?.Cancellation);
            foreach (PendingAiPlayerReplyGeneration pending in replies)
                AiRequestLifecycle.Cancel(pending?.Cancellation);

            outfitGenerations.Clear();
            replyGenerations.Clear();
            return replies;
        }

        private static void Start(PendingAiGeneration pending, Func<CancellationToken, string> generate)
        {
            pending.Task = Task.Run(() => generate(pending.Cancellation.Token));
            AiRequestLifecycle.DisposeWhenFinished(pending.Task, pending.Cancellation);
        }

        private static void Start(PendingAiPlayerReplyGeneration pending, Func<CancellationToken, string> generate)
        {
            pending.Task = Task.Run(() => generate(pending.Cancellation.Token));
            AiRequestLifecycle.DisposeWhenFinished(pending.Task, pending.Cancellation);
        }
    }
}
