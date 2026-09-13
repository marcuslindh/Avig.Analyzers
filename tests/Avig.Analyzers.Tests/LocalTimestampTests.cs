using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<LocalTimestampAnalyzer>;
using VerifyFix = CSharpCodeFixVerifier<LocalTimestampAnalyzer, LocalTimestampCodeFixProvider>;

public class LocalTimestampTests
{
    [Fact]
    public async Task Reports_now_today_and_offset_now()
    {
        await Verify.VerifyAsync("""
            using System;

            public class Stamps
            {
                public DateTime Created() => DateTime.{|AVIG0020:Now|};

                public DateTime Day() => DateTime.{|AVIG0020:Today|};

                public DateTimeOffset Offset() => DateTimeOffset.{|AVIG0020:Now|};
            }
            """);
    }

    [Fact]
    public async Task Silent_for_utc_readings()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System;

            public class Stamps
            {
                public DateTime Created() => DateTime.UtcNow;

                public DateTimeOffset Offset() => DateTimeOffset.UtcNow;
            }
            """);
    }

    [Fact]
    public async Task Silent_for_an_unrelated_now_property()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Clock
            {
                public static int Now => 0;
            }

            public class Stamps
            {
                public int Read() => Clock.Now;
            }
            """);
    }

    [Fact]
    public async Task Fixes_now_to_utc_now()
    {
        await VerifyFix.VerifyCodeFixAsync(
            """
            using System;

            public class Stamps
            {
                public DateTime Created() => DateTime.{|AVIG0020:Now|};
            }
            """,
            """
            using System;

            public class Stamps
            {
                public DateTime Created() => DateTime.UtcNow;
            }
            """);
    }

    [Fact]
    public async Task Fixes_today_to_the_utc_date()
    {
        await VerifyFix.VerifyCodeFixAsync(
            """
            using System;

            public class Stamps
            {
                public DateTime Day() => DateTime.{|AVIG0020:Today|};
            }
            """,
            """
            using System;

            public class Stamps
            {
                public DateTime Day() => DateTime.UtcNow.Date;
            }
            """);
    }
}
