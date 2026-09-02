using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<RepositoryCancellationTokenAnalyzer>;

public class RepositoryCancellationTokenTests
{
    [Fact]
    public async Task Reports_task_returning_members_without_a_trailing_token()
    {
        await Verify.VerifyAsync("""
            using System.Threading.Tasks;

            public interface IOrderRepository
            {
                Task<int> {|AVIG0008:GetAsync|}(int id);
                ValueTask {|AVIG0008:SaveAsync|}(int id);
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_token_is_the_last_parameter()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Threading;
            using System.Threading.Tasks;

            public interface IOrderRepository
            {
                Task<int> GetAsync(int id, CancellationToken cancellationToken);
                ValueTask SaveAsync(int id, CancellationToken cancellationToken);
            }
            """);
    }

    [Fact]
    public async Task Silent_for_synchronous_members()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public interface IOrderRepository
            {
                int Count();
            }
            """);
    }

    [Fact]
    public async Task Silent_for_an_interface_that_is_not_a_repository()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Threading.Tasks;

            public interface IOrderService
            {
                Task<int> GetAsync(int id);
            }
            """);
    }
}
