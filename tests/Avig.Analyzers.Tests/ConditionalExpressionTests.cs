using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<ConditionalExpressionAnalyzer>;
using VerifyFix = CSharpCodeFixVerifier<ConditionalExpressionAnalyzer, ConditionalExpressionCodeFixProvider>;

public class ConditionalExpressionTests
{
    [Fact]
    public async Task Reports_conditionals_wherever_a_statement_could_take_their_place()
    {
        await Verify.VerifyAsync("""
            using System;

            public class Pricing
            {
                private readonly decimal _rate = {|AVIG0024:DateTime.UtcNow.Month == 12 ? 0.8m : 1m|};

                public string Label(bool member) => {|AVIG0024:member ? "Member" : "Guest"|};

                public Func<int, int> Clamp { get; } = value => {|AVIG0024:value < 0 ? 0 : value|};

                public void Print(int count)
                {
                    Console.WriteLine({|AVIG0024:count == 1 ? "item" : "items"|});
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_each_level_of_a_nested_conditional()
    {
        await Verify.VerifyAsync("""
            public class Grading
            {
                public string Grade(int score)
                {
                    return {|AVIG0024:score > 90 ? "A" : {|AVIG0024:score > 70 ? "B" : "C"|}|};
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_where_no_statement_can_stand()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System;
            using System.ComponentModel;

            public class Settings
            {
                private const bool Verbose = false;
                private const int Level = Verbose ? 2 : 1;

                [DefaultValue(Verbose ? "debug" : "info")]
                public string Mode { get; set; } = "info";

                public int Retries(int attempts = Verbose ? 5 : 3)
                {
                    const int ceiling = Verbose ? 10 : 6;

                    return Math.Min(attempts, ceiling) + Level;
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_null_conditional_and_coalescing_operators()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Names
            {
                public int Length(string? name)
                {
                    return name?.Length ?? 0;
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_a_return_into_if_else()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            public class Labels
            {
                public string Label(bool member)
                {
                    return {|AVIG0024:member ? "Member" : "Guest"|};
                }
            }
            """, """
            public class Labels
            {
                public string Label(bool member)
                {
                    if (member)
                    {
                        return "Member";
                    }
                    else
                    {
                        return "Guest";
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_a_nested_false_arm_into_else_if()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            public class Grading
            {
                public string Grade(int score)
                {
                    return {|AVIG0024:score > 90 ? "A" : {|AVIG0024:score > 70 ? "B" : "C"|}|};
                }
            }
            """, """
            public class Grading
            {
                public string Grade(int score)
                {
                    if (score > 90)
                    {
                        return "A";
                    }
                    else if (score > 70)
                    {
                        return "B";
                    }
                    else
                    {
                        return "C";
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_an_assignment_and_separates_the_next_statement()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System;

            public class Pricing
            {
                private decimal _rate;

                public void Apply(bool member)
                {
                    _rate = {|AVIG0024:member ? 0.8m : 1m|};
                    Console.WriteLine(_rate);
                }
            }
            """, """
            using System;

            public class Pricing
            {
                private decimal _rate;

                public void Apply(bool member)
                {
                    if (member)
                    {
                        _rate = 0.8m;
                    }
                    else
                    {
                        _rate = 1m;
                    }

                    Console.WriteLine(_rate);
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_a_var_declaration_with_the_inferred_type()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System;

            public class Pricing
            {
                public void Print(bool member)
                {
                    var rate = {|AVIG0024:member ? 0.8m : 1m|};
                    Console.WriteLine(rate);
                }
            }
            """, """
            using System;

            public class Pricing
            {
                public void Print(bool member)
                {
                    decimal rate;
                    if (member)
                    {
                        rate = 0.8m;
                    }
                    else
                    {
                        rate = 1m;
                    }

                    Console.WriteLine(rate);
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_a_throw_arm_into_a_throw_statement()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System;

            public class Guard
            {
                public string Require(string? value)
                {
                    return {|AVIG0024:value != null ? value : throw new ArgumentNullException(nameof(value))|};
                }
            }
            """, """
            using System;

            public class Guard
            {
                public string Require(string? value)
                {
                    if (value != null)
                    {
                        return value;
                    }
                    else
                    {
                        throw new ArgumentNullException(nameof(value));
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_a_yield_return_into_if_else()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System.Collections.Generic;

            public class Labels
            {
                public IEnumerable<string> All(IEnumerable<bool> members)
                {
                    foreach (bool member in members)
                    {
                        yield return {|AVIG0024:member ? "Member" : "Guest"|};
                    }
                }
            }
            """, """
            using System.Collections.Generic;

            public class Labels
            {
                public IEnumerable<string> All(IEnumerable<bool> members)
                {
                    foreach (bool member in members)
                    {
                        if (member)
                        {
                            yield return "Member";
                        }
                        else
                        {
                            yield return "Guest";
                        }
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_an_expression_bodied_method_and_separates_the_next_member()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            public class Labels
            {
                public string Label(bool member) => {|AVIG0024:member ? "Member" : "Guest"|};
                public string Fallback() => "Guest";
            }
            """, """
            public class Labels
            {
                public string Label(bool member)
                {
                    if (member)
                    {
                        return "Member";
                    }
                    else
                    {
                        return "Guest";
                    }
                }

                public string Fallback() => "Guest";
            }
            """);
    }

    [Fact]
    public async Task Fixes_an_expression_bodied_property_into_a_getter()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            public class Labels
            {
                private bool _member;

                public string Label => {|AVIG0024:_member ? "Member" : "Guest"|};
            }
            """, """
            public class Labels
            {
                private bool _member;

                public string Label
                {
                    get
                    {
                        if (_member)
                        {
                            return "Member";
                        }
                        else
                        {
                            return "Guest";
                        }
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_an_expression_bodied_get_accessor()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            public class Labels
            {
                private bool _member;

                public string Label
                {
                    get => {|AVIG0024:_member ? "Member" : "Guest"|};
                    set => _member = value == "Member";
                }
            }
            """, """
            public class Labels
            {
                private bool _member;

                public string Label
                {
                    get
                    {
                        if (_member)
                        {
                            return "Member";
                        }
                        else
                        {
                            return "Guest";
                        }
                    }
                    set => _member = value == "Member";
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_an_expression_bodied_local_function()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System;

            public class Labels
            {
                public void Print(bool member)
                {
                    string Label(bool flag) => {|AVIG0024:flag ? "Member" : "Guest"|};

                    Console.WriteLine(Label(member));
                }
            }
            """, """
            using System;

            public class Labels
            {
                public void Print(bool member)
                {
                    string Label(bool flag)
                    {
                        if (flag)
                        {
                            return "Member";
                        }
                        else
                        {
                            return "Guest";
                        }
                    }

                    Console.WriteLine(Label(member));
                }
            }
            """);
    }

    [Fact]
    public async Task Fixes_a_lambda_into_a_block_lambda()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System.Collections.Generic;
            using System.Linq;

            public class Scores
            {
                public List<int> Clamp(List<int> values)
                {
                    return values.Select(value => {|AVIG0024:value < 0 ? 0 : value|}).ToList();
                }
            }
            """, """
            using System.Collections.Generic;
            using System.Linq;

            public class Scores
            {
                public List<int> Clamp(List<int> values)
                {
                    return values.Select(value =>
                    {
                        if (value < 0)
                        {
                            return 0;
                        }
                        else
                        {
                            return value;
                        }
                    }).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task Offers_no_fix_inside_an_argument_list()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System;

            public class Pricing
            {
                public void Print(int count)
                {
                    Console.WriteLine({|AVIG0024:count == 1 ? "item" : "items"|});
                }
            }
            """, """
            using System;

            public class Pricing
            {
                public void Print(int count)
                {
                    Console.WriteLine({|AVIG0024:count == 1 ? "item" : "items"|});
                }
            }
            """);
    }

    [Fact]
    public async Task Offers_no_fix_when_the_target_has_side_effects()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            public class Buffer
            {
                private readonly int[] _slots = new int[4];
                private int _next;

                public void Write(bool flag)
                {
                    _slots[_next++] = {|AVIG0024:flag ? 1 : 0|};
                }
            }
            """, """
            public class Buffer
            {
                private readonly int[] _slots = new int[4];
                private int _next;

                public void Write(bool flag)
                {
                    _slots[_next++] = {|AVIG0024:flag ? 1 : 0|};
                }
            }
            """);
    }
}
