using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Validates and cleans AI-generated dialogue before it reaches the player. The orchestrator
    /// ValidateGeneratedDialogueText runs the individual checks (pacing, theme specificity,
    /// forbidden literals, technical-label leaks, wrong language, copied-format/instruction leaks,
    /// gender terms, private-reveal fluster cue) and returns an issue string (or null if clean).
    /// Also hosts the dialogue text cleaners. This is pure text processing and needs no game state.
    /// </summary>
    internal static class DialogueValidator
    {
        public static string ValidateGeneratedDialogueText(string text, OutfitAiContext context, ModConfig config, ActiveAiSettings ai)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "empty dialogue text";

            // A #$b# break is useful for longer Stardew dialogue, but short natural one-box
            // reactions are allowed. Only require a break when the configured/actual length
            // is high enough that one box would feel cramped.

            string pacingIssue = ValidateDialogueBoxPacing(text, config, ai);
            if (!string.IsNullOrWhiteSpace(pacingIssue))
                return pacingIssue;

            if (LooksLikeCopiedFormatExample(text))
                return "copied prompt format example";

            if (LooksLikeInstructionLeak(text))
                return "prompt/instruction leak";

            if (ContainsTechnicalContextLabelLeak(text))
                return "technical context label leaked into dialogue";

            if (ContainsForbiddenOutfitLiteral(text, context))
                return "saved outfit name was mentioned literally";

            string themeSpecificityIssue = ValidateRecognizableThemeSpecificity(text, context);
            if (!string.IsNullOrWhiteSpace(themeSpecificityIssue))
                return themeSpecificityIssue;

            string accessoryCombinationIssue = ValidateAccessoryOutfitCombinationSpecificity(text, context);
            if (!string.IsNullOrWhiteSpace(accessoryCombinationIssue))
                return accessoryCombinationIssue;

            string specialItemToneIssue = ValidateMayorShortsTone(text, context);
            if (!string.IsNullOrWhiteSpace(specialItemToneIssue))
                return specialItemToneIssue;

            string privateFlusterIssue = ValidatePrivateRevealingFlusterCue(text, context);
            if (!string.IsNullOrWhiteSpace(privateFlusterIssue))
                return privateFlusterIssue;

            string playerGenderIssue = ValidatePlayerGenderTerms(text, context);
            if (!string.IsNullOrWhiteSpace(playerGenderIssue))
                return playerGenderIssue;

            string languageIssue = DetectLikelyWrongLanguage(text, context?.TargetLanguage);
            if (!string.IsNullOrWhiteSpace(languageIssue))
                return languageIssue;

            return null;
        }

        private static string ValidateMayorShortsTone(string text, OutfitAiContext context)
        {
            if (string.IsNullOrWhiteSpace(text) || context?.IsMayorShortsSpecialItem != true)
                return null;

            string lower = " " + StripDialogueMarkup(text).ToLowerInvariant() + " ";
            bool romanticized = ContainsAny(lower,
                " fica sexy", " ficou sexy", " está sexy", " esta sexy", " tá sexy", " ta sexy",
                " atraente demais", " tão atraente", " tao atraente", " sedutora", " sedutor",
                " irresistível", " irresistivel", " gostosa", " gostoso", " excitante",
                " perigosa com essa roupa", " perigoso com essa roupa",
                " não consigo desviar o olhar", " nao consigo desviar o olhar",
                " não consigo tirar os olhos", " nao consigo tirar os olhos",
                " you look sexy", " you look hot", " so attractive", " seductive", " irresistible",
                " can't look away", " cannot look away", " can't take my eyes off", " turning me on");

            return romanticized
                ? "Mayor's shorts reaction romanticized or sexualized the farmer; treat the item only as absurd, embarrassing, scandalous, disgusting, concerning, or shocking"
                : null;
        }

        private static string ValidatePlayerGenderTerms(string text, OutfitAiContext context)
        {
            if (string.IsNullOrWhiteSpace(text) || context == null)
                return null;

            string gender = (context.PlayerGender ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(gender))
                return null;

            string lower = " " + StripDialogueMarkup(text).ToLowerInvariant() + " ";

            if (gender == "female" || gender == "feminine" || gender == "woman" || gender == "mulher")
            {
                string[] forbiddenFemaleAddress =
                {
                    " meu rapaz ", " rapaz ", " garoto ", " menino ", " moço ", " moco ",
                    " senhor ", " homem ", " meu cara ", " cara,", " cara. ", " brother ",
                    " bro ", " dude ", " boy ", " man "
                };

                if (ContainsAny(lower, forbiddenFemaleAddress))
                    return "dialogue used masculine address for a female farmer/player";
            }
            else if (gender == "male" || gender == "masculine" || gender == "man" || gender == "homem")
            {
                string[] forbiddenMaleAddress =
                {
                    " minha moça ", " minha moca ", " moça ", " moca ", " garota ",
                    " menina ", " senhora ", " mulher ", " lady ", " girl "
                };

                if (ContainsAny(lower, forbiddenMaleAddress))
                    return "dialogue used feminine address for a male farmer/player";
            }

            return null;
        }

        public static string ValidateDialogueBoxPacing(string text, ModConfig config, ActiveAiSettings ai)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string[] boxes = text.Split(new[] { "#$b#" }, StringSplitOptions.None)
                .Select(b => StripDialogueMarkup(b).Trim())
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToArray();

            if (boxes.Length <= 0)
                return null;

            int visibleTotal = CountVisibleDialogueCharacters(text);

            // Allow naturally talkative NPCs to use one, two, or several dialogue boxes.
            // This only catches extremely cramped single boxes; it does not force a box count.
            int largestBox = boxes.Select(CountVisibleDialogueCharacters).DefaultIfEmpty(0).Max();
            if (boxes.Length == 1 && visibleTotal >= 360 && largestBox >= 330)
                return "very long one-box dialogue should contain at least one #$b# break for Stardew pacing";

            return null;
        }

        public static string ValidateRecognizableThemeSpecificity(string text, OutfitAiContext context)
        {
            if (string.IsNullOrWhiteSpace(text) || !HasRecognizableThemeClue(context))
                return null;

            string stripped = StripDialogueMarkup(text);
            string lower = " " + stripped.ToLowerInvariant() + " ";

            bool blandSuitPhrase = ContainsAny(lower,
                "combina com você", "combina com voce", "combina contigo",
                "fica bem em você", "fica bem em voce", "ficou bem em você", "ficou bem em voce",
                "ficou legal em você", "ficou legal em voce", "fica legal em você", "fica legal em voce",
                "combina demais com você", "combina demais com voce",
                "estranhamente legal", "curiosamente legal");

            if (!blandSuitPhrase)
                return null;

            bool hasCreativeAngle = stripped.Contains("?") || ContainsAny(lower,
                "por que", "pra quê", "pra que", "fantasia", "evento", "festival", "aposta",
                "mascote", "cosplay", "saloon", "mina", "caverna", "slime", "monstro",
                "fazenda", "galinha", "vaca", "cabra", "porco", "plantação", "plantacao", "colheita",
                "se você", "se voce", "imagino", "imaginar", "dá pra imaginar", "da pra imaginar",
                "parece que", "parece roupa", "assustar", "fugiu", "missão", "missao",
                "horrorosa", "horrível", "horrivel", "feia", "ridícula", "ridicula",
                "engraçada", "engracada", "absurda", "bizarra", "suspeita");

            if (hasCreativeAngle)
                return null;

            return "recognizable theme reaction was too generic; add a joke, question, imagined scenario, or stronger in-character reaction drawn from this NPC's own personality instead of just saying it suits the farmer";
        }

        public static string ValidateAccessoryOutfitCombinationSpecificity(string text, OutfitAiContext context)
        {
            bool accessoryChangeWithOutfit = context?.IsAccessoryChange == true;
            bool savedOutfitWithAccessory = context?.IsOutfitChange == true && context.SavedOutfitIncludesMeaningfulAccessory;
            if (string.IsNullOrWhiteSpace(text) || (!accessoryChangeWithOutfit && !savedOutfitWithAccessory))
                return null;

            if (!HasRecognizableThemeClue(context) || !HasRecognizableOutfitThemeClue(context))
                return null;

            string accessoryClue = string.Join(" ", new[]
            {
                context.SafeNoticedChangeHint,
                context.NoticedChangeName,
                context.NoticedChangeType,
                context.SavedOutfitAccessoryHint
            }).ToLowerInvariant();

            // Only enforce this for clear, visible accessory concepts. Tiny makeup/earrings can
            // be commented on alone without sounding wrong. Large accessories should be compared
            // to the existing saved outfit/theme when that context is available.
            bool clearLargeAccessory = ContainsAny(accessoryClue,
                "wing", "wings", "asa", "asas", "angel", "anjo", "fairy", "fada",
                "cape", "capa", "backpack", "mochila", "umbrella", "guarda-chuva",
                "tail", "cauda", "horn", "horns", "chifre", "chifres", "halo",
                "bag", "bolsa", "shield", "escudo", "weapon", "sword", "espada",
                "claw", "claws", "garra", "garras", "spike", "spikes", "espinho", "espinhos");

            if (!clearLargeAccessory)
                return null;

            string stripped = StripDialogueMarkup(text);
            string lower = " " + stripped.ToLowerInvariant() + " ";

            bool combinedAngle = ContainsAny(lower,
                "pikachu", "pokemon", "pokémon", "sanrio", "my melody", "mymelody",
                "fantasia", "mascote", "cosplay", "personagem", "bicho", "animal",
                "lagarto", "lizard", "dinossauro", "dinosaur", "sapo", "frog",
                "gato", "cat", "coelho", "rabbit", "roupa", "visual", "look",
                "junto", "junto com", "por cima", "em cima", "com essa", "com esse",
                "mistura", "misturou", "combinação", "combinacao", "híbrido", "hibrido",
                "combina", "combinou", "reforça", "reforca", "contrasta", "contraste", "transforma",
                "não existe", "nao existe", "agora tem", "ganhou asa", "ganhou asas",
                "asas em", "asa em", "com asas", "com asa", "sem asas", "voar",
                "fazenda", "galinha", "vaca", "slime", "mina", "saloon", "festival");

            if (combinedAngle)
                return null;

            return "reaction ignored a clearly named large accessory or the existing themed outfit; relate both parts or react to the combined look instead of describing only one of them";
        }

        public static string ValidateSemanticThemeAnchor(string dialogueText, string themeAnchor, bool required)
        {
            if (!required)
                return null;

            string anchor = (themeAnchor ?? "").Trim().Trim('"', '\'', '“', '”');
            if (string.IsNullOrWhiteSpace(anchor))
                return "semantic theme anchor is missing for a readable saved-outfit theme combined with a large accessory";

            if (anchor.Length > 100 || Regex.Matches(anchor, @"\S+").Count > 8)
                return "semantic theme anchor must be a short exact excerpt from the spoken dialogue";

            string spoken = StripDialogueMarkup(dialogueText ?? "");
            if (spoken.IndexOf(anchor, StringComparison.OrdinalIgnoreCase) < 0)
                return "semantic theme anchor is not an exact excerpt from the spoken dialogue";

            string semanticRemainder = Regex.Replace(anchor.ToLowerInvariant(), @"[^\p{L}\p{M}\p{N}]+", " ");
            semanticRemainder = Regex.Replace(
                semanticRemainder,
                @"(?i)\b(the|a|an|this|that|these|those|with|and|or|of|your|look|outfit|clothes|clothing|dress|shirt|skirt|visual|roupa|vestido|camisa|saia|esse|essa|isso|com|e|ou|de|do|da|seu|sua|red|blue|green|yellow|pink|purple|orange|black|white|brown|gray|grey|dark|light|vermelho|vermelha|azul|verde|amarelo|amarela|rosa|roxo|roxa|laranja|preto|preta|branco|branca|marrom|cinza|escuro|escura|claro|clara|wing|wings|asa|asas|cape|capa|backpack|mochila|umbrella|moth|butterfly|mariposa|borboleta|accessory|acessório|acessorio)\b",
                " ");
            semanticRemainder = Regex.Replace(semanticRemainder, @"\s+", " ").Trim();
            if (!semanticRemainder.Any(char.IsLetterOrDigit))
                return "semantic theme anchor contains only colors, clothing, or the accessory; it must quote the outfit theme or a natural thematic allusion";

            return null;
        }

        public static bool IsSemanticThemeAnchorIssue(string issue)
            => !string.IsNullOrWhiteSpace(issue)
                && issue.StartsWith("semantic theme anchor", StringComparison.OrdinalIgnoreCase);

        private static bool HasRecognizableOutfitThemeClue(OutfitAiContext context)
        {
            if (context == null)
                return false;

            string outfitClues = string.Join(" ", new[]
            {
                context.SafeOutfitHint,
                context.ThemeContext,
                context.ThemePriorityInstruction,
                context.DialogueKey,
                context.OutfitName
            }).ToLowerInvariant();

            return ContainsRecognizableClueTerm(outfitClues,
                "sanrio", "my melody", "mymelody", "kuromi", "hello kitty", "cinnamoroll", "keroppi",
                "pikachu", "pokemon", "pokémon", "eevee", "jigglypuff", "charmander", "bulbasaur", "squirtle",
                "lizard", "lagarto", "dinosaur", "dinossauro", "dino", "frog", "sapo", "cat", "gato",
                "rabbit", "coelho", "bunny", "urso", "bear", "fox", "raposa", "wolf", "lobo",
                "chicken", "galinha", "cow", "vaca", "goat", "cabra", "pig", "porco",
                "fairy", "fada", "witch", "bruxa", "vampire", "vampiro", "angel", "anjo", "demon", "demônio", "demonio",
                "mermaid", "sereia", "slime", "monster", "monstro", "mascot", "mascote", "cosplay",
                "strawberry", "morango", "apple", "maçã", "maca", "orange", "laranja", "chocolate", "coffee", "café", "cafe",
                "cake", "bolo", "candy", "doce", "pumpkin", "abóbora", "abobora", "halloween", "christmas", "natal");
        }

        private static bool HasRecognizableThemeClue(OutfitAiContext context)
        {
            if (context == null)
                return false;

            string clues = string.Join(" ", new[]
            {
                context.SafeOutfitHint,
                context.SafeNoticedChangeHint,
                context.ThemeContext,
                context.ThemePriorityInstruction,
                context.DialogueKey,
                context.OutfitName,
                context.NoticedChangeName,
                context.NoticedChangeType
            }).ToLowerInvariant();

            return ContainsRecognizableClueTerm(clues,
                "sanrio", "my melody", "mymelody", "kuromi", "hello kitty", "cinnamoroll", "keroppi",
                "pikachu", "pokemon", "pokémon", "eevee", "jigglypuff", "charmander", "bulbasaur", "squirtle",
                "lizard", "lagarto", "dinosaur", "dinossauro", "dino", "frog", "sapo", "cat", "gato",
                "rabbit", "coelho", "bunny", "urso", "bear", "fox", "raposa", "wolf", "lobo",
                "chicken", "galinha", "cow", "vaca", "goat", "cabra", "pig", "porco",
                "fairy", "fada", "witch", "bruxa", "vampire", "vampiro", "angel", "anjo", "demon", "demônio", "demonio",
                "mermaid", "sereia", "slime", "monster", "monstro", "mascot", "mascote", "cosplay",
                "strawberry", "morango", "apple", "maçã", "maca", "orange", "laranja", "chocolate", "coffee", "café", "cafe",
                "cake", "bolo", "candy", "doce", "pumpkin", "abóbora", "abobora", "halloween", "christmas", "natal");
        }

        private static bool ContainsRecognizableClueTerm(string source, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(source) || terms == null)
                return false;

            foreach (string rawTerm in terms)
            {
                string term = (rawTerm ?? "").Trim();
                if (term.Length <= 0)
                    continue;

                // Short English theme words like "cat", "cow", "pig", or "fox" should not
                // accidentally match unrelated metadata words such as "category". Use word
                // boundaries for compact ASCII terms, and substring matching for longer or
                // multi-word clues like "my melody" or "dinossauro".
                bool compactAscii = term.Length <= 4 && Regex.IsMatch(term, @"^[a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (compactAscii)
                {
                    if (Regex.IsMatch(source, @"(?<![A-Za-z0-9_])" + Regex.Escape(term) + @"(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                        return true;
                }
                else if (source.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldRequireDialogueBreak(string text, ModConfig config, ActiveAiSettings ai)
        {
            // Require a break only for truly huge one-box lines that would be cramped in Stardew.
            int visible = CountVisibleDialogueCharacters(text);
            return visible >= 360;
        }

        private static int CountVisibleDialogueCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string cleaned = StripDialogueMarkup(text);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned.Length;
        }

        private static string ValidatePrivateRevealingFlusterCue(string text, OutfitAiContext context)
        {
            // Do not hard-reject private/revealing outfit reactions for lacking literal
            // blush/fluster words. The prompt now lets each NPC profile and heart level decide
            // whether the reaction is awkward, funny, shy, romantic, blunt, or casual.
            return null;
        }

        private static bool ContainsForbiddenOutfitLiteral(string text, OutfitAiContext context)
        {
            if (string.IsNullOrWhiteSpace(text) || context == null)
                return false;

            foreach (string candidate in BuildForbiddenOutfitLiteralCandidates(context))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string trimmed = candidate.Trim();
                if (trimmed.Length < 3)
                    continue;

                // Do not reject normal theme words. A line can naturally mention "chocolate",
                // "chocolate quente", "sapinho", "natal", "fairy", "coffee", etc. The goal is
                // only to prevent ugly saved-slot/file/key names from leaking literally.
                if (!LooksLikeTechnicalOrOverSpecificOutfitName(trimmed))
                    continue;

                string pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(trimmed) + @"(?![\p{L}\p{N}])";
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }

            return false;
        }

        public static bool LooksLikeTechnicalOrOverSpecificOutfitName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            string lower = trimmed.ToLowerInvariant();

            // Be deliberately lenient. Many players name saved outfits with words like
            // "outfit", long natural phrases, or theme descriptions. Those should NOT make
            // a good line get rejected. Only block names that look like internal keys,
            // filenames, IDs, or content-pack routing labels.
            if (Regex.IsMatch(trimmed, @"[\[\]{}]|\.json|\.png|\.pack|\.content|\.cp", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(trimmed, @"[_/\\]|--|::|\$|#", RegexOptions.CultureInvariant))
                return true;

            if (Regex.IsMatch(trimmed, @"[a-zà-ÿ][A-Z]"))
                return true;

            string[] hardTechnicalMarkers =
            {
                "npcroom",
                "dialoguekey",
                "textsource",
                "content pack",
                "fashion sense",
                "internal",
                "variant:",
                "theme:",
                "category:",
                "dialogue:",
                "preset:"
            };

            foreach (string marker in hardTechnicalMarkers)
            {
                if (lower.Contains(marker))
                    return true;
            }

            // Numeric IDs are suspicious only when they look like version/file/key labels.
            // This avoids rejecting natural names like "Outfit 1" unless the label also has
            // clearly technical structure.
            if (Regex.IsMatch(trimmed, @"\b(v\d+|id\s*\d+|key\s*\d+)\b", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        private static bool ContainsTechnicalContextLabelLeak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string lower = " " + StripDialogueMarkup(text).ToLowerInvariant() + " ";
            lower = Regex.Replace(lower, @"\s+", " ");

            string[] exactBad =
            {
                " indoor ",
                " outdoor ",
                " npc room ",
                " npcroom ",
                " location type ",
                " tipo de local ",
                " theme guidance ",
                " orientação de tema ",
                " orientacao de tema ",
                " dialogue category ",
                " outfit category ",
                " categoria de diálogo ",
                " categoria de dialogo ",
                " categoria da roupa ",
                " summer indoor ",
                " verão indoor ",
                " verao indoor ",
                " indoor theme ",
                " tema indoor ",
                " inside variant ",
                " outside variant ",
                " npc-specific ",
                " textsource "
            };

            foreach (string marker in exactBad)
            {
                if (lower.Contains(marker))
                    return true;
            }

            // A normal character can say something has a theme, but local models often leak phrases like
            // "summer indoor theme" or "tema do verão indoor" from our private routing labels.
            if ((lower.Contains(" theme ") || lower.Contains(" tema ")) && lower.Contains(" indoor"))
                return true;

            return false;
        }

        private static IEnumerable<string> BuildForbiddenOutfitLiteralCandidates(OutfitAiContext context)
        {
            HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

            void Add(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                value = Regex.Replace(value.Trim(), @"\s+", " ");
                if (value.Length >= 3)
                    values.Add(value);
            }

            Add(context.OutfitName);

            if (!string.IsNullOrWhiteSpace(context.OutfitName))
            {
                string spaced = Regex.Replace(context.OutfitName, @"[_\-.]+", " ");
                spaced = Regex.Replace(spaced, @"([a-zà-ÿ])([A-Z])", "$1 $2");
                spaced = Regex.Replace(spaced, @"\s{2,}", " ").Trim();
                Add(spaced);
            }

            return values;
        }

        private static string DetectLikelyWrongLanguage(string text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(targetLanguage))
                return null;

            string stripped = Regex.Replace(text, @"#\$b#|\$[a-z0-9]+|\{\{.*?\}\}", " ", RegexOptions.IgnoreCase);
            string lower = " " + stripped.ToLowerInvariant() + " ";

            if (targetLanguage.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int score = 0;
                if (Regex.IsMatch(lower, @"[ãõçáéíóúâêôà]") )
                    score += 2;

                string[] portugueseMarkers =
                {
                    " você ", " voce ", " está ", " esta ", " isso ", " aqui ", " roupa ", " visual ",
                    " combina ", " gostei ", " natal ", " primavera ", " inverno ", " verão ", " verao ",
                    " outono ", " estranho ", " usar ", " seu ", " sua ", " que ", " tem ", " toque "
                };

                foreach (string marker in portugueseMarkers)
                {
                    if (lower.Contains(marker))
                        score++;
                }

                if (score >= 2)
                    return "wrong language: expected English, but the dialogue looks Portuguese";
            }

            if (targetLanguage.IndexOf("Portuguese", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int score = 0;
                string[] englishMarkers =
                {
                    " the ", " outfit ", " look ", " style ", " you ", " your ", " this ", " that ",
                    " really ", " cute ", " nice ", " spring ", " winter ", " christmas ", " strange "
                };

                foreach (string marker in englishMarkers)
                {
                    if (lower.Contains(marker))
                        score++;
                }

                if (score >= 3 && !Regex.IsMatch(lower, @"[ãõçáéíóúâêôà]"))
                    return "wrong language: expected Brazilian Portuguese, but the dialogue looks English";
            }

            return null;
        }

        public static string RestoreEllipsesAndNormalise(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Convert a lone period followed by space/break + lowercase letter into ellipsis.
            // This fixes the common LLM habit of using "." as a dramatic pause mid-sentence.
            // e.g. "de pato. combina" -> "de pato... combina"
            // e.g. "Heh. eu gosto"   -> "Heh... eu gosto"
            // Sentences ending properly (uppercase after period) are left untouched.
            text = Regex.Replace(
                text,
                @"(?<!\.)\.(?!\.\.)(?:\s+|#\$b#)([a-záàâãéèêíìîóòôõúùûçñ])",
                m => "... " + m.Groups[1].Value);

            // Normalise incomplete ellipses: .. -> ... and .... -> ...
            text = Regex.Replace(text, @"\.{4,}", "...");
            text = Regex.Replace(text, @"(?<!\.)\.{2}(?!\.)", "...");

            return text;
        }

        public static string NormaliseDialogueBreakTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string result = text
                .Replace("＃", "#")
                .Replace("﹟", "#")
                .Replace("＄", "$");

            // Some providers frequently drop the first # from Stardew's dialogue break,
            // returning "$b#" instead of "#$b#". Normalize that before the portrait
            // sanitizer runs; otherwise "$b" looks like an inline portrait command and
            // gets removed, making valid multi-box dialogue fail validation.
            result = Regex.Replace(result, @"#\s*\$\s*b\s*#", "#$b#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            result = Regex.Replace(result, @"(?<!#)\$\s*b\s*#", "#$b#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Extra tolerance for models that copy the token with spaces.
            result = Regex.Replace(result, @"#\s*\$\s*b\s*#", "#$b#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            result = Regex.Replace(result, @"\s*#\$b#\s*", "#$b#", RegexOptions.CultureInvariant);
            return result;
        }

        public static string CleanDialogueText(string line, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            string text = line.Trim();
            text = text.Replace("\r\n", "#$b#").Replace("\n", "#$b#").Replace("\r", "#$b#");
            text = text.Replace("\"", "").Replace("“", "").Replace("”", "");
            text = NormaliseDialogueBreakTokens(text);
            // Strip accidental list markers / local prompt bullets at the beginning of the
            // dialogue and at the beginning of each dialogue box, e.g. "-hey..." -> "hey...".
            text = Regex.Replace(text, @"(^|#\$b#)\s*[-–—•]+\s*", "$1").Trim();
            // Strip leading stray punctuation at the start of each dialogue box (period, comma,
            // semicolon etc.) that can appear when portrait commands are removed mid-sentence.
            text = Regex.Replace(text, @"(^|#\$b#)\s*[.,;:]+\s*", "$1").Trim();
            text = Regex.Replace(text, @"\s+#\$b#\s+", "#$b#");
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();

            // Normalize informal internet slang: "u " used as "eu " in Brazilian PT.
            // Only fix at sentence start or after ellipsis/pause to avoid false positives.
            text = Regex.Replace(text, @"(?<=\.\.\. )u\b", "eu", RegexOptions.CultureInvariant);
            text = Regex.Replace(text, @"(?<=^|#\$b#)u\b", "eu", RegexOptions.CultureInvariant);

            // Fix malformed portrait+break hybrids like #$h# #$l# #$16# that the AI
            // sometimes generates by mixing the $portrait and #$b# syntaxes together.
            // Convert them to proper form: portrait command followed by a #$b# break.
            // e.g. "#$h#" -> "$h#$b#"   "#$16#" -> "$16#$b#"
            text = Regex.Replace(text, @"#\$(?!b)([A-Za-z0-9]+)#", "$$1#$b#", RegexOptions.CultureInvariant);
            // Clean up any double #$b## that may result
            text = Regex.Replace(text, @"(#\$b#){2,}", "#$b#", RegexOptions.CultureInvariant);

            // Remove markdown bold (**text**) — never intentional in Stardew dialogue.
            // Single *action* asterisks are kept since they are valid stylistic emotes.
            text = Regex.Replace(text, @"\*\*([^*]*)\*\*", "$1", RegexOptions.CultureInvariant);
            text = Regex.Replace(text, @"\*{2,}", "", RegexOptions.CultureInvariant);
            // Ensure space after closing asterisk: "*ação*texto" -> "*ação* texto"
            // Also handles "*ação*.Texto" -> "*ação* Texto" (period stuck between asterisk and next word)
            // Pattern: word char + * + optional period + letter = closing asterisk stuck to next word
            text = Regex.Replace(text, @"(\w)\*\.?([A-Za-záàâãéèêíìîóòôõúùûçñÁÀÂÃÉÈÊÍÌÎÓÒÔÕÚÙÛÇÑ])", "$1* $2", RegexOptions.CultureInvariant);
            // Remove spurious space between opening asterisk and first word.
            // e.g. "* murmura baixinho*" -> "*murmura baixinho*"
            // Use negative lookbehind (?<!\w) so we only target true opening asterisks (not preceded
            // by a word char), never the closing-asterisk space that the previous step just added.
            text = Regex.Replace(text, @"(?<!\w)\*\s+([A-Za-záàâãéèêíìîóòôõúùûçñÁÀÂÃÉÈÊÍÌÎÓÒÔÕÚÙÛÇÑ])", "*$1", RegexOptions.CultureInvariant);
            // Ensure there is a space between a portrait command and the following word.
            // e.g. "$hCaaara..." -> "$h Caaara..."  "$16olha" -> "$16 olha"
            text = Regex.Replace(text, @"(\$[A-Za-z0-9]+)([A-Za-záàâãéèêíìîóòôõúùûçñÁÀÂÃÉÈÊÍÌÎÓÒÔÕÚÙÛÇÑ])", "$1 $2", RegexOptions.CultureInvariant);


            // maxCharacters is intentionally a generation target, not a post-processing cutoff.
            // Keep the complete response once the provider has generated it; truncating here can
            // discard the end of a sentence or an entire final dialogue box.
            _ = maxCharacters;

            return text;
        }

        public static bool LooksLikeCopiedFormatExample(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string lower = text.ToLowerInvariant();
            return lower.Contains("essa roupa ficou boa em você")
                || lower.Contains("essa roupa ficou boa em voce")
                || lower.Contains("não faz essa cara")
                || lower.Contains("nao faz essa cara")
                || lower.Contains("that outfit actually looks good on you")
                || lower.Contains("don't make that face")
                || lower.Contains("dont make that face")
                || lower.Contains("spoken outfit reaction")
                || lower.Contains("current game language")
                || lower.Contains("optional portrait command");
        }

        public static bool LooksLikeInstructionLeak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string lower = text.ToLowerInvariant();
            return lower.Contains("dialogue box break")
                || lower.Contains("return json")
                || lower.Contains("stardew valley-style")
                || lower.Contains("npc characteristics")
                || lower.Contains("available portraits")
                || lower.Contains("current context")
                || lower.Contains("contexto atual")
                || lower.Contains("categorias de diálogo")
                || lower.Contains("categoria de diálogo")
                || lower.Contains("tone guidance")
                || lower.Contains("use #$b#")
                || lower.Contains("metadata")
                || lower.Contains("json only")
                || lower.Contains("return only json")
                || lower.Contains("portrait:")
                || lower.Contains("**portrait**")
                || lower.Contains("tonalidade:")
                || lower.Contains("personalidade de sebastian");
        }

        private static bool ContainsAny(string source, params string[] terms)
        {
            if (string.IsNullOrEmpty(source))
                return false;
            foreach (string t in terms)
                if (source.Contains(t, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static string StripDialogueMarkup(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string stripped = Regex.Replace(text, @"#\$b#|\$[a-z0-9]+|\{\{.*?\}\}", " ", RegexOptions.IgnoreCase);
            stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
            return stripped;
        }
    }
}
