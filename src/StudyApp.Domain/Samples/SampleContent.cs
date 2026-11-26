using System.Reflection;

namespace StudyApp.Domain.Samples;

/// <summary>The sample subject offered on first start (same files as /samples in the repository).</summary>
public static class SampleContent
{
    public static string Json(string lang)
    {
        var name = lang == "bg" ? "StudyApp.Samples.sample-heart-bg.json" : "StudyApp.Samples.sample-heart-en.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded sample: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
