using AwesomeAssertions;
using OrderApi.Application.Events;
using OrderApi.Infrastructure.Messaging;
using OrderApi.Worker;

namespace OrderApi.Test.Worker;

public class RabbitMqOptionsValidatorTests
{
    private readonly RabbitMqOptionsValidator _validator = new();

    private static RabbitMqOptions Valid() => new() { UserName = "svc", Password = "secret", UseTls = true };

    [Fact]
    public void TlsWithCredentials_IsValid() => _validator.Validate(null, Valid()).Succeeded.Should().BeTrue();

    [Fact]
    public void NoCredentialDefaults_Fails() => _validator.Validate(null, new RabbitMqOptions { UseTls = true }).Failed.Should().BeTrue();

    [Theory]
    [InlineData("", "secret")]
    [InlineData("svc", " ")]
    public void BlankCredential_Fails(string user, string password) =>
        _validator.Validate(null, new RabbitMqOptions { UserName = user, Password = password, UseTls = true }).Failed.Should().BeTrue();

    [Fact]
    public void PlainConnection_FailsUnlessAllowInsecure()
    {
        var plain = new RabbitMqOptions { UserName = "guest", Password = "guest" };

        _validator.Validate(null, plain).Failed.Should().BeTrue();
        plain.AllowInsecure = true;
        _validator.Validate(null, plain).Succeeded.Should().BeTrue();
    }
}

public class SmtpOptionsValidatorTests
{
    private readonly SmtpOptionsValidator _validator = new();

    [Fact]
    public void StartTls_IsValid() => _validator.Validate(null, new SmtpOptions { UseStartTls = true, UserName = "u", Password = "p" }).Succeeded.Should().BeTrue();

    [Fact]
    public void PlainSmtp_FailsUnlessAllowInsecure()
    {
        _validator.Validate(null, new SmtpOptions()).Failed.Should().BeTrue();
        _validator.Validate(null, new SmtpOptions { AllowInsecure = true }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void CredentialsOverPlainSmtp_FailEvenWhenAllowInsecure() =>
        _validator.Validate(null, new SmtpOptions { AllowInsecure = true, UserName = "u", Password = "p" }).Failed.Should().BeTrue();
}
