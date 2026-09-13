using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<CancellationTokenForwardingAnalyzer>;

public class CancellationTokenForwardingTests
{
    /// <summary>A store with an optional token, and a second one that offers the token overload instead.</summary>
    private const string StoreSource = """
        using System.Threading;
        using System.Threading.Tasks;

        public class Store
        {
            public Task<int> LoadAsync(int id, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(id);
            }
        }

        public class OverloadedStore
        {
            public Task<int> LoadAsync(int id)
            {
                return Task.FromResult(id);
            }

            public Task<int> LoadAsync(int id, CancellationToken cancellationToken)
            {
                return Task.FromResult(id);
            }
        }
        """;

    [Fact]
    public async Task Reports_an_omitted_optional_token()
    {
        await Verify.VerifySourcesAsync(StoreSource, """
            using System.Threading;
            using System.Threading.Tasks;

            public class Service
            {
                private readonly Store _store = new Store();

                public async Task<int> GetAsync(int id, CancellationToken cancellationToken)
                {
                    return await _store.{|AVIG0015:LoadAsync|}(id);
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_a_token_overload_that_was_not_chosen()
    {
        await Verify.VerifySourcesAsync(StoreSource, """
            using System.Threading;
            using System.Threading.Tasks;

            public class Service
            {
                private readonly OverloadedStore _store = new OverloadedStore();

                public async Task<int> GetAsync(int id, CancellationToken cancellationToken)
                {
                    return await _store.{|AVIG0015:LoadAsync|}(id);
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_cancellation_token_none_and_default()
    {
        // Both compile, and both leave the call exactly as uncancellable as omitting the argument.
        await Verify.VerifySourcesAsync(StoreSource, """
            using System.Threading;
            using System.Threading.Tasks;

            public class Service
            {
                private readonly Store _store = new Store();

                public async Task<int> NoneAsync(int id, CancellationToken cancellationToken)
                {
                    return await _store.{|AVIG0015:LoadAsync|}(id, CancellationToken.None);
                }

                public async Task<int> DefaultAsync(int id, CancellationToken cancellationToken)
                {
                    return await _store.{|AVIG0015:LoadAsync|}(id, default);
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_through_configure_await()
    {
        await Verify.VerifySourcesAsync(StoreSource, """
            using System.Threading;
            using System.Threading.Tasks;

            public class Service
            {
                private readonly Store _store = new Store();

                public async Task<int> GetAsync(int id, CancellationToken cancellationToken)
                {
                    return await _store.{|AVIG0015:LoadAsync|}(id).ConfigureAwait(false);
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_token_is_forwarded()
    {
        await Verify.VerifySourcesAsync(StoreSource, """
            using System.Threading;
            using System.Threading.Tasks;

            public class Service
            {
                private readonly Store _store = new Store();

                public async Task<int> GetAsync(int id, CancellationToken cancellationToken)
                {
                    return await _store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_no_token_is_in_scope()
    {
        // Nothing to forward. AVIG0008 is the rule that asks for the parameter in the first place.
        await Verify.VerifySourcesAsync(StoreSource, """
            using System.Threading.Tasks;

            public class Service
            {
                private readonly Store _store = new Store();

                public async Task<int> GetAsync(int id)
                {
                    return await _store.LoadAsync(id);
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_callee_cannot_take_a_token()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Threading;
            using System.Threading.Tasks;

            public class Service
            {
                public async Task<int> GetAsync(int id, CancellationToken cancellationToken)
                {
                    return await Task.FromResult(id);
                }
            }
            """);
    }

    [Fact]
    public async Task Finds_the_token_on_the_enclosing_method_from_inside_a_lambda()
    {
        await Verify.VerifySourcesAsync(StoreSource, """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public class Service
            {
                private readonly Store _store = new Store();

                public Task<int> GetAsync(int id, CancellationToken cancellationToken)
                {
                    Func<Task<int>> load = async () => await _store.{|AVIG0015:LoadAsync|}(id);

                    return load();
                }
            }
            """);
    }
}
