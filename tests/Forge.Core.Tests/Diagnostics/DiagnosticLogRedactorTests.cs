using Forge.Core.Abstractions.Diagnostics;
using Shouldly;

namespace Forge.Core.Tests.Diagnostics;

/// <summary>
/// Tries to get health data past the redactor.
/// </summary>
/// <remarks>
/// <para>
/// These are written as attacks rather than as examples. Forge encrypts its database with
/// SQLCipher specifically so body weight, injuries, food logs and profile names are not readable
/// at rest; a plaintext log that captures them has undone that, next to the file it was supposed
/// to protect. Most of what Forge holds is GDPR Article 9 special-category data, so a leak here is
/// a breach rather than an untidiness.
/// </para>
/// <para>
/// The second half of the file matters just as much. A redactor that removes everything is easy
/// and useless: these tests pin the diagnostics that must survive, because the whole feature
/// exists so that somebody can read this file and find a fault.
/// </para>
/// </remarks>
public class DiagnosticLogRedactorTests
{
    private const int Cap = 4000;

    // ---- Things that must not survive ----

    [Fact]
    public void A_body_weight_written_with_its_unit_does_not_survive()
    {
        var result = DiagnosticLogRedactor.Redact("Rejected the entry because 82.4 kg is out of range", Cap);

        result.ShouldNotContain("82.4");
        result.ShouldContain(DiagnosticLogRedactor.MeasurementMarker);
    }

    [Fact]
    public void A_body_weight_written_without_a_unit_does_not_survive()
    {
        // The shape a validation exception takes most often, because the unit lives in the
        // property's type rather than in its message. The unit rule cannot see this one at all;
        // the proximity rule is what catches it.
        var result = DiagnosticLogRedactor.Redact("Body weight 82.4 was rejected", Cap);

        result.ShouldNotContain("82.4");
        result.ShouldContain(DiagnosticLogRedactor.NumberMarker);
    }

    [Fact]
    public void An_injury_description_after_a_label_does_not_survive()
    {
        var result = DiagnosticLogRedactor.Redact("injury: Left knee ACL reconstruction, avoid deep flexion", Cap);

        result.ShouldNotContain("knee");
        result.ShouldNotContain("ACL");
        result.ShouldContain(DiagnosticLogRedactor.RedactedMarker);
    }

    [Fact]
    public void The_tail_of_a_free_text_note_after_a_comma_does_not_survive_either()
    {
        // Found on a device, not by reading the code. The value pattern used to stop at the first
        // comma, so a two-clause injury note came out as
        // "injury note: <redacted>, avoid deep flexion" - the redactor removing the first half of
        // a sentence and printing the second is worse than not running, because the file looks
        // redacted.
        var result = DiagnosticLogRedactor.Redact(
            "Body weight 82.4 kg rejected; injury note: Left knee - ACL reconstruction 2019, avoid deep flexion",
            Cap);

        result.ShouldNotContain("knee");
        result.ShouldNotContain("flexion");
        result.ShouldNotContain("82.4");

        // The semicolon is still a boundary, so the two facts stay separable.
        result.ShouldContain("injury note:");
    }

    [Fact]
    public void A_quoted_injury_description_does_not_survive()
    {
        var result = DiagnosticLogRedactor.Redact(
            "ArgumentException: 'Left knee - ACL reconstruction 2019' is not a valid note.",
            Cap);

        result.ShouldNotContain("knee");
        result.ShouldNotContain("ACL");
    }

    [Fact]
    public void A_quoted_number_does_not_survive_even_with_no_health_word_anywhere()
    {
        // FormatException is the realistic carrier: it quotes the input verbatim and says nothing
        // about what the input was, so neither the unit rule nor the proximity rule can help.
        var result = DiagnosticLogRedactor.Redact(
            "System.FormatException: The input string '82.4' was not in a correct format.",
            Cap);

        result.ShouldNotContain("82.4");
        result.ShouldContain("System.FormatException");
    }

