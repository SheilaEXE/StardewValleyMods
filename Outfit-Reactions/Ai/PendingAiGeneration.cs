using System;
using System.Threading;
using System.Threading.Tasks;

namespace OutfitReactions.Ai
{
    internal enum AiGenerationLifecycleState
    {
        Waiting,
        Completed,
        TimedOut
    }

    internal interface IAiPendingGeneration
    {
        Task<string> Task { get; }
        int SafetyTimer { get; set; }
    }

    /// <summary>
    /// State for one in-flight outfit reaction request. It deliberately contains no Stardew
    /// objects, so the game-facing dialogue code can remain responsible for UI and NPC state.
    /// </summary>
    internal sealed class PendingAiGeneration : IAiPendingGeneration
    {
        public string NpcName { get; set; } = "";
        public bool IsSpouseDialogue { get; set; }
        public bool ClearExistingDialogue { get; set; }
        public Task<string> Task { get; set; }
        public CancellationTokenSource Cancellation { get; set; }
        public bool CompletionHandled { get; set; }
        public int WaitingDotCount { get; set; } = 1;
        public int WaitingDotTimer { get; set; } = 30;
        public int SafetyTimer { get; set; } = 7200;
    }

    /// <summary>State for one in-flight follow-up request after the player replies to an NPC.</summary>
    internal sealed class PendingAiPlayerReplyGeneration : IAiPendingGeneration
    {
        public string NpcName { get; set; } = "";
        public bool IsSpouseDialogue { get; set; }
        public string NpcCompliment { get; set; } = "";
        public string PlayerReply { get; set; } = "";
        public Task<string> Task { get; set; }
        public CancellationTokenSource Cancellation { get; set; }
        public bool CompletionHandled { get; set; }
        public int WaitingDotCount { get; set; } = 1;
        public int WaitingDotTimer { get; set; } = 30;
        public int SafetyTimer { get; set; } = 7200;
        public Action OnFinished { get; set; }
    }

    /// <summary>Owns the cancellation-token lifecycle shared by AI request types.</summary>
    internal static class AiRequestLifecycle
    {
        public static void Cancel(CancellationTokenSource cancellation)
        {
            if (cancellation == null)
                return;

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request already completed and disposed its token source.
            }
        }

        public static void DisposeWhenFinished(Task task, CancellationTokenSource cancellation)
        {
            if (task == null || cancellation == null)
                return;

            _ = task.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        }
    }

    /// <summary>Shared, game-agnostic state transitions for dialogue-generating AI requests.</summary>
    internal static class AiDialogueLifecycle
    {
        public static AiGenerationLifecycleState Advance(IAiPendingGeneration pending)
        {
            if (pending?.Task?.IsCompleted == true)
                return AiGenerationLifecycleState.Completed;

            if (pending != null && pending.SafetyTimer > 0)
            {
                pending.SafetyTimer--;
                return AiGenerationLifecycleState.Waiting;
            }

            return AiGenerationLifecycleState.TimedOut;
        }
    }
}
