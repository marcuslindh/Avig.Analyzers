using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TrackedTodoCommentAnalyzer>;

public class TrackedTodoCommentTests
{
    [Fact]
    public async Task Reports_every_marker_without_a_reference()
    {
        await Verify.VerifyAsync("""
            public class Orders
            {
                // {|AVIG0023:TODO|}: handle the empty basket
                public void Place() { }

                // {|AVIG0023:FIXME|} this rounds the wrong way
                public void Total() { }

                // {|AVIG0023:HACK|} - works until the migration lands
                public void Migrate() { }

                // {|AVIG0023:XXX|} do not ship
                public void Draft() { }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_comment_carries_a_reference()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Orders
            {
                // TODO(#412): handle the empty basket
                public void Place() { }

                // FIXME ORDERS-77 rounds the wrong way
                public void Total() { }

                // HACK until https://github.com/marcuslindh/Avig.Analyzers/issues/9 is fixed
                public void Migrate() { }
            }
            """);
    }

    [Fact]
    public async Task Reports_inside_block_and_documentation_comments()
    {
        await Verify.VerifyAsync("""
            public class Orders
            {
                /* {|AVIG0023:TODO|}: split this type */

                /// <summary>Places an order. {|AVIG0023:TODO|}: validate the currency.</summary>
                public void Place() { }
            }
            """);
    }

    [Fact]
    public async Task Finds_a_reference_anywhere_in_a_block_comment()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Orders
            {
                /*
                 * TODO: split this type once the pricing module lands.
                 * Tracked as PRICE-31.
                 */
                public void Place() { }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_marker_is_part_of_a_longer_word()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Orders
            {
                // The TodoListItem type is documented elsewhere.
                // XXXL is a size, not a marker.
                public void Place() { }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_a_marker_inside_a_string_literal()
    {
        // Trivia only. A literal that mentions the marker is data, not a note to self.
        await Verify.VerifyNoDiagnosticAsync("""
            public class Orders
            {
                public string Banner() => "TODO: nothing to see here";
            }
            """);
    }

    [Fact]
    public async Task Reports_a_marker_named_in_editorconfig()
    {
        Verify.Test test = new()
        {
            TestState =
            {
                Sources =
                {
                    """
                    public class Orders
                    {
                        // {|AVIG0023:REVIEW|} the rounding before release
                        public void Total() { }
                    }
                    """,
                },
                AnalyzerConfigFiles =
                {
                    ("/.editorconfig", """
                        root = true

                        [*.cs]
                        dotnet_code_quality.AVIG0023.additional_markers = NOTE,REVIEW
                        """),
                },
            },
        };

        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Honours_a_custom_reference_pattern()
    {
        // With the pattern narrowed to WORK-nnn, a bare issue number no longer counts.
        Verify.Test test = new()
        {
            TestState =
            {
                Sources =
                {
                    """
                    public class Orders
                    {
                        // TODO WORK-88: handle the empty basket
                        public void Place() { }

                        // {|AVIG0023:TODO|}(#412): handle the currency
                        public void Total() { }
                    }
                    """,
                },
                AnalyzerConfigFiles =
                {
                    ("/.editorconfig", """
                        root = true

                        [*.cs]
                        dotnet_code_quality.AVIG0023.reference_pattern = WORK-\d+
                        """),
                },
            },
        };

        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Falls_back_to_the_default_pattern_when_the_configured_one_is_invalid()
    {
        // An unparseable pattern must not crash the analyzer and take the build with it.
        Verify.Test test = new()
        {
            TestState =
            {
                Sources =
                {
                    """
                    public class Orders
                    {
                        // TODO(#412): handle the empty basket
                        public void Place() { }

                        // {|AVIG0023:TODO|}: handle the currency
                        public void Total() { }
                    }
                    """,
                },
                AnalyzerConfigFiles =
                {
                    ("/.editorconfig", """
                        root = true

                        [*.cs]
                        dotnet_code_quality.AVIG0023.reference_pattern = (unclosed
                        """),
                },
            },
        };

        await test.RunAsync(CancellationToken.None);
    }
}
