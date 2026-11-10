namespace Ooshak.Core;

/// <summary>
/// Builds Whisper's initial prompt. Whisper keeps only the end of a long prompt and imitates the
/// style of what it reads, so session names come first and the sample sentence comes last.
/// </summary>
public static class VoicePrompt
{
    private const int MaxTitles = 10;

    private const string CodeSwitchedSample =
        "Branch'i merge edip production'a deploy alalım. Pull request'teki build hatasını fix'leyip commit atalım. " +
        "Endpoint'in response'una unit test yazalım, migration'ı rollback edelim, cache'i invalidate edelim.";

    private const string EnglishSample =
        "Merge the branch and deploy to production. Fix the build error in the pull request, then commit. " +
        "Add a unit test for the endpoint response, roll back the migration and invalidate the cache.";

    public static string Build(OoshakConfig config, IEnumerable<string> titles)
    {
        var names = titles.Take(MaxTitles).Concat(config.Aliases.Keys).Concat(config.Vocabulary).Concat(config.WakeNames);
        var sample = config.Language == L.English ? EnglishSample : CodeSwitchedSample;
        return $"{string.Join(", ", names)}. {sample}";
    }
}
