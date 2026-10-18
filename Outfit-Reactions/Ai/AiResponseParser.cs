using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Turns the raw text returned by an AI provider into an AiComplimentResult. Handles strict
    /// JSON parsing, lenient/loose recovery when the model returns slightly malformed JSON, the
    /// local "- dialogue" dash-line style, markdown-fence stripping, smart-quote normalization,
    /// and balanced-object extraction. Pure text processing: no game/helper/monitor dependencies.
    /// </summary>
    internal static class AiResponseParser
    {
        public static AiComplimentResult ParseLocalDashLineStyleResult(string raw, CharacterAiProfile profile)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string text = StripMarkdownFences(raw).Trim();
            text = Regex.Replace(text, @"(?is)<think>.*?</think>", "").Trim();
            text = Regex.Replace(text, @"(?is)^\s*(assistant|resposta|response)\s*:\s*", "").Trim();

            string selected = null;
            bool collectingDialogueEntry = false;
            foreach (string rawLine in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("%", StringComparison.Ordinal))
                    break;

                if (!collectingDialogueEntry)
                {
                    // Older local prompts asked models to start with "-". New prompts do not,
                    // but accept and strip it for backwards compatibility so the dash never leaks
                    // into the in-game dialogue.
                    line = Regex.Replace(line, @"^\s*[-–—•]+\s*", "").Trim();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    selected = line;
                    collectingDialogueEntry = true;
                    continue;
                }

                // Stop before accidental extra bullet/dialogue choices. Plain continuation
                // lines are treated as extra Stardew dialogue boxes.
                if (line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("–", StringComparison.Ordinal) || line.StartsWith("—", StringComparison.Ordinal) || line.StartsWith("•", StringComparison.Ordinal))
                    break;

                selected += "#$b#" + line;
            }

            if (string.IsNullOrWhiteSpace(selected))
                return null;

            // If the model added answer suggestions anyway, keep only the NPC line.
            int percentIndex = selected.IndexOf('%');
            if (percentIndex >= 0)
                selected = selected.Substring(0, percentIndex).Trim();

            selected = Regex.Replace(selected, @"^['""“”]+|['""“”]+$", "").Trim();
            bool needsClarification = false;
            if (Regex.IsMatch(selected, @"^\s*\[\s*(?:CLARIFY|NEEDS[_\s-]*CLARIFICATION)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                needsClarification = true;
                selected = Regex.Replace(selected, @"^\s*\[\s*(?:CLARIFY|NEEDS[_\s-]*CLARIFICATION)\s*\]\s*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            }
            selected = Regex.Replace(selected, @"(^|#\$b#)\s*[-–—•]+\s*", "$1").Trim();
            selected = Regex.Replace(selected, @"\s{2,}", " ").Trim();

            if (string.IsNullOrWhiteSpace(selected))
                return null;

            return new AiComplimentResult
            {
                Text = selected,
                Portrait = "",
                NeedsClarification = needsClarification
            };
        }

        public static AiComplimentResult ParseAiResult(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            foreach (string candidate in BuildJsonCandidates(raw))
            {
                AiComplimentResult result = TryDeserializeCompliment(candidate);
                if (result != null)
                    return result;
            }

            // Some vision models return a nearly perfect JSON object but forget the final
            // quote/brace, e.g. {"text":"... visible detail ...}. Do not throw away the
            // good line in that case; recover the text field and still run normal validation.
            AiComplimentResult recovered = TryRecoverLooseCompliment(raw);
            if (recovered != null)
                return recovered;

            return null;
        }

        private static IEnumerable<string> BuildJsonCandidates(string raw)
        {
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            // Some models return a JSON string that itself contains the actual JSON.
            string unwrapped = TryUnwrapJsonString(text);
            if (!string.IsNullOrWhiteSpace(unwrapped) && !unwrapped.Equals(text, StringComparison.Ordinal))
                text = unwrapped.Trim();

            text = StripMarkdownFences(text).Trim();
            text = NormalizeSmartJsonQuotes(text).Trim();

            yield return text;

            string balanced = ExtractFirstBalancedJsonObject(text);
            if (!string.IsNullOrWhiteSpace(balanced) && !balanced.Equals(text, StringComparison.Ordinal))
                yield return balanced;

            // Last-resort regex for short provider wrappers like:
            // "Here is the JSON requested: { ... }"
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                string sliced = text.Substring(start, end - start + 1);
                if (!string.IsNullOrWhiteSpace(sliced) && !sliced.Equals(balanced, StringComparison.Ordinal))
                    yield return sliced;
            }
        }

        private static AiComplimentResult TryDeserializeCompliment(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
            try
            {
                AiComplimentResult result = JsonSerializer.Deserialize<AiComplimentResult>(json.Trim(), options);
                if (result != null && (!string.IsNullOrWhiteSpace(result.Text) || !string.IsNullOrWhiteSpace(result.Portrait)))
                    return result;
            }
            catch
            {
                // Continue to manual parsing below.
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json.Trim(), new JsonDocumentOptions { AllowTrailingCommas = true });
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                string text = GetFirstStringProperty(doc.RootElement, "text", "dialogue", "dialogo", "diálogo", "fala", "line", "response", "compliment", "elogio", "texto");
                string portrait = GetFirstStringProperty(doc.RootElement, "portrait", "portraitKey", "expression", "expressao", "expressão", "emotion", "retrato");
                List<string> portraits = GetFirstStringArrayProperty(doc.RootElement, "portraits", "portraitKeys", "expressions", "expressoes", "expressões", "retratos");
                bool needsClarification = GetFirstBoolProperty(doc.RootElement, "needsClarification", "needs_clarification", "clarificationNeeded", "clarification", "needClarification");
                string themeAnchor = GetFirstStringProperty(doc.RootElement, "themeAnchor", "theme_anchor", "thematicAnchor", "themeExcerpt");
                if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(portrait) && (portraits == null || portraits.Count == 0))
                    return null;

                return new AiComplimentResult { Text = text, Portrait = portrait, Portraits = portraits ?? new List<string>(), NeedsClarification = needsClarification, ThemeAnchor = themeAnchor };
            }
            catch
            {
                return null;
            }
        }

        private static AiComplimentResult TryRecoverLooseCompliment(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            List<string> candidates = new();

            string text = StripMarkdownFences(raw.Trim());
            text = NormalizeSmartJsonQuotes(text).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                candidates.Add(text);

            string unwrapped = TryUnwrapJsonString(text);
            if (!string.IsNullOrWhiteSpace(unwrapped) && !unwrapped.Equals(text, StringComparison.Ordinal))
                candidates.Add(NormalizeSmartJsonQuotes(StripMarkdownFences(unwrapped.Trim())).Trim());

            foreach (string candidate in candidates)
            {
                string recoveredText = TryExtractLooseStringProperty(candidate, "text", "dialogue", "dialogo", "diálogo", "fala", "line", "response", "compliment", "elogio", "texto");
                string portrait = TryExtractLooseStringProperty(candidate, "portrait", "portraitKey", "expression", "expressao", "expressão", "emotion", "retrato");
                bool needsClarification = TryExtractLooseBoolProperty(candidate, "needsClarification", "needs_clarification", "clarificationNeeded", "clarification", "needClarification");
                string themeAnchor = TryExtractLooseStringProperty(candidate, "themeAnchor", "theme_anchor", "thematicAnchor", "themeExcerpt");

                if (!string.IsNullOrWhiteSpace(recoveredText) || !string.IsNullOrWhiteSpace(portrait))
                    return new AiComplimentResult
                    {
                        Text = recoveredText,
                        Portrait = portrait,
                        Portraits = new List<string>(),
                        NeedsClarification = needsClarification,
                        ThemeAnchor = themeAnchor
                    };
            }

            return null;
        }

        private static string TryExtractLooseStringProperty(string source, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(source) || names == null)
                return null;

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Accept both strict JSON keys ("text": "...") and sloppy keys (text: "...").
                string pattern = "(?is)(?:\"" + Regex.Escape(name) + "\"|" + Regex.Escape(name) + ")\\s*:\\s*\"";
                Match match = Regex.Match(source, pattern);
                if (!match.Success)
                    continue;

                int index = match.Index + match.Length;
                StringBuilder value = new();

                while (index < source.Length)
                {
                    bool escaped = false;
                    bool closedQuote = false;

                    for (; index < source.Length; index++)
                    {
                        char c = source[index];

                        if (escaped)
                        {
                            value.Append(c switch
                            {
                                'n' => '\n',
                                'r' => '\r',
                                't' => '\t',
                                '"' => '"',
                                '\\' => '\\',
                                _ => c
                            });
                            escaped = false;
                            continue;
                        }

                        if (c == '\\')
                        {
                            escaped = true;
                            continue;
                        }

                        if (c == '"')
                        {
                            closedQuote = true;
                            index++;
                            break;
                        }

                        value.Append(c);
                    }

                    if (!closedQuote)
                        break;

                    // Some models produce malformed JSON for Stardew dialogue boxes like:
                    // {"text":"first box"#$b#"second box","portrait":"h"}
                    // That is not valid JSON, but it is still a complete in-game dialogue.
                    // Preserve every quoted chunk connected by a #$b# / $b# token instead of
                    // keeping only the first box.
                    int afterQuote = index;
                    while (afterQuote < source.Length && char.IsWhiteSpace(source[afterQuote]))
                        afterQuote++;

                    Match breakMatch = Regex.Match(source.Substring(afterQuote), @"^(?:#\s*\$\s*b\s*#|\$\s*b\s*#)\s*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (!breakMatch.Success)
                        break;

                    value.Append("#$b#");
                    index = afterQuote + breakMatch.Length;
                }

                string cleaned = value.ToString().Trim();

                // If the model forgot the closing quote/brace, the captured text may include
                // trailing JSON-ish debris. Trim only structural leftovers, not normal dialogue.
                cleaned = Regex.Replace(cleaned, @"\s*[,}]+\s*$", "").Trim();
                cleaned = Regex.Replace(cleaned, @"\s*```\s*$", "").Trim();

                // Detect truncated responses: if the text ends mid-word or mid-sentence
                // (no sentence-ending punctuation and no dialogue break), it was cut off.
                // Discard these to avoid showing half-sentences like "H-hey c-caramba. Você".
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    string lastSegment = cleaned.Contains("#$b#")
                        ? cleaned.Substring(cleaned.LastIndexOf("#$b#", StringComparison.Ordinal) + 4).Trim()
                        : cleaned;
                    bool endsNaturally = lastSegment.EndsWith(".") || lastSegment.EndsWith("!") ||
                                        lastSegment.EndsWith("?") || lastSegment.EndsWith("...") ||
                                        lastSegment.EndsWith("~") || lastSegment.EndsWith("♪") ||
                                        lastSegment.EndsWith("*");
                    if (!endsNaturally)
                        return null; // Truncated — discard and let retry handle it.
                    return cleaned;
                }
            }

            return null;
        }

        private static bool TryExtractLooseBoolProperty(string source, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(source) || names == null)
                return false;

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string pattern = "(?is)(?:\"" + Regex.Escape(name) + "\"|" + Regex.Escape(name) + ")\\s*:\\s*(true|false|\"true\"|\"false\"|\"yes\"|\"no\"|\"sim\"|\"não\"|\"nao\")";
                Match match = Regex.Match(source, pattern);
                if (!match.Success)
                    continue;

                string value = match.Groups[1].Value.Trim().Trim('"');
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("sim", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("não", StringComparison.OrdinalIgnoreCase) || value.Equals("nao", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return false;
        }

        private static string GetFirstStringProperty(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!element.TryGetProperty(name, out JsonElement value))
                    continue;

                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString();

                if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                    return value.ToString();
            }

            return null;
        }

        private static List<string> GetFirstStringArrayProperty(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!element.TryGetProperty(name, out JsonElement value))
                    continue;

                List<string> result = new();

                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            string text = item.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                                result.Add(text.Trim());
                        }
                        else if (item.ValueKind == JsonValueKind.Number || item.ValueKind == JsonValueKind.True || item.ValueKind == JsonValueKind.False)
                        {
                            string text = item.ToString();
                            if (!string.IsNullOrWhiteSpace(text))
                                result.Add(text.Trim());
                        }
                    }

                    return result;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    string raw = value.GetString();
                    if (string.IsNullOrWhiteSpace(raw))
                        return result;

                    foreach (string part in raw.Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string text = part.Trim().Trim('[', ']', '\'', '"');
                        if (!string.IsNullOrWhiteSpace(text))
                            result.Add(text);
                    }

                    return result;
                }
            }

            return new List<string>();
        }

        private static bool GetFirstBoolProperty(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!element.TryGetProperty(name, out JsonElement value))
                    continue;

                if (value.ValueKind == JsonValueKind.True)
                    return true;

                if (value.ValueKind == JsonValueKind.False)
                    return false;

                if (value.ValueKind == JsonValueKind.String)
                {
                    string text = value.GetString();
                    if (bool.TryParse(text, out bool parsed))
                        return parsed;

                    if (string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "sim", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static string StripMarkdownFences(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Trim();
            text = Regex.Replace(text, @"^```(?:json|JSON)?\s*", "", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\s*```$", "", RegexOptions.IgnoreCase).Trim();
            return text;
        }

        private static string TryUnwrapJsonString(string text)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                    return doc.RootElement.GetString();
            }
            catch
            {
                // Not a JSON string wrapper.
            }

            return text;
        }

        private static string NormalizeSmartJsonQuotes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Only helps with models that use smart quotes around JSON keys/values.
            // Normal dialogue quotes are removed later by CleanDialogueText.
            return text.Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('‟', '"');
        }

        private static string ExtractFirstBalancedJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            bool inString = false;
            bool escaped = false;
            int depth = 0;
            int start = -1;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '{')
                {
                    if (depth == 0)
                        start = i;
                    depth++;
                }
                else if (c == '}' && depth > 0)
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                        return text.Substring(start, i - start + 1);
                }
            }

            return null;
        }
    }
}