    [Fact]
    public void An_export_filename_built_from_a_profile_name_does_not_survive()
    {
        var result = DiagnosticLogRedactor.Redact(
            "Could not open /data/user/0/com.nikomix.forge/files/Alexandra-export.json",
            Cap);

        result.ShouldNotContain("Alexandra");
        result.ShouldNotContain("com.nikomix.forge");
        result.ShouldContain("<path.json>");
    }

    [Fact]
    public void A_path_arriving_as_a_file_uri_does_not_survive()
    {
        // The generic path rule cannot see this one: its leading slash is preceded by another
        // slash, which is the guard that keeps http URLs intact. It needs its own rule, and this
        // is what proves the rule is there.
        var result = DiagnosticLogRedactor.Redact(
            "Share failed for file:///data/user/0/com.nikomix.forge/cache/Sam-backup.forge",
            Cap);

        result.ShouldNotContain("Sam");
        result.ShouldNotContain("com.nikomix.forge");
    }

    [Fact]
    public void A_windows_path_does_not_survive_whichever_separator_it_arrives_with()
    {
        DiagnosticLogRedactor.Redact(@"at C:\Users\alexandra\forge\notes.txt", Cap)
            .ShouldNotContain("alexandra");

        DiagnosticLogRedactor.Redact("at C:/Users/alexandra/forge/notes.txt", Cap)
            .ShouldNotContain("alexandra");
    }

    [Fact]
    public void An_email_address_does_not_survive()
    {
        DiagnosticLogRedactor.Redact("Sync refused for alex.smith+forge@example.com", Cap)
            .ShouldBe($"Sync refused for {DiagnosticLogRedactor.EmailMarker}");
    }

    [Fact]
    public void A_training_date_does_not_survive()
    {
        // When somebody trained is health data on its own, even with nothing else attached.
        var result = DiagnosticLogRedactor.Redact("Session 2026-02-11T06:30:00Z could not be summarised", Cap);

        result.ShouldNotContain("2026-02-11");
        result.ShouldContain(DiagnosticLogRedactor.DateMarker);
    }

    [Fact]
    public void A_food_log_entry_does_not_survive()
    {
        var result = DiagnosticLogRedactor.Redact("meal: chicken and rice, 640 kcal, protein 48 g", Cap);

        result.ShouldNotContain("chicken");
        result.ShouldNotContain("640");
        result.ShouldNotContain("48");
    }

    [Fact]
    public void A_profile_name_after_a_label_does_not_survive()
    {
        DiagnosticLogRedactor.Redact("Active profile named Alexandra could not be resolved", Cap)
            .ShouldNotContain("Alexandra");
    }

    [Fact]
    public void A_body_measurement_does_not_survive()
    {
        var result = DiagnosticLogRedactor.Redact("waist 96 cm, hip 104 cm, height 181 cm", Cap);

        result.ShouldNotContain("96");
        result.ShouldNotContain("104");
        result.ShouldNotContain("181");
    }

    [Fact]
    public void A_long_identifier_that_could_be_an_account_number_does_not_survive()
    {
        DiagnosticLogRedactor.Redact("Reference 4929123456789012 was refused", Cap)
            .ShouldNotContain("4929123456789012");
    }

    // ---- Things that must survive, or the log is not worth writing ----

    [Fact]
    public void An_exception_type_name_survives()
    {
        DiagnosticLogRedactor.Redact("Microsoft.Data.Sqlite.SqliteException was thrown", Cap)
            .ShouldContain("Microsoft.Data.Sqlite.SqliteException");
    }

    [Fact]
    public void A_quoted_property_name_survives_because_it_is_a_dotted_identifier()
    {
        // This is the split the whole quoting rule turns on: 'Exercise.Id' is what makes a
        // SQLite constraint failure diagnosable, and it cannot contain anything a user typed.
        DiagnosticLogRedactor.Redact("UNIQUE constraint failed on 'Exercise.Id'", Cap)
            .ShouldContain("'Exercise.Id'");
    }

