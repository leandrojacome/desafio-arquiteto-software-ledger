namespace Ledger.Domain.Tests.Support;

internal static class MessageStyle
{
    private const char EmDash = '\u2014';
    private const char EnDash = '\u2013';
    private const char LastLatinOneCharacter = 'ÿ';
    private const char Quote = '\'';

    private static readonly char[] WordSeparators = [' ', ',', '.', ':', ';', '(', ')'];

    private static readonly HashSet<string> SecondPersonAndFiller = new(StringComparer.OrdinalIgnoreCase)
    {
        "você", "seu", "sua", "seus", "suas", "favor", "infelizmente", "ops", "opa"
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "must", "does", "is", "was", "has", "have", "cannot", "with", "from", "this", "that", "are", "be",
        "of", "or", "for", "by", "not", "at", "if"
    };

    private static readonly HashSet<string> WordsThatLostTheirAccent = new(StringComparer.OrdinalIgnoreCase)
    {
        "nao", "ja", "voce", "lancamento", "descricao", "referencia", "invalido", "valido", "instrucao", "requisicao",
        "idempotencia", "cabecalho", "padrao", "maximo", "minimo", "possivel", "obrigatorio", "numero", "codigo",
        "servico", "disponivel", "indisponivel", "monetarios", "monetario", "especifico"
    };

    public static List<string> Violations(string message)
    {
        var violations = new List<string>();

        if (message.Length == 0 || !char.IsUpper(message[0]))
        {
            violations.Add("does not start with an upper case letter");
        }

        if (!message.EndsWith('.'))
        {
            violations.Add("does not end with a period");
        }

        if (message != message.Trim() || message.Contains("  ", StringComparison.Ordinal))
        {
            violations.Add("has stray whitespace");
        }

        if (message.Contains(EmDash, StringComparison.Ordinal) || message.Contains(EnDash, StringComparison.Ordinal))
        {
            violations.Add("uses a dash as punctuation");
        }

        if (message.Contains('!', StringComparison.Ordinal) || message.Contains("..", StringComparison.Ordinal))
        {
            violations.Add("uses an exclamation mark or an ellipsis");
        }

        if (message.Any(character => character > LastLatinOneCharacter))
        {
            violations.Add("carries a character outside the Latin-1 range");
        }

        if (HasMojibake(message))
        {
            violations.Add("looks like text decoded with the wrong encoding");
        }

        var words = OutsideQuotes(message).Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

        violations.AddRange(words.Where(SecondPersonAndFiller.Contains).Select(word => $"uses the word {word}"));
        violations.AddRange(words.Where(EnglishWords.Contains).Select(word => $"uses the English word {word}"));
        violations.AddRange(words.Where(WordsThatLostTheirAccent.Contains).Select(word => $"writes {word} without its accent"));

        return violations;
    }

    private static string OutsideQuotes(string message) =>
        string.Concat(message.Split(Quote).Where((_, index) => index % 2 == 0).Select(part => part + " "));

    private static bool HasMojibake(string message)
    {
        for (var index = 0; index < message.Length - 1; index++)
        {
            var lead = message[index];
            var follower = message[index + 1];

            if ((lead == 'Ã' || lead == 'Â') && follower is >= '\u0080' and <= '¿')
            {
                return true;
            }
        }

        return false;
    }
}
