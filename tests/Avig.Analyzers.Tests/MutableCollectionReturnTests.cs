using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<MutableCollectionReturnAnalyzer>;
using VerifyFix = CSharpCodeFixVerifier<MutableCollectionReturnAnalyzer, MutableCollectionReturnCodeFixProvider>;

public class MutableCollectionReturnTests
{
    [Fact]
    public async Task Reports_methods_and_properties_returning_mutable_collections()
    {
        await Verify.VerifyAsync("""
            using System.Collections.Generic;

            public class Catalogue
            {
                public {|AVIG0022:List<int>|} Ids() => new List<int>();

                public {|AVIG0022:Dictionary<string, int>|} Counts { get; } = new Dictionary<string, int>();

                public {|AVIG0022:HashSet<string>|} Tags() => new HashSet<string>();

                public {|AVIG0022:string[]|} Names() => new string[0];
            }
            """);
    }

    [Fact]
    public async Task Reports_through_a_task()
    {
        await Verify.VerifyAsync("""
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public class Catalogue
            {
                public Task<{|AVIG0022:List<int>|}> IdsAsync() => Task.FromResult(new List<int>());
            }
            """);
    }

    [Fact]
    public async Task Reports_on_an_interface_declaration()
    {
        await Verify.VerifyAsync("""
            using System.Collections.Generic;

            public interface ICatalogue
            {
                {|AVIG0022:List<int>|} Ids();
            }
            """);
    }

    [Fact]
    public async Task Silent_for_read_only_signatures()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Collections.Generic;

            public class Catalogue
            {
                public IReadOnlyList<int> Ids() => new List<int>();

                public IReadOnlyDictionary<string, int> Counts { get; } = new Dictionary<string, int>();

                public IEnumerable<string> Tags() => new HashSet<string>();
            }
            """);
    }

    [Fact]
    public async Task Silent_for_byte_arrays()
    {
        // Hashes, streams and wire payloads. IReadOnlyList<byte> is a downgrade at every call site.
        await Verify.VerifyNoDiagnosticAsync("""
            public class Payloads
            {
                public byte[] Hash() => new byte[32];
            }
            """);
    }

    [Fact]
    public async Task Silent_for_non_public_members_and_types()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Collections.Generic;

            internal class Internal
            {
                public List<int> Ids() => new List<int>();
            }

            public class Catalogue
            {
                private List<int> Cached() => new List<int>();
            }
            """);
    }

    [Fact]
    public async Task Reports_the_declaration_but_not_the_override()
    {
        // Nothing the override can do about the type: changing it there would not compile.
        await Verify.VerifyAsync("""
            using System.Collections.Generic;

            public abstract class CatalogueBase
            {
                public abstract {|AVIG0022:List<int>|} Ids();
            }

            public class Catalogue : CatalogueBase
            {
                public override List<int> Ids() => new List<int>();
            }
            """);
    }

    [Fact]
    public async Task Silent_for_a_set_only_property()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Collections.Generic;

            public class Catalogue
            {
                private List<int> _ids = new List<int>();

                public List<int> Ids { set => _ids = value; }
            }
            """);
    }

    [Fact]
    public async Task Fixes_a_list_to_a_read_only_list()
    {
        await VerifyFix.VerifyCodeFixAsync(
            """
            using System.Collections.Generic;

            public class Catalogue
            {
                public {|AVIG0022:List<int>|} Ids() => new List<int>();
            }
            """,
            """
            using System.Collections.Generic;

            public class Catalogue
            {
                public IReadOnlyList<int> Ids() => new List<int>();
            }
            """);
    }

    [Fact]
    public async Task Fixes_an_array_and_imports_the_namespace_as_needed()
    {
        await VerifyFix.VerifyCodeFixAsync(
            """
            public class Catalogue
            {
                public {|AVIG0022:string[]|} Names() => new string[0];
            }
            """,
            """
            public class Catalogue
            {
                public System.Collections.Generic.IReadOnlyList<string> Names() => new string[0];
            }
            """);
    }
}
