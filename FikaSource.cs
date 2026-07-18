using Fika.Core.Main.Components;

namespace BotCensus
{
    // Kept in its own type so the Fika.Core types are only JIT-resolved when Fika is actually
    // installed (the caller guards this behind a Chainloader plugin check).
    internal static class FikaSource
    {
        public static bool TryClassify(BotCensusPlugin plugin)
        {
            if (!CoopHandler.TryGetCoopHandler(out var handler) || handler == null || handler.Players == null)
                return false;

            foreach (var player in handler.Players.Values)
            {
                if (player == null) continue;
                plugin.Classify(player, player.IsObservedAI);
            }

            return true;
        }
    }
}