    [Fact]
    public void Counts_and_durations_survive_when_no_health_word_is_near_them()
    {
        var result = DiagnosticLogRedactor.Redact("Imported 60 exercises in 412 ms after 3 retries", Cap);

        result.ShouldContain("60");
        result.ShouldContain("412 ms");
        result.ShouldContain("3");
    }

    [Fact]
    public void Byte_sizes_survive()
    {
        // 'MB' is deliberately not in the unit list. Redacting sizes would delete the log's
        // account of its own behaviour while protecting nobody.
        DiagnosticLogRedactor.Redact("Cache holds 128 MB across 4 packs", Cap)
            .ShouldContain("128 MB");
    }

    [Fact]
    public void An_ordinary_apostrophe_does_not_open_a_quoted_run()
    {
        // Two apostrophes in one English sentence used to pair up and swallow everything between
        // them, which turned Forge's own log messages into "<redacted>".
        var result = DiagnosticLogRedactor.Redact("Forge couldn't open what wasn't there", Cap);

        result.ShouldBe("Forge couldn't open what wasn't there");
    }

    [Fact]
    public void A_support_url_survives()
    {
        // URLs in an exception message are almost always a framework's own documentation link.
        // They are useful and they are not personal, so the path rule is written to leave them.
        DiagnosticLogRedactor.Redact("See https://aka.ms/efcore-docs/sqlite-limitations for detail", Cap)
            .ShouldContain("https://aka.ms/efcore-docs/sqlite-limitations");
    }

    [Fact]
    public void The_reason_a_file_could_not_be_opened_survives_alongside_the_redacted_path()
    {
        // The path rule matched greedily once and ate the rest of the sentence with the path,
        // so the log said a file failed and never said why.
        var result = DiagnosticLogRedactor.Redact(
            "IOException: could not open /data/user/0/com.nikomix.forge/files/forge.db because the disk is full",
            Cap);

        result.ShouldContain("because the disk is full");
        result.ShouldContain("<path.db>");
    }

    // ---- Failure modes ----

    [Fact]
    public void Text_is_truncated_to_the_cap()
    {
        var result = DiagnosticLogRedactor.Redact(new string('a', 500), 100);

        result.Length.ShouldBe(100 + DiagnosticLogRedactor.TruncationMarker.Length);
        result.ShouldEndWith(DiagnosticLogRedactor.TruncationMarker);
    }

    [Fact]
    public void Null_and_empty_are_empty()
    {
        DiagnosticLogRedactor.Redact(null, Cap).ShouldBe(string.Empty);
        DiagnosticLogRedactor.Redact(string.Empty, Cap).ShouldBe(string.Empty);
    }

    [Fact]
    public void An_exception_keeps_its_type_and_stack_but_not_its_message()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => throw new InvalidOperationException("Body weight 82.4 kg failed validation"));

        var described = DiagnosticLogRedactor.Describe(exception, DiagnosticLogOptions.Default);

        described.ShouldContain("System.InvalidOperationException");
        described.ShouldContain(nameof(An_exception_keeps_its_type_and_stack_but_not_its_message));
        described.ShouldNotContain("82.4");
    }

    [Fact]
    public void An_inner_exception_message_is_redacted_too()
    {
        var exception = new InvalidOperationException(
            "Could not save the session",
            new ArgumentException("injury: torn left meniscus"));

        var described = DiagnosticLogRedactor.Describe(exception, DiagnosticLogOptions.Default);

        described.ShouldContain("System.ArgumentException");
        described.ShouldNotContain("meniscus");
    }

    [Fact]
    public void A_null_exception_describes_as_nothing()
    {
        DiagnosticLogRedactor.Describe(null, DiagnosticLogOptions.Default).ShouldBe(string.Empty);
    }
}
