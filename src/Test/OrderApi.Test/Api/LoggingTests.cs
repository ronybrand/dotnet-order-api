using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OrderApi.Api.Middleware;

namespace OrderApi.Test.Api;

[Collection(HostlessApiCollection.Name)]
public class LoggingConfigurationTests(HostlessApiFactory factory)
{
    [Fact]
    public void ConsoleFormatter_IncludesScopes_SoTheRequestIdReachesEveryLogLine()
    {
        // SimpleConsoleFormatter reads the unnamed (current) options, which is what the Logging:Console
        // configuration section binds to.
        var options = factory.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().CurrentValue;

        options.IncludeScopes.Should().BeTrue("the correlation id lives in a logger scope, which the console only prints when scopes are included");
    }
}

public class CorrelationScopeTests
{
    [Fact]
    public async Task InvokeAsync_OpensALogScopeCarryingTheRequestId()
    {
        var logger = new ScopeRecordingLogger();
        var context = new DefaultHttpContext();
        IReadOnlyDictionary<string, object>? scopeSeenByTheRequest = null;
        var middleware = new CorrelationIdMiddleware(
            _ =>
            {
                scopeSeenByTheRequest = logger.ActiveScope;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        scopeSeenByTheRequest.Should().NotBeNull();
        scopeSeenByTheRequest!["RequestId"].Should().Be(context.TraceIdentifier);
        logger.ActiveScope.Should().BeNull("the scope must end with the request");
    }

    private sealed class ScopeRecordingLogger : ILogger<CorrelationIdMiddleware>
    {
        public IReadOnlyDictionary<string, object>? ActiveScope { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            ActiveScope = state as IReadOnlyDictionary<string, object>;
            return new Ending(this);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class Ending(ScopeRecordingLogger owner) : IDisposable
        {
            public void Dispose() => owner.ActiveScope = null;
        }
    }
}
