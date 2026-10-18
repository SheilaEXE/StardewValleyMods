namespace OutfitReactions
{
    /// <summary>
    /// Small shared string helpers used across the mod. Previously several classes each had their
    /// own private copy of <see cref="FirstNonEmpty"/>; this is the single canonical version.
    /// </summary>
    internal static class StringUtils
    {
        /// <summary>
        /// Returns the first value that is not null/empty/whitespace, trimmed. Returns an empty
        /// string when every value is null/empty/whitespace (never returns null), so callers can
        /// safely concatenate the result. Use <c>FirstNonEmpty(...) is { Length: &gt; 0 }</c> or a
        /// plain emptiness check when you need to know whether anything matched.
        /// </summary>
        public static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
